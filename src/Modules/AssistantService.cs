using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Asistente de IA (fase G): conversación con la IA local (Ollama, en la tarjeta gráfica) o con Gemini (nivel
    /// gratis con clave de Google AI Studio). Las respuestas llegan por trozos para escribirse en vivo.
    /// </summary>
    internal static class AssistantService
    {
        private const string OllamaUrl = "http://127.0.0.1:11434";
        private const string GeminiUrl = "https://generativelanguage.googleapis.com/v1beta/";
        private const int MaxTurns = 6;   // Preguntas anteriores que se recuerdan en la conversación

        private class Turn
        {
            public bool FromUser;
            public string Text;
        }

        private static readonly object Gate = new object();
        private static readonly List<Turn> History = new List<Turn>();
        private static string _geminiModel;
        private static int _requestVersion;
        private static string _activeApp;  // App que se usaba al abrir el asistente

        /// <summary>
        /// Se llama justo antes de mostrar el asistente, mientras la app del usuario aún está en primer plano.
        /// </summary>
        public static void RememberActiveApp()
        {
            _activeApp = ActiveAppName();
        }

        /// <summary>
        /// Nombre del motor para el pie de la tarjeta ("Local · llama3.1:8b · en tu PC").
        /// </summary>
        public static string EngineLabel
        {
            get
            {
                return SuiteSettings.AssistantEngine == SuiteSettings.AssistantEngineKind.Gemini
                    ? "Gemini · " + (_geminiModel ?? "nube de Google")
                    : "Local · " + SuiteSettings.OllamaModel + " · en tu PC";
            }
        }

        public static void NewConversation()
        {
            lock (Gate)
            {
                History.Clear();
                _requestVersion++; // Una respuesta que siga llegando de la conversación anterior se descarta
            }
        }

        /// <summary>
        /// Pregunta en segundo plano. onChunk recibe cada trozo de texto; onDone el texto completo o null si falló
        /// (con el motivo en onError). Todo llega en un hilo de segundo plano.
        /// </summary>
        public static void Ask(string question, string context, Action<string> onChunk, Action<string> onDone, Action<string> onError)
        {
            int version;
            List<Turn> history;
            lock (Gate)
            {
                version = ++_requestVersion;
                history = new List<Turn>(History);
            }
            string prompt = string.IsNullOrEmpty(context) ? question : question + "\n\n---\n" + context;

            ThreadPool.QueueUserWorkItem(state =>
            {
                StringBuilder answer = new StringBuilder();
                Action<string> chunk = text =>
                {
                    if (version != _requestVersion) throw new OperationCanceledException();
                    answer.Append(text);
                    onChunk(text);
                };
                try
                {
                    if (SuiteSettings.AssistantEngine == SuiteSettings.AssistantEngineKind.Gemini) AskGemini(history, prompt, chunk);
                    else AskOllama(history, prompt, chunk);

                    lock (Gate)
                    {
                        if (version != _requestVersion) return;
                        History.Add(new Turn { FromUser = true, Text = prompt });
                        History.Add(new Turn { FromUser = false, Text = answer.ToString() });
                        while (History.Count > MaxTurns * 2) History.RemoveAt(0);
                    }
                    onDone(answer.ToString());
                }
                catch (OperationCanceledException)
                {
                    // Se hizo otra pregunta o se empezó una conversación nueva
                }
                catch (Exception ex)
                {
                    Log.Error("[AssistantService] {0}", ex.Message);
                    if (version == _requestVersion) onError(FriendlyError(ex));
                }
            });
        }

        private static string SystemPrompt()
        {
            string app = _activeApp;
            return "Eres el asistente de Streeck Suite, integrado en la barra superior de Windows del usuario. " +
                   "Responde siempre en español, de forma breve y clara (normalmente 1 a 4 frases), sin rodeos. " +
                   "Usa texto plano: sin tablas ni encabezados. " +
                   "Hoy es " + DateTime.Now.ToString("dddd d 'de' MMMM 'de' yyyy, HH:mm", new System.Globalization.CultureInfo("es-MX")) + ". " +
                   (app != null ? "El usuario está usando la app «" + app + "». " : "") +
                   "Si el usuario incluye un texto después de una línea ---, es lo que tiene copiado: úsalo para responder.";
        }

        #region Ollama (local)

        private static void AskOllama(List<Turn> history, string prompt, Action<string> chunk)
        {
            List<object> messages = new List<object>();
            messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", SystemPrompt() } });
            foreach (Turn turn in history) messages.Add(new Dictionary<string, object> { { "role", turn.FromUser ? "user" : "assistant" }, { "content", turn.Text } });
            messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", prompt } });
            Dictionary<string, object> body = new Dictionary<string, object> { { "model", SuiteSettings.OllamaModel }, { "messages", messages }, { "stream", true } };

            JavaScriptSerializer json = new JavaScriptSerializer();
            foreach (string line in PostLines(OllamaUrl + "/api/chat", json.Serialize(body), null))
            {
                Dictionary<string, object> data = json.DeserializeObject(line) as Dictionary<string, object>;
                if (data == null) continue;
                if (data.ContainsKey("error")) throw new InvalidOperationException(data["error"] as string);
                Dictionary<string, object> message = data.ContainsKey("message") ? data["message"] as Dictionary<string, object> : null;
                string text = message != null && message.ContainsKey("content") ? message["content"] as string : null;
                if (!string.IsNullOrEmpty(text)) chunk(text);
            }
        }

        /// <summary>
        /// Estado de la IA local para el panel: si Ollama responde y qué modelos tiene.
        /// </summary>
        public static OllamaStatus CheckOllama()
        {
            OllamaStatus status = new OllamaStatus();
            status.Installed = File.Exists(OllamaExe);
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(OllamaUrl + "/api/tags");
                request.Timeout = 2000;
                using (WebResponse response = request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    Dictionary<string, object> data = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                    status.Running = true;
                    status.Installed = true;
                    object[] models = data != null && data.ContainsKey("models") ? data["models"] as object[] : null;
                    if (models != null)
                    {
                        foreach (object model in models)
                        {
                            Dictionary<string, object> m = model as Dictionary<string, object>;
                            string name = m != null && m.ContainsKey("name") ? m["name"] as string : null;
                            if (name != null && (name == SuiteSettings.OllamaModel || name == SuiteSettings.OllamaModel + ":latest")) status.HasModel = true;
                        }
                    }
                }
            }
            catch (WebException)
            {
                // No responde: no instalado o apagado
            }
            return status;
        }

        internal class OllamaStatus
        {
            public bool Installed, Running, HasModel;
        }

        public static string OllamaExe
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Ollama\ollama.exe"); }
        }

        /// <summary>
        /// Arranca Ollama si está instalado pero apagado (se queda en la bandeja, como al iniciar Windows).
        /// </summary>
        public static void StartOllama()
        {
            string app = Path.Combine(Path.GetDirectoryName(OllamaExe), "ollama app.exe");
            try
            {
                if (File.Exists(app)) Process.Start(app);
                else if (File.Exists(OllamaExe)) Process.Start(new ProcessStartInfo(OllamaExe, "serve") { CreateNoWindow = true, UseShellExecute = false });
            }
            catch (Exception ex)
            {
                Log.Error("[AssistantService] No se pudo arrancar Ollama: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Descarga el modelo local (unos 5 GB). progress recibe 0..1 y el texto de estado; tarda varios minutos.
        /// </summary>
        public static void PullModel(Action<float, string> progress, Action<string> done)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    JavaScriptSerializer json = new JavaScriptSerializer();
                    string body = json.Serialize(new Dictionary<string, object> { { "model", SuiteSettings.OllamaModel }, { "stream", true } });
                    foreach (string line in PostLines(OllamaUrl + "/api/pull", body, null))
                    {
                        Dictionary<string, object> data = json.DeserializeObject(line) as Dictionary<string, object>;
                        if (data == null) continue;
                        if (data.ContainsKey("error")) throw new InvalidOperationException(data["error"] as string);
                        long total = data.ContainsKey("total") ? Convert.ToInt64(data["total"]) : 0;
                        long completed = data.ContainsKey("completed") ? Convert.ToInt64(data["completed"]) : 0;
                        progress(total > 0 ? (float)completed / total : -1, data.ContainsKey("status") ? data["status"] as string : "");
                    }
                    done(null);
                }
                catch (Exception ex)
                {
                    Log.Error("[AssistantService] No se pudo descargar el modelo: {0}", ex.Message);
                    done(FriendlyError(ex));
                }
            });
        }

        #endregion

        #region Gemini (nube de Google, nivel gratis)

        private static void AskGemini(List<Turn> history, string prompt, Action<string> chunk)
        {
            string key = SuiteSettings.GeminiApiKey;
            if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("Falta la clave de Gemini");
            if (_geminiModel == null) _geminiModel = PickGeminiModel(key);

            List<object> contents = new List<object>();
            foreach (Turn turn in history) contents.Add(GeminiContent(turn.FromUser ? "user" : "model", turn.Text));
            contents.Add(GeminiContent("user", prompt));
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "systemInstruction", new Dictionary<string, object> { { "parts", new object[] { new Dictionary<string, object> { { "text", SystemPrompt() } } } } } },
                { "contents", contents }
            };

            JavaScriptSerializer json = new JavaScriptSerializer();
            foreach (string line in PostLines(GeminiUrl + "models/" + _geminiModel + ":streamGenerateContent?alt=sse", json.Serialize(body), key))
            {
                if (!line.StartsWith("data:")) continue;
                Dictionary<string, object> data = json.DeserializeObject(line.Substring(5).Trim()) as Dictionary<string, object>;
                object[] candidates = data != null && data.ContainsKey("candidates") ? data["candidates"] as object[] : null;
                if (candidates == null || candidates.Length == 0) continue;
                Dictionary<string, object> content = ((Dictionary<string, object>)candidates[0]).ContainsKey("content")
                    ? ((Dictionary<string, object>)candidates[0])["content"] as Dictionary<string, object> : null;
                object[] parts = content != null && content.ContainsKey("parts") ? content["parts"] as object[] : null;
                if (parts == null) continue;
                foreach (object part in parts)
                {
                    Dictionary<string, object> p = part as Dictionary<string, object>;
                    if (p != null && p.ContainsKey("text") && !(p.ContainsKey("thought") && true.Equals(p["thought"]))) chunk(p["text"] as string);
                }
            }
        }

        private static object GeminiContent(string role, string text)
        {
            return new Dictionary<string, object> { { "role", role }, { "parts", new object[] { new Dictionary<string, object> { { "text", text } } } } };
        }

        /// <summary>
        /// El modelo "flash" estable más reciente que ofrece Google con esta clave (los nombres cambian con el tiempo).
        /// También sirve para comprobar que la clave es válida.
        /// </summary>
        public static string PickGeminiModel(string key)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(GeminiUrl + "models?pageSize=200");
            request.Headers["x-goog-api-key"] = key;
            request.Timeout = 10000;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string best = null;
            double bestVersion = -1;
            using (WebResponse response = request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                Dictionary<string, object> data = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                object[] models = data != null && data.ContainsKey("models") ? data["models"] as object[] : new object[0];
                foreach (object model in models)
                {
                    Dictionary<string, object> m = model as Dictionary<string, object>;
                    string name = m != null && m.ContainsKey("name") ? (m["name"] as string ?? "").Replace("models/", "") : "";
                    object[] methods = m != null && m.ContainsKey("supportedGenerationMethods") ? m["supportedGenerationMethods"] as object[] : null;
                    if (methods == null || Array.IndexOf(methods, "generateContent") < 0) continue;
                    // Solo "gemini-X.Y-flash" a secas: ni lite, ni vistas previas, ni variantes de imagen/voz
                    System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(name, @"^gemini-(\d+(\.\d+)?)-flash$");
                    if (!match.Success) continue;
                    double version = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (version > bestVersion) { bestVersion = version; best = name; }
                }
            }
            return best ?? "gemini-2.5-flash";
        }

        #endregion

        #region HTTP

        // POST que devuelve la respuesta línea a línea según va llegando (Ollama: JSON por línea; Gemini: eventos SSE)
        private static IEnumerable<string> PostLines(string url, string body, string geminiKey)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2 para Google
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;
            if (geminiKey != null) request.Headers["x-goog-api-key"] = geminiKey;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);

            using (WebResponse response = request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Trim().Length > 0) yield return line;
                }
            }
        }

        private static string FriendlyError(Exception ex)
        {
            WebException web = ex as WebException;
            if (web != null)
            {
                HttpWebResponse response = web.Response as HttpWebResponse;
                if (response == null)
                {
                    return SuiteSettings.AssistantEngine == SuiteSettings.AssistantEngineKind.Local
                        ? "La IA local no responde. Revisa en Ajustes → Asistente IA que Ollama esté instalado."
                        : "Sin conexión con Google. ¿Hay internet?";
                }
                int code = (int)response.StatusCode;
                if (code == 400 || code == 401 || code == 403) return "La clave de Gemini no es válida. Pégala de nuevo en Ajustes → Asistente IA.";
                if (code == 404) return SuiteSettings.AssistantEngine == SuiteSettings.AssistantEngineKind.Local
                    ? "Falta descargar el modelo local. Ve a Ajustes → Asistente IA." : "Ese modelo de Gemini ya no existe; vuelve a pegar la clave.";
                if (code == 429) return "Llegaste al límite gratis de Gemini por ahora. Prueba en un rato o usa la IA local.";
                return "Error " + code + " del servicio de IA.";
            }
            return ex.Message;
        }

        #endregion

        // Nombre de la app en primer plano (fuera de la suite), para dar contexto a la IA
        private static string ActiveAppName()
        {
            try
            {
                IntPtr window = NativeMethods.GetForegroundWindow();
                uint processId;
                NativeMethods.GetWindowThreadProcessId(window, out processId);
                if (processId == 0 || processId == (uint)Process.GetCurrentProcess().Id) return null;
                using (Process process = Process.GetProcessById((int)processId))
                {
                    try
                    {
                        string description = process.MainModule.FileVersionInfo.FileDescription;
                        if (!string.IsNullOrEmpty(description)) return description;
                    }
                    catch (Exception)
                    {
                        // Proceso con más privilegios
                    }
                    return process.ProcessName;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
