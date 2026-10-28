using System;
using System.Runtime.InteropServices;
using System.Threading;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Ecualizador real: escucha lo que suena por el dispositivo de salida predeterminado (captura "loopback", sin
    /// micrófono), calcula el espectro con una FFT y lo reparte en 5 bandas de graves a agudos (0..1).
    /// Solo captura mientras alguien lee Levels (la isla al dibujar barras); 2 s sin lecturas y se detiene solo.
    /// </summary>
    internal static class AudioSpectrum
    {
        public const int Bands = 5;

        private const int FftSize = 1024;
        private const int IdleStopMs = 2000;
        private const int FrameMs = 15;
        private const int DeviceCheckMs = 3000;
        private const float RangeDb = 30f;            // Margen dinámico de cada barra por debajo de su pico reciente
        private const float SilenceDb = -75f;         // Por debajo de esto la banda cuenta como callada

        // Bordes de las bandas en Hz: bombo, graves, voz, presencia, platillos
        private static readonly float[] BandEdges = { 40, 150, 400, 1200, 4000, 12000 };

        private static readonly object Gate = new object();
        private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private static Thread _thread;
        private static int _lastDemand;
        private static volatile float[] _levels = new float[Bands];
        private static volatile bool _capturing;

        // Estado del hilo de captura
        private static CoreAudio.IAudioClient _client;
        private static CoreAudio.IAudioCaptureClient _capture;
        private static string _deviceId;
        private static int _channels, _bytesPerSample, _sampleRate;
        private static bool _isFloat;
        private static byte[] _packet = new byte[0];
        private static readonly float[] Ring = new float[FftSize];
        private static int _ringPos;
        private static int _lastPacketTick;
        private static readonly float[] Re = new float[FftSize], Im = new float[FftSize], Window = BuildWindow();
        private static readonly float[] PeakDb = new float[Bands], Shown = new float[Bands];

        /// <summary>
        /// Nivel actual de cada banda (0..1). Leerlo mantiene viva la captura.
        /// </summary>
        public static float[] Levels
        {
            get
            {
                _lastDemand = Environment.TickCount;
                lock (Gate)
                {
                    if (_thread == null)
                    {
                        _thread = new Thread(Run) { IsBackground = true, Name = "AudioSpectrum", Priority = ThreadPriority.BelowNormal };
                        _thread.Start();
                    }
                }
                Wake.Set();
                return _levels;
            }
        }

        /// <summary>
        /// Falso si Windows no deja capturar la salida (las barras usan entonces la animación de reserva).
        /// </summary>
        public static bool IsCapturing
        {
            get { return _capturing; }
        }

        private static void Run()
        {
            int lastDeviceCheck = Environment.TickCount;
            while (true)
            {
                try
                {
                    if (Environment.TickCount - _lastDemand > IdleStopMs)
                    {
                        StopCapture();
                        _levels = new float[Bands];
                        Array.Clear(Shown, 0, Bands);
                        Wake.WaitOne();
                        continue;
                    }

                    if (_client == null && !StartCapture())
                    {
                        Thread.Sleep(DeviceCheckMs);
                        continue;
                    }

                    if (Environment.TickCount - lastDeviceCheck > DeviceCheckMs)
                    {
                        lastDeviceCheck = Environment.TickCount;
                        if (DefaultDeviceId() != _deviceId)
                        {
                            StopCapture();
                            continue;
                        }
                    }

                    if (!ReadPackets())
                    {
                        StopCapture(); // Dispositivo desconectado o invalidado: se reintenta en la siguiente vuelta
                        continue;
                    }

                    // Sin paquetes durante un rato = no suena nada (el loopback no entrega silencio)
                    if (Environment.TickCount - _lastPacketTick > 100) Array.Clear(Ring, 0, FftSize);
                    Analyze();
                }
                catch (COMException ex)
                {
                    Log.Error("[AudioSpectrum] {0}", ex.Message);
                    StopCapture();
                    Thread.Sleep(DeviceCheckMs);
                }
                Thread.Sleep(FrameMs);
            }
        }

        private static string DefaultDeviceId()
        {
            CoreAudio.IMMDeviceEnumerator enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumeratorComObject();
            try
            {
                CoreAudio.IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Render, CoreAudio.ERole.Multimedia, out device) != 0 || device == null) return null;
                string id;
                device.GetId(out id);
                Marshal.ReleaseComObject(device);
                return id;
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }

        private static bool StartCapture()
        {
            CoreAudio.IMMDeviceEnumerator enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumeratorComObject();
            CoreAudio.IMMDevice device = null;
            IntPtr format = IntPtr.Zero;
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Render, CoreAudio.ERole.Multimedia, out device) != 0 || device == null) return false;
                device.GetId(out _deviceId);

                Guid iid = CoreAudio.IID_IAudioClient;
                object instance;
                if (device.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out instance) != 0) return false;
                _client = (CoreAudio.IAudioClient)instance;

                if (_client.GetMixFormat(out format) != 0 || !ReadFormat(format)) return Fail("formato de audio no compatible");
                // 100 ms de búfer (en unidades de 100 ns)
                int hr = _client.Initialize(CoreAudio.AUDCLNT_SHAREMODE_SHARED, CoreAudio.AUDCLNT_STREAMFLAGS_LOOPBACK, 1000000, 0, format, IntPtr.Zero);
                if (hr != 0) return Fail(string.Format("Initialize 0x{0:X8}", hr));

                Guid captureIid = CoreAudio.IID_IAudioCaptureClient;
                object service;
                if (_client.GetService(ref captureIid, out service) != 0) return Fail("GetService");
                _capture = (CoreAudio.IAudioCaptureClient)service;
                if (_client.Start() != 0) return Fail("Start");

                for (int b = 0; b < Bands; b++) PeakDb[b] = SilenceDb + RangeDb;
                _lastPacketTick = Environment.TickCount;
                _capturing = true;
                Log.Info("[AudioSpectrum] Escuchando la salida: {0} Hz, {1} canales, {2}", _sampleRate, _channels, _isFloat ? "float" : "16 bits");
                return true;
            }
            finally
            {
                if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
                if (device != null) Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
        }

        private static bool Fail(string reason)
        {
            Log.Error("[AudioSpectrum] No se pudo escuchar la salida: {0}", reason);
            StopCapture();
            return false;
        }

        // WAVEFORMATEX (y WAVEFORMATEXTENSIBLE): solo se aceptan float de 32 bits y PCM de 16 bits
        private static bool ReadFormat(IntPtr format)
        {
            int tag = (ushort)Marshal.ReadInt16(format, 0);
            _channels = (ushort)Marshal.ReadInt16(format, 2);
            _sampleRate = Marshal.ReadInt32(format, 4);
            int bits = (ushort)Marshal.ReadInt16(format, 14);
            if (tag == 0xFFFE) tag = Marshal.ReadInt32(format, 24); // Primeros 4 bytes del SubFormat: 1 = PCM, 3 = float
            _isFloat = tag == 3;
            _bytesPerSample = bits / 8;
            return _channels > 0 && ((_isFloat && bits == 32) || (tag == 1 && bits == 16));
        }

        private static void StopCapture()
        {
            _capturing = false;
            if (_capture != null) { Marshal.ReleaseComObject(_capture); _capture = null; }
            if (_client != null)
            {
                _client.Stop();
                Marshal.ReleaseComObject(_client);
                _client = null;
            }
        }

        // Copia los paquetes pendientes al anillo de muestras (mezcla los canales a mono)
        private static bool ReadPackets()
        {
            uint packetFrames;
            if (_capture.GetNextPacketSize(out packetFrames) != 0) return false;
            while (packetFrames > 0)
            {
                IntPtr data;
                uint frames;
                int flags;
                ulong devicePosition, qpcPosition;
                if (_capture.GetBuffer(out data, out frames, out flags, out devicePosition, out qpcPosition) != 0) return false;

                int frameBytes = _channels * _bytesPerSample;
                int bytes = (int)frames * frameBytes;
                bool silent = (flags & CoreAudio.AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                if (!silent)
                {
                    if (_packet.Length < bytes) _packet = new byte[bytes];
                    Marshal.Copy(data, _packet, 0, bytes);
                }
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0;
                    if (!silent)
                    {
                        for (int c = 0; c < _channels; c++)
                        {
                            int at = f * frameBytes + c * _bytesPerSample;
                            sum += _isFloat ? BitConverter.ToSingle(_packet, at) : BitConverter.ToInt16(_packet, at) / 32768f;
                        }
                    }
                    Ring[_ringPos] = sum / _channels;
                    _ringPos = (_ringPos + 1) % FftSize;
                }

                _capture.ReleaseBuffer(frames);
                _lastPacketTick = Environment.TickCount;
                if (_capture.GetNextPacketSize(out packetFrames) != 0) return false;
            }
            return true;
        }

        private static void Analyze()
        {
            for (int i = 0; i < FftSize; i++)
            {
                Re[i] = Ring[(_ringPos + i) % FftSize] * Window[i];
                Im[i] = 0;
            }
            Fft(Re, Im);

            float binHz = (float)_sampleRate / FftSize;
            float[] next = new float[Bands];
            for (int b = 0; b < Bands; b++)
            {
                int from = Math.Max(1, (int)(BandEdges[b] / binHz));
                int to = Math.Min(FftSize / 2 - 1, Math.Max(from, (int)(BandEdges[b + 1] / binHz)));
                double energy = 0;
                for (int k = from; k <= to; k++) energy += Re[k] * Re[k] + Im[k] * Im[k];
                float db = (float)(10 * Math.Log10(energy / (to - from + 1) + 1e-12)) - 20 * (float)Math.Log10(FftSize / 4.0);

                // Cada barra se mide contra su propio pico reciente, que baja despacio: así los agudos también bailan
                PeakDb[b] = Math.Max(Math.Max(db, PeakDb[b] - 0.12f), SilenceDb + RangeDb);
                float target = db <= SilenceDb ? 0 : Math.Max(0, Math.Min(1, (db - (PeakDb[b] - RangeDb)) / RangeDb));

                // Sube rápido con el golpe y cae suave
                Shown[b] = target > Shown[b] ? Shown[b] + (target - Shown[b]) * 0.7f : Math.Max(target, Shown[b] - 0.05f);
                next[b] = Shown[b];
            }
            _levels = next;
        }

        private static float[] BuildWindow()
        {
            float[] window = new float[FftSize];
            for (int i = 0; i < FftSize; i++) window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
            return window;
        }

        // FFT radix-2 en el sitio (Cooley-Tukey)
        private static void Fft(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    float t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2 * Math.PI / len;
                float wRe = (float)Math.Cos(angle), wIm = (float)Math.Sin(angle);
                for (int i = 0; i < n; i += len)
                {
                    float curRe = 1, curIm = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        float tRe = re[b] * curRe - im[b] * curIm;
                        float tIm = re[b] * curIm + im[b] * curRe;
                        re[b] = re[a] - tRe; im[b] = im[a] - tIm;
                        re[a] += tRe; im[a] += tIm;
                        float nextRe = curRe * wRe - curIm * wIm;
                        curIm = curRe * wIm + curIm * wRe;
                        curRe = nextRe;
                    }
                }
            }
        }
    }
}
