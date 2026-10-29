using System;
using System.Runtime.InteropServices;
using System.Threading;
using StreeckSuite.Core;
using Windows.Foundation;
using Windows.Media.SpeechRecognition;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Dictado para el asistente (fase G) con el reconocimiento de voz de Windows (el mismo de Win + H), en el idioma
    /// del sistema. Escucha una frase: el texto parcial llega mientras se habla y la frase completa al callarse.
    /// Necesita "Reconocimiento de voz en línea" activado en Configuración → Privacidad → Voz.
    /// </summary>
    internal class VoiceInput
    {
        private IAsyncOperation<SpeechRecognitionResult> _operation;
        private SpeechRecognizer _recognizer;

        /// <summary>
        /// Texto provisional mientras se habla (hilo de segundo plano).
        /// </summary>
        public event Action<string> Partial;

        /// <summary>
        /// Frase reconocida, o null si no se entendió nada o se canceló (hilo de segundo plano).
        /// </summary>
        public event Action<string> Finished;

        /// <summary>
        /// Motivo legible cuando el dictado no puede empezar (hilo de segundo plano).
        /// </summary>
        public event Action<string> Failed;

        public void Start()
        {
            ThreadPool.QueueUserWorkItem(state => Listen());
        }

        public void Cancel()
        {
            IAsyncOperation<SpeechRecognitionResult> operation = _operation;
            if (operation != null)
            {
                try { operation.Cancel(); } catch (Exception) { /* Ya terminó */ }
            }
        }

        private void Listen()
        {
            try
            {
                _recognizer = new SpeechRecognizer(SpeechRecognizer.SystemSpeechLanguage);
                _recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "asistente"));
                _recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.2);
                _recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(6);
                SpeechRecognitionCompilationResult compiled = WinRtAsync.Wait(_recognizer.CompileConstraintsAsync());
                if (compiled.Status != SpeechRecognitionResultStatus.Success)
                {
                    Raise(Failed, "El dictado de Windows no está disponible (" + compiled.Status + ")");
                    return;
                }

                _recognizer.HypothesisGenerated += (sender, args) => Raise(Partial, args.Hypothesis.Text);
                _operation = _recognizer.RecognizeAsync();
                SpeechRecognitionResult result = WinRtAsync.Wait(_operation, TimeSpan.FromSeconds(60));
                string text = result.Status == SpeechRecognitionResultStatus.Success ? result.Text : null;
                Raise(Finished, string.IsNullOrWhiteSpace(text) ? null : text);
            }
            catch (OperationCanceledException)
            {
                Raise(Finished, null);
            }
            catch (Exception ex)
            {
                Log.Error("[VoiceInput] {0} (0x{1:X8})", ex.Message, ex.HResult);
                // 0x80045509: la privacidad de voz en línea está desactivada
                Raise(Failed, ex.HResult == unchecked((int)0x80045509)
                    ? "Activa «Reconocimiento de voz en línea» en Configuración → Privacidad y seguridad → Voz"
                    : "No se pudo usar el micrófono: " + ex.Message);
            }
            finally
            {
                _operation = null;
                if (_recognizer != null) { _recognizer.Dispose(); _recognizer = null; }
            }
        }

        private static void Raise(Action<string> handler, string value)
        {
            if (handler != null) handler(value);
        }

        #region Nivel del micrófono

        private static CoreAudio.IAudioMeterInformation _meter;

        /// <summary>
        /// Volumen de pico del micrófono predeterminado (0..1), para dibujar las ondas mientras se habla.
        /// </summary>
        public static float MicLevel
        {
            get
            {
                try
                {
                    if (_meter == null)
                    {
                        CoreAudio.IMMDeviceEnumerator enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumeratorComObject();
                        CoreAudio.IMMDevice device;
                        if (enumerator.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Capture, CoreAudio.ERole.Communications, out device) == 0 && device != null)
                        {
                            Guid iid = CoreAudio.IID_IAudioMeterInformation;
                            object instance;
                            if (device.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out instance) == 0) _meter = (CoreAudio.IAudioMeterInformation)instance;
                            Marshal.ReleaseComObject(device);
                        }
                        Marshal.ReleaseComObject(enumerator);
                    }
                    float peak;
                    return _meter != null && _meter.GetPeakValue(out peak) == 0 ? peak : 0;
                }
                catch (COMException)
                {
                    _meter = null; // Se desconectó el micrófono: se vuelve a buscar la próxima vez
                    return 0;
                }
            }
        }

        #endregion
    }
}
