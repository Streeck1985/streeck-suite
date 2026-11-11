using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Avisos de agentes de IA (Claude Code): sus «ganchos» llaman a StreeckSuite.exe --agente &lt;estado&gt;, que deja el
    /// aviso en agente.txt y despierta a la suite. La isla muestra «Claude trabajando…» junto a los ojitos, y avisa
    /// cuando termina o cuando necesita tu permiso (clic: vuelve a VS Code).
    /// </summary>
    internal static class AgentNotifier
    {
        public enum AgentState { Idle, Working, Done, NeedsYou }

        private const string EventName = @"Local\StreeckSuite.Agent";
        private static readonly string StatePath = Path.Combine(Path.GetDirectoryName(Log.FilePath), "agente.txt");
        private static Thread _thread;

        public static AgentState State { get; private set; }
        public static DateTime StateSince { get; private set; }
        public static string Message { get; private set; }
        public static string Folder { get; private set; }

        /// <summary>
        /// En el hilo de la isla: el estado cambió.
        /// </summary>
        public static event Action<AgentState> Changed;

        /// <summary>
        /// La suite escucha los avisos (llamar desde el hilo de la interfaz: los eventos vuelven a él).
        /// </summary>
        public static void Start(Control ui)
        {
            if (_thread != null) return;
            _thread = new Thread(() =>
            {
                using (EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName))
                {
                    while (true)
                    {
                        signal.WaitOne();
                        string[] parts;
                        try { parts = File.ReadAllText(StatePath, Encoding.UTF8).Split(new[] { '\n' }, 3); }
                        catch (IOException) { continue; }
                        if (parts.Length < 1) continue;
                        AgentState state = parts[0].Trim() == "trabajando" ? AgentState.Working
                                         : parts[0].Trim() == "termino" ? AgentState.Done
                                         : parts[0].Trim() == "permiso" ? AgentState.NeedsYou : AgentState.Idle;
                        string folder = parts.Length > 1 ? parts[1].Trim() : "";
                        string message = parts.Length > 2 ? parts[2].Trim() : "";
                        try
                        {
                            ui.BeginInvoke((Action)(() =>
                            {
                                State = state;
                                StateSince = DateTime.Now;
                                Message = message;
                                Folder = folder;
                                Action<AgentState> handler = Changed;
                                if (handler != null) handler(state);
                            }));
                        }
                        catch (InvalidOperationException) { return; }
                    }
                }
            }) { IsBackground = true, Name = "AgentNotifier" };
            _thread.Start();
        }

        /// <summary>
        /// Lado del gancho (StreeckSuite.exe --agente estado): lee lo que manda Claude Code por la entrada estándar,
        /// lo deja en agente.txt y despierta a la suite. Rápido y sin ventanas.
        /// </summary>
        public static void Send(string state)
        {
            string input = "";
            try
            {
                Stream stdin = Console.OpenStandardInput();
                if (stdin != Stream.Null)
                {
                    Thread reader = new Thread(() =>
                    {
                        try { using (StreamReader r = new StreamReader(stdin, Encoding.UTF8)) input = r.ReadToEnd(); } catch (Exception) { }
                    }) { IsBackground = true };
                    reader.Start();
                    reader.Join(1500);
                }
            }
            catch (Exception) { }

            string message = JsonField(input, "message");
            string folder = JsonField(input, "cwd");
            try
            {
                File.WriteAllText(StatePath, state + "\n" + folder + "\n" + message, Encoding.UTF8);
                using (EventWaitHandle signal = EventWaitHandle.OpenExisting(EventName)) signal.Set();
            }
            catch (Exception)
            {
                // La suite no está abierta: no hay a quién avisar
            }
        }

        private static string JsonField(string json, string name)
        {
            Match m = Regex.Match(json ?? "", "\"" + name + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!m.Success) return "";
            try { return Regex.Unescape(m.Groups[1].Value).Replace("\n", " "); } catch (Exception) { return m.Groups[1].Value; }
        }

        /// <summary>
        /// Trae al frente la ventana de VS Code (la del proyecto si se reconoce por el nombre de la carpeta).
        /// </summary>
        public static void FocusEditor()
        {
            string project = string.IsNullOrEmpty(Folder) ? null : Path.GetFileName(Folder.TrimEnd('\\', '/'));
            IntPtr best = IntPtr.Zero;
            foreach (Process p in Process.GetProcessesByName("Code"))
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    if (best == IntPtr.Zero) best = p.MainWindowHandle;
                    if (project != null && p.MainWindowTitle.IndexOf(project, StringComparison.OrdinalIgnoreCase) >= 0) { best = p.MainWindowHandle; break; }
                }
                catch (Exception) { }
            }
            if (best == IntPtr.Zero) return;
            if (IsIconic(best)) ShowWindow(best, 9); // Restaurar si está minimizada
            NativeMethods.SetForegroundWindow(best);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

        #region Conectar con Claude Code (~/.claude/settings.json)

        private static readonly string ClaudeSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
        private static readonly string[][] Hooks =
        {
            new[] { "UserPromptSubmit", "trabajando" },
            new[] { "Stop", "termino" },
            new[] { "Notification", "permiso" }
        };

        public static bool ClaudeConnected
        {
            get
            {
                try { return File.Exists(ClaudeSettings) && File.ReadAllText(ClaudeSettings).Contains("--agente"); }
                catch (IOException) { return false; }
            }
        }

        /// <summary>
        /// Agrega (o quita) los ganchos de la suite en la configuración de Claude Code, sin tocar lo demás.
        /// Guarda una copia de respaldo antes de escribir. Devuelve un mensaje de error o null.
        /// </summary>
        public static string ConnectClaude(bool connect)
        {
            return ConnectClaude(connect, ClaudeSettings);
        }

        internal static string ConnectClaude(bool connect, string ClaudeSettings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ClaudeSettings));
                JavaScriptSerializer json = new JavaScriptSerializer();
                Dictionary<string, object> root = new Dictionary<string, object>();
                if (File.Exists(ClaudeSettings))
                {
                    string text = File.ReadAllText(ClaudeSettings);
                    if (text.Trim().Length > 0) root = json.Deserialize<Dictionary<string, object>>(text);
                    File.Copy(ClaudeSettings, ClaudeSettings + ".streeck-respaldo", true);
                }

                Dictionary<string, object> hooks = root.ContainsKey("hooks") ? root["hooks"] as Dictionary<string, object> : null;
                if (hooks == null) hooks = new Dictionary<string, object>();
                string exe = Application.ExecutablePath.Replace('\\', '/');
                foreach (string[] hook in Hooks)
                {
                    List<object> entries = new List<object>();
                    object existing;
                    if (hooks.TryGetValue(hook[0], out existing) && existing is IEnumerable)
                        foreach (object e in (IEnumerable)existing)
                            if (!json.Serialize(e).Contains("--agente")) entries.Add(e); // Quita los nuestros de antes
                    if (connect)
                    {
                        Dictionary<string, object> command = new Dictionary<string, object>
                        {
                            { "type", "command" },
                            { "command", "\"" + exe + "\" --agente " + hook[1] }
                        };
                        entries.Add(new Dictionary<string, object> { { "hooks", new List<object> { command } } });
                    }
                    if (entries.Count > 0) hooks[hook[0]] = entries; else hooks.Remove(hook[0]);
                }
                if (hooks.Count > 0) root["hooks"] = hooks; else root.Remove("hooks");

                StringBuilder sb = new StringBuilder();
                WriteJson(sb, root, 0, json);
                File.WriteAllText(ClaudeSettings, sb.ToString() + "\n", new UTF8Encoding(false));
                Log.Info("[AgentNotifier] Claude Code {0}", connect ? "conectado" : "desconectado");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("[AgentNotifier] No se pudo cambiar la configuración de Claude Code: {0}", ex.Message);
                return ex.Message;
            }
        }

        // JSON con sangría de 2 espacios, como lo deja Claude Code
        private static void WriteJson(StringBuilder sb, object value, int depth, JavaScriptSerializer json)
        {
            string pad = new string(' ', (depth + 1) * 2), close = new string(' ', depth * 2);
            IDictionary dict = value as IDictionary;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (DictionaryEntry entry in dict)
                {
                    sb.Append(pad).Append(json.Serialize(entry.Key.ToString())).Append(": ");
                    WriteJson(sb, entry.Value, depth + 1, json);
                    sb.Append(++i < dict.Count ? ",\n" : "\n");
                }
                sb.Append(close).Append('}');
                return;
            }
            if (value is IEnumerable && !(value is string))
            {
                List<object> items = new List<object>();
                foreach (object o in (IEnumerable)value) items.Add(o);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(pad);
                    WriteJson(sb, items[i], depth + 1, json);
                    sb.Append(i < items.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(close).Append(']');
                return;
            }
            sb.Append(json.Serialize(value));
        }

        #endregion
    }
}
