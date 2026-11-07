using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Web.Script.Serialization;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Buscar actualizaciones (mockup «Actualizaciones Streeck» aprobado): revisa la última versión publicada en GitHub,
    /// avisa en la isla y, cuando tú lo decides, descarga el instalador y lo corre con --update (se instala sin
    /// ventanas, cierra la suite y la vuelve a abrir).
    /// </summary>
    internal static class UpdateService
    {
        public enum State { Idle, Checking, UpToDate, Available, Downloading, Error }

        public static State Current { get; private set; }
        public static string LatestVersion { get; private set; }
        public static string Notes { get; private set; }
        public static long SizeBytes { get; private set; }
        public static float Progress { get; private set; }
        public static string ErrorText { get; private set; }
        public static DateTime LastChecked { get; private set; }

        /// <summary>
        /// Cambió el estado (el panel se redibuja).
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// Se encontró una versión nueva (la isla lo avisa).
        /// </summary>
        public static event Action<string> Found;

        private static string _downloadUrl;
        private static System.Threading.Timer _timer;
        private static int _busy;

        /// <summary>
        /// Revisión automática: al minuto de arrancar y luego cada día (si está activada en Acerca de).
        /// </summary>
        public static void StartAutomatic()
        {
            if (_timer != null) return;
            _timer = new System.Threading.Timer(_ =>
            {
                if (SuiteSettings.UpdateAuto && (DateTime.Now - LastChecked).TotalHours >= 24) Check();
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
        }

        /// <summary>
        /// Revisa GitHub en segundo plano.
        /// </summary>
        public static void Check()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            Set(State.Checking);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Dictionary<string, object> release = FetchLatest();
                    LastChecked = DateTime.Now;
                    string tag = Convert.ToString(release["tag_name"]).TrimStart('v', 'V');
                    _downloadUrl = null;
                    // JavaScriptSerializer entrega los arreglos JSON como object[] (no ArrayList): se acepta cualquier lista
                    IEnumerable assets = release.ContainsKey("assets") ? release["assets"] as IEnumerable : null;
                    if (assets != null)
                    {
                        foreach (object item in assets)
                        {
                            Dictionary<string, object> asset = item as Dictionary<string, object>;
                            if (asset == null || !Convert.ToString(asset["name"]).EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                            _downloadUrl = Convert.ToString(asset["browser_download_url"]);
                            SizeBytes = Convert.ToInt64(asset["size"]);
                            break;
                        }
                    }
                    LatestVersion = tag;
                    Notes = release.ContainsKey("body") ? Convert.ToString(release["body"]) : "";
                    bool newer = _downloadUrl != null && IsNewer(tag, AppInfo.Version);
                    Log.Info("[UpdateService] En GitHub: {0} (instalador: {1}); esta: {2}", tag, _downloadUrl != null ? "sí" : "no", AppInfo.Version);
                    _busy = 0;
                    Set(newer ? State.Available : State.UpToDate);
                    if (newer)
                    {
                        Action<string> found = Found;
                        if (found != null) found(tag);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[UpdateService] No se pudo revisar GitHub: {0}", ex.Message);
                    ErrorText = ex is WebException && ((WebException)ex).Response == null ? "sin internet" : ex.Message;
                    LastChecked = DateTime.Now;
                    _busy = 0;
                    Set(State.Error);
                }
            });
        }

        /// <summary>
        /// Descarga el instalador nuevo y lo corre en modo actualización (cierra y reabre la suite).
        /// </summary>
        public static void Install()
        {
            if (Current != State.Available || _downloadUrl == null) return;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            Progress = 0;
            Set(State.Downloading);
            string url = _downloadUrl;
            string target = Path.Combine(Path.GetTempPath(), "StreeckSuite-Setup-" + LatestVersion + ".exe");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.UserAgent = "StreeckSuite/" + AppInfo.Version;
                    using (WebResponse response = request.GetResponse())
                    using (Stream source = response.GetResponseStream())
                    using (FileStream file = File.Create(target))
                    {
                        long total = response.ContentLength > 0 ? response.ContentLength : SizeBytes;
                        byte[] buffer = new byte[64 * 1024];
                        long done = 0;
                        int read;
                        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            file.Write(buffer, 0, read);
                            done += read;
                            if (total > 0)
                            {
                                Progress = Math.Min(1f, done / (float)total);
                                Raise();
                            }
                        }
                    }
                    Progress = 1;
                    Raise();
                    Log.Info("[UpdateService] Instalando la versión {0}", LatestVersion);
                    Process.Start(new ProcessStartInfo(target, "--update") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Error("[UpdateService] No se pudo descargar la versión nueva: {0}", ex.Message);
                    ErrorText = "no se pudo descargar";
                    _busy = 0;
                    Set(State.Error);
                }
            });
        }

        private static Dictionary<string, object> FetchLatest()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2 para GitHub
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + AppInfo.GitHubRepo + "/releases/latest");
            request.UserAgent = "StreeckSuite/" + AppInfo.Version; // GitHub exige identificarse
            request.Accept = "application/vnd.github+json";
            request.Timeout = 15000;
            using (WebResponse response = request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                Dictionary<string, object> data = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                if (data == null || !data.ContainsKey("tag_name")) throw new InvalidDataException("respuesta inesperada de GitHub");
                return data;
            }
        }

        /// <summary>
        /// "1.2.0" es más nueva que "1.1.0" (compara número por número).
        /// </summary>
        public static bool IsNewer(string candidate, string current)
        {
            Version a, b;
            if (System.Version.TryParse(Normalize(candidate), out a) && System.Version.TryParse(Normalize(current), out b)) return a > b;
            return false;
        }

        private static string Normalize(string version)
        {
            string clean = (version ?? "").Trim();
            int dash = clean.IndexOfAny(new[] { '-', ' ', '+' });
            if (dash > 0) clean = clean.Substring(0, dash);
            return clean.Contains(".") ? clean : clean + ".0";
        }

        private static void Set(State state)
        {
            Current = state;
            Raise();
        }

        private static void Raise()
        {
            Action changed = Changed;
            if (changed != null) changed();
        }
    }
}
