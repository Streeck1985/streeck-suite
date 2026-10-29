using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Voz del asistente (fase G). Charon (voz de Gemini) es la elegida por el usuario; si Gemini tarda, no tiene cupo
    /// o no hay internet, habla Piper Ald (voz neuronal local, en %LOCALAPPDATA%\StreeckSuite\piper) para que el
    /// asistente siempre conteste en voz alta. También están Piper Claude y la voz de Windows.
    /// </summary>
    internal static class VoiceOutput
    {
        private const int GeminiTimeoutMs = 8000;
        private const int MaxChars = 700;   // Las respuestas largas se leen solo hasta aquí (se ven completas en la isla)
        private static readonly string[] GeminiTtsModels = { "gemini-3.8-flash-tts", "gemini-3.1-flash-tts-preview" };

        private static readonly object Gate = new object();
        private static SoundPlayer _player;
        private static int _version;

        public static string PiperFolder
        {
            get { return Path.Combine(Path.GetDirectoryName(Log.FilePath), "piper"); }
        }

        public static bool PiperInstalled
        {
            get { return File.Exists(Path.Combine(PiperFolder, "piper.exe")); }
        }

        /// <summary>
        /// Lee el texto en voz alta en segundo plano (corta lo que se estuviera leyendo).
        /// </summary>
        public static void Speak(string text)
        {
            int version;
            lock (Gate) version = ++_version;
            Stop(false);
            if (string.IsNullOrWhiteSpace(text)) return;
            string speech = text.Length > MaxChars ? text.Substring(0, text.LastIndexOf(' ', MaxChars) > 0 ? text.LastIndexOf(' ', MaxChars) : MaxChars) + "…" : text;

            ThreadPool.QueueUserWorkItem(state =>
            {
                string engine;
                byte[] wav = Synthesize(speech, SuiteSettings.AssistantVoice, out engine);
                lock (Gate)
                {
                    if (wav == null || version != _version) return; // Ya se pidió otra cosa
                    _player = new SoundPlayer(new MemoryStream(wav));
                    _player.Play();
                }
            });
        }

        /// <summary>
        /// Genera el audio (WAV) con la voz elegida o su respaldo. engine dice cuál habló al final.
        /// </summary>
        internal static byte[] Synthesize(string speech, SuiteSettings.AssistantVoiceKind voice, out string engine)
        {
            byte[] wav = null;
            engine = null;
                try
                {
                    if (voice == SuiteSettings.AssistantVoiceKind.GeminiCharon && !string.IsNullOrEmpty(SuiteSettings.GeminiApiKey))
                    {
                        wav = GeminiSpeech(speech, "Charon");
                        engine = "Charon";
                        if (wav == null) voice = SuiteSettings.AssistantVoiceKind.PiperAld; // Respaldo local
                    }
                    else if (voice == SuiteSettings.AssistantVoiceKind.GeminiCharon)
                    {
                        voice = SuiteSettings.AssistantVoiceKind.PiperAld;
                    }
                    if (wav == null && voice != SuiteSettings.AssistantVoiceKind.Windows && PiperInstalled)
                    {
                        engine = voice == SuiteSettings.AssistantVoiceKind.PiperClaude ? "es_MX-claude-high" : "es_MX-ald-medium";
                        wav = PiperSpeech(speech, engine);
                    }
                    if (wav == null) { engine = "Windows"; wav = WindowsSpeech(speech); }
                }
                catch (Exception ex)
                {
                    Log.Error("[VoiceOutput] {0}", ex.Message);
                }
            return wav;
        }

        public static void Stop()
        {
            lock (Gate) _version++;
            Stop(true);
        }

        private static void Stop(bool all)
        {
            lock (Gate)
            {
                if (_player != null) { _player.Stop(); _player.Dispose(); _player = null; }
            }
        }

        #region Charon (Gemini)

        private static byte[] GeminiSpeech(string text, string voiceName)
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            string body = json.Serialize(new Dictionary<string, object>
            {
                { "contents", new object[] { new Dictionary<string, object> { { "parts", new object[] { new Dictionary<string, object> { { "text", "Lee con tono amable y natural, acento de México: " + text } } } } } } },
                { "generationConfig", new Dictionary<string, object>
                    {
                        { "responseModalities", new[] { "AUDIO" } },
                        { "speechConfig", new Dictionary<string, object> { { "voiceConfig", new Dictionary<string, object> { { "prebuiltVoiceConfig", new Dictionary<string, object> { { "voiceName", voiceName } } } } } } }
                    }
                }
            });

            foreach (string model in GeminiTtsModels)
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://generativelanguage.googleapis.com/v1beta/models/" + model + ":generateContent");
                    request.Method = "POST";
                    request.ContentType = "application/json; charset=utf-8";
                    request.Headers["x-goog-api-key"] = SuiteSettings.GeminiApiKey;
                    request.Timeout = GeminiTimeoutMs;
                    request.ReadWriteTimeout = GeminiTimeoutMs;
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                    using (WebResponse response = request.GetResponse())
                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        Dictionary<string, object> data = json.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                        object[] candidates = data["candidates"] as object[];
                        Dictionary<string, object> content = ((Dictionary<string, object>)candidates[0])["content"] as Dictionary<string, object>;
                        Dictionary<string, object> part = (Dictionary<string, object>)((object[])content["parts"])[0];
                        Dictionary<string, object> inline = (Dictionary<string, object>)part["inlineData"];
                        string mime = inline["mimeType"] as string ?? "";
                        int rate = 24000;
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(mime, @"rate=(\d+)");
                        if (m.Success) rate = int.Parse(m.Groups[1].Value);
                        return Wav(Convert.FromBase64String((string)inline["data"]), rate);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[VoiceOutput] Charon ({0}) no disponible: {1}; se prueba la siguiente opción", model, ex.Message);
                }
            }
            return null;
        }

        // PCM de 16 bits mono -> archivo WAV en memoria
        private static byte[] Wav(byte[] pcm, int rate)
        {
            using (MemoryStream memory = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(memory))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Length); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length); w.Write(pcm);
                w.Flush();
                return memory.ToArray();
            }
        }

        #endregion

        #region Piper (local)

        private static byte[] PiperSpeech(string text, string model)
        {
            string output = Path.Combine(Path.GetTempPath(), "streeck_voz.wav");
            ProcessStartInfo info = new ProcessStartInfo(Path.Combine(PiperFolder, "piper.exe"),
                string.Format("-m \"{0}\" -f \"{1}\"", Path.Combine(PiperFolder, model + ".onnx"), output))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                WorkingDirectory = PiperFolder
            };
            using (Process process = Process.Start(info))
            {
                process.ErrorDataReceived += (s, e) => { };
                process.BeginErrorReadLine();
                // Piper lee el texto en UTF-8 por la entrada estándar (con la página de códigos de Windows saldrían mal las tildes)
                byte[] bytes = Encoding.UTF8.GetBytes(text.Replace("\r", " ").Replace("\n", " ") + "\n");
                process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                process.StandardInput.Close();
                if (!process.WaitForExit(20000)) { process.Kill(); return null; }
            }
            if (!File.Exists(output)) return null;
            byte[] wav = File.ReadAllBytes(output);
            try { File.Delete(output); } catch (IOException) { }
            return wav;
        }

        #endregion

        // Último recurso: la voz de Windows (Sabina)
        private static byte[] WindowsSpeech(string text)
        {
            using (System.Speech.Synthesis.SpeechSynthesizer synth = new System.Speech.Synthesis.SpeechSynthesizer())
            using (MemoryStream memory = new MemoryStream())
            {
                foreach (System.Speech.Synthesis.InstalledVoice voice in synth.GetInstalledVoices())
                {
                    if (voice.Enabled && voice.VoiceInfo.Culture.TwoLetterISOLanguageName == "es") { synth.SelectVoice(voice.VoiceInfo.Name); break; }
                }
                synth.SetOutputToWaveStream(memory);
                synth.Speak(text);
                return memory.ToArray();
            }
        }
    }
}
