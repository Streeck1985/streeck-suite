using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Dictado del asistente (fase G) como Alexa o Gemini: graba el micrófono predeterminado, detecta sola cuando el
    /// usuario empieza y deja de hablar (por el volumen) y convierte la voz en texto con Whisper en el propio PC
    /// (whisper.cpp, sin internet ni ventanas de Windows). Los avisos llegan en el hilo de la interfaz.
    /// </summary>
    internal class VoiceInput : IDisposable
    {
        private const int SampleRate = 16000;          // Lo que espera Whisper: 16 kHz, mono, 16 bits
        private const int FrameMs = 20;
        private const int CalibrationMs = 300;         // Ruido de fondo medido al empezar
        private const int EndSilenceMs = 900;          // Silencio tras hablar = fin de la frase
        private const int NoSpeechMs = 7000;           // Si no se habla en este tiempo, se avisa
        private const int MaxRecordingMs = 15000;
        private const float MinThreshold = 0.012f;     // Volumen mínimo para contar como voz

        private readonly Control _host;
        private volatile bool _cancel;
        private volatile float _level;

        /// <summary>
        /// Terminó de grabar y está convirtiendo la voz en texto.
        /// </summary>
        public event Action Transcribing;

        /// <summary>
        /// Frase reconocida, o null si se canceló.
        /// </summary>
        public event Action<string> Finished;

        /// <summary>
        /// Motivo legible si no se pudo.
        /// </summary>
        public event Action<string> Failed;

        public VoiceInput(Control host)
        {
            _host = host;
        }

        public static string WhisperFolder
        {
            get { return Path.Combine(Path.GetDirectoryName(Log.FilePath), "whisper"); }
        }

        public static string WhisperExe
        {
            get { return Path.Combine(WhisperFolder, "whisper-cli.exe"); }
        }

        public static string WhisperModel
        {
            get { return Path.Combine(WhisperFolder, "ggml-small-q5_1.bin"); }
        }

        public static bool IsInstalled
        {
            get { return File.Exists(WhisperExe) && File.Exists(WhisperModel); }
        }

        /// <summary>
        /// Volumen actual del micrófono (0..1) mientras se graba, para las ondas.
        /// </summary>
        public float Level
        {
            get { return _level; }
        }

        public void Start()
        {
            if (!IsInstalled)
            {
                Post(() => Raise(Failed, "Falta Whisper para el dictado. Ve a Ajustes → Asistente IA."));
                return;
            }
            _cancel = false;
            Thread thread = new Thread(Record) { IsBackground = true, Name = "VoiceInput" };
            thread.Start();
        }

        public void Cancel()
        {
            _cancel = true;
        }

        #region Grabación con detección de voz

        private void Record()
        {
            List<short> samples = new List<short>(SampleRate * 15);
            CoreAudio.IAudioClient client = null;
            CoreAudio.IAudioCaptureClient capture = null;
            try
            {
                int channels, rate;
                bool isFloat;
                if (!OpenMicrophone(out client, out capture, out channels, out rate, out isFloat))
                {
                    Post(() => Raise(Failed, "Windows no encontró el micrófono."));
                    return;
                }

                double resampleStep = (double)rate / SampleRate, resamplePos = 0;
                int frameSamples = SampleRate * FrameMs / 1000;
                double frameEnergy = 0;
                int frameCount = 0, totalFrames = 0, speechFrames = 0, silentFrames = 0;
                double noiseFloor = 0;
                float threshold = MinThreshold;
                bool speaking = false;
                byte[] buffer = new byte[0];
                float previous = 0;

                while (!_cancel)
                {
                    Thread.Sleep(FrameMs);
                    uint packet;
                    if (capture.GetNextPacketSize(out packet) != 0) break;
                    while (packet > 0)
                    {
                        IntPtr data;
                        uint frames;
                        int flags;
                        ulong devicePosition, qpcPosition;
                        if (capture.GetBuffer(out data, out frames, out flags, out devicePosition, out qpcPosition) != 0) break;
                        int bytesPerSample = isFloat ? 4 : 2;
                        int bytes = (int)frames * channels * bytesPerSample;
                        if (buffer.Length < bytes) buffer = new byte[bytes];
                        bool silent = (flags & CoreAudio.AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                        if (!silent) Marshal.Copy(data, buffer, 0, bytes);
                        capture.ReleaseBuffer(frames);

                        // A mono y a 16 kHz (interpolación lineal), midiendo el volumen cada 20 ms
                        for (int f = 0; f < frames; f++)
                        {
                            float sum = 0;
                            if (!silent)
                            {
                                for (int c = 0; c < channels; c++)
                                {
                                    int at = (f * channels + c) * bytesPerSample;
                                    sum += isFloat ? BitConverter.ToSingle(buffer, at) : BitConverter.ToInt16(buffer, at) / 32768f;
                                }
                            }
                            float sample = sum / channels;
                            resamplePos += 1;
                            while (resamplePos >= resampleStep)
                            {
                                resamplePos -= resampleStep;
                                float value = previous + (sample - previous) * (float)(1 - resamplePos / resampleStep);
                                samples.Add((short)Math.Max(short.MinValue, Math.Min(short.MaxValue, value * 32767)));
                                frameEnergy += value * value;
                                if (++frameCount < frameSamples) continue;

                                // Fin de un bloque de 20 ms: ¿voz o silencio?
                                float rms = (float)Math.Sqrt(frameEnergy / frameCount);
                                frameEnergy = 0;
                                frameCount = 0;
                                totalFrames++;
                                _level = Math.Min(1f, rms * 6);
                                if (totalFrames * FrameMs <= CalibrationMs)
                                {
                                    noiseFloor += rms / (CalibrationMs / FrameMs);
                                    threshold = Math.Max(MinThreshold, (float)noiseFloor * 3.5f);
                                    continue;
                                }
                                if (rms > threshold) { speechFrames++; silentFrames = 0; if (speechFrames >= 3) speaking = true; }
                                else { silentFrames++; if (!speaking) speechFrames = 0; }
                            }
                            previous = sample;
                        }
                        if (capture.GetNextPacketSize(out packet) != 0) break;
                    }

                    int elapsedMs = totalFrames * FrameMs;
                    if (speaking && silentFrames * FrameMs >= EndSilenceMs) break;   // Terminó de hablar
                    if (!speaking && elapsedMs >= NoSpeechMs)
                    {
                        Post(() => Raise(Failed, "No te escuché. Habla un poco más cerca del micrófono."));
                        return;
                    }
                    if (elapsedMs >= MaxRecordingMs) break;
                }
                _level = 0;
                if (_cancel) { Post(() => Raise(Finished, null)); return; }

                Post(() => { if (Transcribing != null) Transcribing(); });
                string text = Transcribe(samples);
                Log.Info("[VoiceInput] Whisper: «{0}»", text);
                if (string.IsNullOrEmpty(text)) Post(() => Raise(Failed, "No te entendí. Prueba otra vez."));
                else Post(() => Raise(Finished, text));
            }
            catch (Exception ex)
            {
                Log.Error("[VoiceInput] {0}", ex.Message);
                Post(() => Raise(Failed, "No se pudo usar el micrófono: " + ex.Message));
            }
            finally
            {
                _level = 0;
                if (client != null) { client.Stop(); Marshal.ReleaseComObject(client); }
                if (capture != null) Marshal.ReleaseComObject(capture);
            }
        }

        private static bool OpenMicrophone(out CoreAudio.IAudioClient client, out CoreAudio.IAudioCaptureClient capture, out int channels, out int rate, out bool isFloat)
        {
            client = null;
            capture = null;
            channels = rate = 0;
            isFloat = false;
            CoreAudio.IMMDeviceEnumerator enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumeratorComObject();
            CoreAudio.IMMDevice device = null;
            IntPtr format = IntPtr.Zero;
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Capture, CoreAudio.ERole.Console, out device) != 0 || device == null) return false;
                Guid iid = CoreAudio.IID_IAudioClient;
                object instance;
                if (device.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out instance) != 0) return false;
                client = (CoreAudio.IAudioClient)instance;
                if (client.GetMixFormat(out format) != 0) return false;

                int tag = (ushort)Marshal.ReadInt16(format, 0);
                channels = (ushort)Marshal.ReadInt16(format, 2);
                rate = Marshal.ReadInt32(format, 4);
                int bits = (ushort)Marshal.ReadInt16(format, 14);
                if (tag == 0xFFFE) tag = Marshal.ReadInt32(format, 24);
                isFloat = tag == 3 && bits == 32;
                if (!isFloat && !(tag == 1 && bits == 16)) return false;

                if (client.Initialize(CoreAudio.AUDCLNT_SHAREMODE_SHARED, 0, 2000000, 0, format, IntPtr.Zero) != 0) return false; // 200 ms de búfer
                Guid captureIid = CoreAudio.IID_IAudioCaptureClient;
                object service;
                if (client.GetService(ref captureIid, out service) != 0) return false;
                capture = (CoreAudio.IAudioCaptureClient)service;
                return client.Start() == 0;
            }
            finally
            {
                if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
                if (device != null) Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
        }

        #endregion

        #region Whisper

        private static string Transcribe(List<short> samples)
        {
            string wav = Path.Combine(Path.GetTempPath(), "streeck_dictado.wav");
            WriteWav(wav, samples);
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(WhisperExe,
                    string.Format("-m \"{0}\" -f \"{1}\" -l es -nt -np -t {2}", WhisperModel, wav, Math.Max(2, Environment.ProcessorCount / 2)))
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    WorkingDirectory = WhisperFolder
                };
                using (Process process = Process.Start(info))
                {
                    process.ErrorDataReceived += (s, e) => { }; // Whisper escribe su progreso aquí: se descarta
                    process.BeginErrorReadLine();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(30000);
                    return Clean(output);
                }
            }
            finally
            {
                try { File.Delete(wav); } catch (IOException) { }
            }
        }

        // Quita las marcas que Whisper pone al oír silencio o ruido ("[BLANK_AUDIO]", "(música)"...)
        private static string Clean(string text)
        {
            string clean = System.Text.RegularExpressions.Regex.Replace(text ?? "", @"\[[^\]]*\]|\([^)]*\)", " ");
            return System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();
        }

        private static void WriteWav(string path, List<short> samples)
        {
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                int dataBytes = samples.Count * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataBytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataBytes);
                foreach (short s in samples) w.Write(s);
            }
        }

        #endregion

        private void Post(Action action)
        {
            if (_host.IsDisposed || !_host.IsHandleCreated) return;
            try { _host.BeginInvoke(action); } catch (InvalidOperationException) { /* Cerrándose */ }
        }

        private static void Raise(Action<string> handler, string value)
        {
            if (handler != null) handler(value);
        }

        public void Dispose()
        {
            _cancel = true;
        }
    }
}
