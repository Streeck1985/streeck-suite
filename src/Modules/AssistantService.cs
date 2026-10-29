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
        private static List<string> _geminiModels;  // Flash estables, del más nuevo al más antiguo (cada uno con su cupo gratis)
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
                    if (SuiteSettings.AssistantEngine == SuiteSettings.AssistantEngineKind.Gemini)
                    {
                        try
                        {
                            AskGemini(history, prompt, chunk);
                        }
                        catch (Exception ex)
                        {
                            // Gemini saturado (503), sin cupo (429), sin internet o muy lento: contesta la IA local
                            // si está lista, en vez de dejar al usuario esperando
                            if (answer.Length > 0 || ex is OperationCanceledException || !CheckOllama().HasModel) throw;
                            Log.Error("[AssistantService] Gemini falló ({0}); responde la IA local", ex.Message);
                            chunk("(Gemini no respondió; contesta la IA local) ");
                            AskOllama(history, prompt, chunk);
                        }
                    }
                    else
                    {
                        AskOllama(history, prompt, chunk);
                    }

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

        private static string SystemPrompt(bool local)
        {
            string app = _activeApp;
            return "Eres el asistente de Streeck Suite, integrado en la barra superior de Windows del usuario. " +
                   "Responde siempre en español, de forma breve y clara (normalmente 1 a 4 frases), sin rodeos. " +
                   "Usa texto plano: sin tablas ni encabezados. " +
                   "Hoy es " + DateTime.Now.ToString("dddd d 'de' MMMM 'de' yyyy, HH:mm", new System.Globalization.CultureInfo("es-MX")) + ". " +
                   "El usuario vive en " + System.Globalization.RegionInfo.CurrentRegion.DisplayName + ": precios, monedas y unidades de ahí salvo que diga otra cosa. " +
                   (app != null ? "El usuario está usando la app «" + app + "». " : "") +
                   "Si el usuario incluye un texto después de una línea ---, es lo que tiene copiado: úsalo para responder." +
                   (local ? " No tienes internet: si te piden datos actuales (precios, tipo de cambio, noticias, resultados, clima), " +
                            "di claramente que no puedes saberlo sin conexión y sugiere usar Gemini; nunca inventes cifras." : "");
        }

        #region Ollama (local)

        private static void AskOllama(List<Turn> history, string prompt, Action<string> chunk)
        {
            List<object> messages = new List<object>();
            messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", SystemPrompt(true) } });
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
            if (_geminiModels == null)
            {
                _geminiModels = GeminiModels(key);
                string preferred = File.Exists(PreferredModelFile) ? File.ReadAllText(PreferredModelFile).Trim() : null;
                _geminiModel = preferred != null && _geminiModels.Contains(preferred) ? preferred : _geminiModels[0];
            }
            else if (_geminiModels.IndexOf(_geminiModel) > 0 && (DateTime.Now - _bestCheckedAt).TotalMinutes > 30)
            {
                CheckBestModelInBackground(key);
            }

            // Si un modelo se quedó sin cupo gratis (429) o está saturado (503), se prueba el anterior
            for (int start = _geminiModels.IndexOf(_geminiModel), i = Math.Max(0, start); ; i++)
            {
                try
                {
                    AskGeminiModel(history, prompt, chunk, key);
                    if (i != start) RememberModel(_geminiModel);
                    return;
                }
                catch (WebException ex)
                {
                    HttpWebResponse response = ex.Response as HttpWebResponse;
                    int code = response != null ? (int)response.StatusCode : ex.Status == WebExceptionStatus.Timeout ? 503 : 0; // Muy lento = saturado
                    // 429 con búsqueda de Google: el plan gratis puede no incluir búsquedas; se repite sin ella
                    if (code == 429 && _geminiSearch)
                    {
                        Log.Error("[AssistantService] {0} rechazó la petición con búsqueda; se repite sin búsqueda", _geminiModel);
                        _geminiSearch = false;
                        i--;
                        continue;
                    }
                    if ((code != 429 && code != 503) || i + 1 >= _geminiModels.Count) throw;
                    Log.Error("[AssistantService] {0} respondió {1}; se prueba {2}", _geminiModel, code, _geminiModels[i + 1]);
                    _geminiModel = _geminiModels[i + 1];
                }
            }
        }

        private static void AskGeminiModel(List<Turn> history, string prompt, Action<string> chunk, string key)
        {

            List<object> contents = new List<object>();
            foreach (Turn turn in history) contents.Add(GeminiContent(turn.FromUser ? "user" : "model", turn.Text));
            contents.Add(GeminiContent("user", prompt));
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "systemInstruction", new Dictionary<string, object> { { "parts", new object[] { new Dictionary<string, object> { { "text", SystemPrompt(false) } } } } } },
                { "contents", contents }
            };
            // Búsqueda de Google (datos actuales: precios, noticias, clima...) y poco "pensamiento" para que conteste rápido.
            // Si este modelo no acepta alguno de los dos, se repite la pregunta sin ellos.
            if (_geminiSearch)
            {
                body["tools"] = new object[] { new Dictionary<string, object> { { "google_search", new Dictionary<string, object>() } } };
            }
            if (_geminiExtras)
            {
                body["generationConfig"] = new Dictionary<string, object> { { "thinkingConfig", new Dictionary<string, object> { { "thinkingLevel", "low" } } } };
            }

            JavaScriptSerializer json = new JavaScriptSerializer();
            IEnumerable<string> lines;
            try
            {
                lines = PostLines(GeminiUrl + "models/" + _geminiModel + ":streamGenerateContent?alt=sse", json.Serialize(body), key);
                lines = BufferFirst(lines);
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if ((!_geminiExtras && !_geminiSearch) || response == null || (int)response.StatusCode != 400) throw;
                Log.Error("[AssistantService] {0} no acepta búsqueda o nivel de pensamiento; se pregunta sin ellos", _geminiModel);
                _geminiExtras = false;
                _geminiSearch = false;
                body.Remove("tools");
                body.Remove("generationConfig");
                lines = PostLines(GeminiUrl + "models/" + _geminiModel + ":streamGenerateContent?alt=sse", json.Serialize(body), key);
            }
            foreach (string line in lines)
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

        // Medido con el plan gratis: la búsqueda de Google da 429 y "pensar poco" dejaba la respuesta colgada,
        // así que se dejan apagados (el código se conserva por si cambia el plan)
        private static bool _geminiExtras = false;
        private static bool _geminiSearch = false;
        private const int GeminiFirstByteMs = 8000;
        private static DateTime _bestCheckedAt = DateTime.Now;

        // Último modelo que respondió bien: se recuerda entre sesiones para no esperar a los saturados
        private static string PreferredModelFile
        {
            get { return Path.Combine(Path.GetDirectoryName(Log.FilePath), "gemini_modelo.txt"); }
        }  // Si un modelo no empieza a responder en este tiempo, se prueba otro


        // Lee la primera línea ya (para que un error 400 salte aquí y no a mitad de la respuesta) y deja el resto en vivo
        private static IEnumerable<string> BufferFirst(IEnumerable<string> lines)
        {
            IEnumerator<string> enumerator = lines.GetEnumerator();
            bool hasFirst = enumerator.MoveNext();
            return Continue(enumerator, hasFirst);
        }

        private static IEnumerable<string> Continue(IEnumerator<string> enumerator, bool hasFirst)
        {
            using (enumerator)
            {
                if (!hasFirst) yield break;
                yield return enumerator.Current;
                while (enumerator.MoveNext()) yield return enumerator.Current;
            }
        }

        private static void RememberModel(string model)
        {
            try { File.WriteAllText(PreferredModelFile, model); }
            catch (IOException ex) { Log.Error("[AssistantService] No se pudo guardar el modelo: {0}", ex.Message); }
        }

        // Pregunta mínima al modelo más nuevo sin que el usuario espere: si ya responde rápido, se vuelve a él
        private static void CheckBestModelInBackground(string key)
        {
            _bestCheckedAt = DateTime.Now;
            string best = _geminiModels[0];
            ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    string body = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "contents", new object[] { GeminiContent("user", "Responde solo: ok") } } });
                    foreach (string line in PostLines(GeminiUrl + "models/" + best + ":streamGenerateContent?alt=sse", body, key)) { }
                    _geminiModel = best;
                    RememberModel(best);
                    Log.Info("[AssistantService] {0} vuelve a responder; se usa de nuevo", best);
                }
                catch (Exception)
                {
                    // Sigue saturado: se queda el modelo actual
                }
            });
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
            return GeminiModels(key)[0];
        }

        private static List<string> GeminiModels(string key)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(GeminiUrl + "models?pageSize=200");
            request.Headers["x-goog-api-key"] = key;
            request.Timeout = 10000;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            List<KeyValuePair<double, string>> found = new List<KeyValuePair<double, string>>();
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
                    found.Add(new KeyValuePair<double, string>(version, name));
                }
            }
            found.Sort((a, b) => b.Key.CompareTo(a.Key));
            List<string> names = found.ConvertAll(f => f.Value);
            if (names.Count == 0) names.Add("gemini-2.5-flash");
            return names;
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
            // Gemini: si no empieza a responder pronto, otro modelo; Ollama: la primera vez carga el modelo y tarda
            request.Timeout = geminiKey != null ? GeminiFirstByteMs : 120000;
            request.ReadWriteTimeout = geminiKey != null ? GeminiFirstByteMs : 120000;
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
