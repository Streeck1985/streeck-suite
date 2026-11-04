using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Letras sincronizadas (fase J2, idea de Nusa): las pide a LRCLIB (gratis, sin clave) para la canción que suena y
    /// las guarda en %LOCALAPPDATA%\StreeckSuite\letras para no volver a pedirlas. La línea actual sale de la posición
    /// de la canción que ya da Windows (MediaInfo.Position).
    /// </summary>
    internal static class LyricsService
    {
        internal class Line
        {
            public TimeSpan Time;
            public string Text;
        }

        internal enum State { None, Loading, Ready, NotFound }

        private static readonly object Sync = new object();
        private static string _trackKey;
        private static List<Line> _lines = new List<Line>();
        private static State _state = State.None;

        private static readonly string Folder = Path.Combine(Path.GetDirectoryName(Log.FilePath), "letras");
        private static readonly Regex Stamp = new Regex(@"^\[(\d+):(\d+(?:\.\d+)?)\]\s*(.*)$");

        public static event Action Updated;

        public static State CurrentState
        {
            get { lock (Sync) return _state; }
        }

        public static List<Line> Lines
        {
            get { lock (Sync) return _lines; }
        }

        /// <summary>
        /// Pide la letra de la canción si cambió. Barato de llamar en cada dibujo.
        /// </summary>
        public static void Request(MediaInfo media)
        {
            if (media == null || !media.IsAvailable || string.IsNullOrEmpty(media.Title)) return;
            string key = media.Artist + "\n" + media.Title;
            lock (Sync)
            {
                if (key == _trackKey) return;
                _trackKey = key;
                _lines = new List<Line>();
                _state = State.Loading;
            }
            string artist = media.Artist, title = media.Title;
            int seconds = (int)Math.Round(media.Duration.TotalSeconds);
            ThreadPool.QueueUserWorkItem(_ => Load(key, artist, title, seconds));
        }

        /// <summary>
        /// Herramienta de desarrollo (--preview-island): letra de ejemplo ya cargada para esa canción.
        /// </summary>
        internal static void SetPreview(MediaInfo media, string lrc)
        {
            lock (Sync)
            {
                _trackKey = media.Artist + "\n" + media.Title;
                _lines = Parse(lrc);
                _state = State.Ready;
            }
        }

        /// <summary>
        /// Índice de la línea que se canta en <paramref name="position"/> (-1 antes de la primera).
        /// </summary>
        public static int IndexAt(TimeSpan position)
        {
            List<Line> lines = Lines;
            int index = -1;
            for (int i = 0; i < lines.Count && lines[i].Time <= position; i++) index = i;
            return index;
        }

        private static void Load(string key, string artist, string title, int seconds)
        {
            List<Line> lines = null;
            try
            {
                string cache = Path.Combine(Folder, Hash(key) + ".lrc");
                string lrc = File.Exists(cache) ? File.ReadAllText(cache, Encoding.UTF8) : Download(artist, title, seconds);
                if (lrc != null)
                {
                    lines = Parse(lrc);
                    if (!File.Exists(cache))
                    {
                        Directory.CreateDirectory(Folder);
                        File.WriteAllText(cache, lrc, Encoding.UTF8);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Lyrics] No se pudo obtener la letra de {0} - {1}: {2}", artist, title, ex.Message);
            }

            lock (Sync)
            {
                if (key != _trackKey) return; // Ya cambió de canción
                _lines = lines ?? new List<Line>();
                _state = _lines.Count > 0 ? State.Ready : State.NotFound;
            }
            Action updated = Updated;
            if (updated != null) updated();
        }

        // Primero la búsqueda exacta (artista, título y duración); si no, la búsqueda libre
        private static string Download(string artist, string title, int seconds)
        {
            string exact = "https://lrclib.net/api/get?artist_name=" + Uri.EscapeDataString(artist) + "&track_name=" + Uri.EscapeDataString(CleanTitle(title)) +
                           (seconds > 0 ? "&duration=" + seconds : "");
            string synced = SyncedFrom(Get(exact));
            if (synced != null) return synced;

            string search = Get("https://lrclib.net/api/search?q=" + Uri.EscapeDataString(artist + " " + CleanTitle(title)));
            if (search == null) return null;
            object[] results = new JavaScriptSerializer().DeserializeObject(search) as object[];
            if (results == null) return null;
            foreach (object result in results)
            {
                Dictionary<string, object> item = result as Dictionary<string, object>;
                object lyrics;
                if (item != null && item.TryGetValue("syncedLyrics", out lyrics) && lyrics is string && ((string)lyrics).Length > 0) return (string)lyrics;
            }
            return null;
        }

        private static string SyncedFrom(string json)
        {
            if (json == null) return null;
            Dictionary<string, object> item = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            object lyrics;
            return item != null && item.TryGetValue("syncedLyrics", out lyrics) && lyrics is string && ((string)lyrics).Length > 0 ? (string)lyrics : null;
        }

        private static string Get(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "StreeckSuite/1.0 (suite de escritorio para Windows)"; // LRCLIB pide identificarse
            request.Timeout = 8000;
            try
            {
                using (WebResponse response = request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse http = ex.Response as HttpWebResponse;
                if (http != null && http.StatusCode == HttpStatusCode.NotFound) return null; // No la tienen
                throw;
            }
        }

        // "Canción (Remastered 2019) - Live" → "Canción": los extras confunden la búsqueda
        private static string CleanTitle(string title)
        {
            string clean = Regex.Replace(title, @"\s*[\(\[][^\)\]]*[\)\]]", "");
            int dash = clean.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) clean = clean.Substring(0, dash);
            return clean.Trim().Length > 0 ? clean.Trim() : title;
        }

        internal static List<Line> Parse(string lrc)
        {
            List<Line> lines = new List<Line>();
            foreach (string raw in lrc.Split('\n'))
            {
                Match match = Stamp.Match(raw.Trim());
                if (!match.Success) continue;
                double secondsPart = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                TimeSpan time = TimeSpan.FromMinutes(int.Parse(match.Groups[1].Value)) + TimeSpan.FromSeconds(secondsPart);
                string text = match.Groups[3].Value.Trim();
                lines.Add(new Line { Time = time, Text = text.Length > 0 ? text : "♪" });
            }
            lines.Sort((a, b) => a.Time.CompareTo(b.Time));
            return lines;
        }

        private static string Hash(string text)
        {
            using (SHA1 sha = SHA1.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }
    }
}
