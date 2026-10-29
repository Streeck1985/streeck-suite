using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    internal class SearchResult
    {
        public enum Kind { App, File, Calculator, Assistant, Web }

        public Kind Type;
        public string Title;
        public string Subtitle;
        public Bitmap Icon;
        public string Target;   // AUMID, ruta, resultado o URL según el tipo
    }

    /// <summary>
    /// Motor de la búsqueda rápida (mockup E2): apps del menú Inicio, archivos del índice de búsqueda de Windows
    /// (el mismo que usa Inicio, sin indexar nada por nuestra cuenta), calculadora y búsqueda en la web.
    /// </summary>
    internal static class SearchService
    {
        private const int MaxApps = 4;
        private const int MaxFiles = 4;
        private const int AppsRefreshMinutes = 10;
        private const int FileIconPx = 48;

        private static readonly object Gate = new object();
        private static List<ShellImage.App> _apps = new List<ShellImage.App>();
        private static DateTime _appsLoadedAt = DateTime.MinValue;
        private static bool _loadingApps;
        private static readonly CultureInfo Spanish = new CultureInfo("es-ES");

        /// <summary>
        /// La isla abre el asistente con esta pregunta (resultado «Preguntar a la IA»).
        /// </summary>
        public static event Action<string> AssistantRequested;

        /// <summary>
        /// Carga (o refresca) la lista de apps en segundo plano; se llama al abrir la búsqueda.
        /// </summary>
        public static void WarmUp()
        {
            lock (Gate)
            {
                if (_loadingApps || (DateTime.Now - _appsLoadedAt).TotalMinutes < AppsRefreshMinutes) return;
                _loadingApps = true;
            }
            ThreadPool.QueueUserWorkItem(state =>
            {
                List<ShellImage.App> apps = ShellImage.StartMenuApps();
                lock (Gate)
                {
                    if (apps.Count > 0) _apps = apps;
                    _appsLoadedAt = DateTime.Now;
                    _loadingApps = false;
                }
            });
        }

        /// <summary>
        /// Carga síncrona de las apps (solo para la vista previa de desarrollo).
        /// </summary>
        internal static void LoadAppsNow()
        {
            List<ShellImage.App> apps = ShellImage.StartMenuApps();
            lock (Gate)
            {
                _apps = apps;
                _appsLoadedAt = DateTime.Now;
            }
        }

        /// <summary>
        /// Resultados inmediatos (calculadora, apps y web). Los archivos llegan después con FindFiles.
        /// </summary>
        public static List<SearchResult> QuickResults(string query)
        {
            List<SearchResult> results = new List<SearchResult>();
            query = query.Trim();
            if (query.Length == 0) return results;

            double value;
            if (TryCalculate(query, out value))
            {
                // Dos decimales como en el mockup; con números muy pequeños, los que hagan falta para no mostrar 0
                string text = value.ToString(Math.Abs(value) >= 0.01 || value == 0 ? "#,##0.##" : "0.##########", Spanish);
                results.Add(new SearchResult { Type = SearchResult.Kind.Calculator, Title = "= " + text, Subtitle = "Calculadora · Enter copia el resultado", Target = text });
            }

            // Preguntar al asistente de IA (fase G), justo después de la calculadora
            if (query.Length >= 3)
            {
                results.Add(new SearchResult { Type = SearchResult.Kind.Assistant, Title = "Preguntar a la IA: «" + query + "»", Subtitle = AssistantService.EngineLabel, Target = query });
            }

            foreach (ShellImage.App app in MatchApps(query))
            {
                results.Add(new SearchResult { Type = SearchResult.Kind.App, Title = app.Name, Subtitle = "Aplicación", Target = app.Id, Icon = NotificationService.IconFor(app.Id) });
            }

            string engine = SuiteSettings.SearchEngine.ToString();
            if (SuiteSettings.SearchEngine == SuiteSettings.WebSearchEngine.DuckDuckGo) engine = "DuckDuckGo";
            results.Add(new SearchResult { Type = SearchResult.Kind.Web, Title = "Buscar «" + query + "» en la web", Subtitle = engine, Target = WebUrl(query) });
            return results;
        }

        private static List<ShellImage.App> MatchApps(string query)
        {
            string needle = Simplify(query);
            List<KeyValuePair<int, ShellImage.App>> scored = new List<KeyValuePair<int, ShellImage.App>>();
            List<ShellImage.App> apps;
            lock (Gate) apps = _apps;
            foreach (ShellImage.App app in apps)
            {
                string name = Simplify(app.Name);
                int score = name.StartsWith(needle) ? 3 : name.Contains(" " + needle) ? 2 : name.Contains(needle) ? 1 : 0;
                if (score > 0) scored.Add(new KeyValuePair<int, ShellImage.App>(score, app));
            }
            scored.Sort((a, b) => a.Key != b.Key ? b.Key.CompareTo(a.Key) : a.Value.Name.Length.CompareTo(b.Value.Name.Length));
            List<ShellImage.App> best = new List<ShellImage.App>();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<int, ShellImage.App> entry in scored)
            {
                if (best.Count >= MaxApps) break;
                if (names.Add(entry.Value.Name)) best.Add(entry.Value); // El menú Inicio repite algunas apps
            }
            return best;
        }

        /// <summary>
        /// Archivos cuyo nombre contiene palabras que empiezan por el texto (índice de Windows). Tarda unas décimas:
        /// llamar fuera del hilo de la interfaz.
        /// </summary>
        public static List<SearchResult> FindFiles(string query)
        {
            List<SearchResult> results = new List<SearchResult>();
            query = query.Trim().Replace("'", "").Replace("\"", "");
            if (query.Length < 2 || !SuiteSettings.SearchFiles) return results;

            string sql = "SELECT TOP " + MaxFiles + " System.ItemUrl, System.ItemNameDisplay, System.ItemFolderNameDisplay, System.DateModified " +
                         "FROM SystemIndex WHERE SCOPE='file:' AND CONTAINS(System.FileName, '\"" + query + "*\"') " +
                         "AND System.ItemPathDisplay NOT LIKE '%\\AppData\\%' ORDER BY System.DateModified DESC";
            try
            {
                using (OleDbConnection connection = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
                {
                    connection.Open();
                    using (OleDbCommand command = new OleDbCommand(sql, connection))
                    using (OleDbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // ItemUrl ("file:C:/Users/...") es la ruta real; ItemPathDisplay viene traducida ("C:\Usuarios")
                            // y con ella no se puede abrir el archivo
                            string url = reader.GetValue(0) as string;
                            if (string.IsNullOrEmpty(url) || !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) continue;
                            string path = Uri.UnescapeDataString(url.Substring(5)).Replace('/', '\\');
                            string folder = reader.GetValue(2) as string ?? "";
                            object modified = reader.GetValue(3);
                            string when = modified is DateTime ? " · " + RelativeDay((DateTime)modified) : "";
                            results.Add(new SearchResult
                            {
                                Type = SearchResult.Kind.File,
                                Title = reader.GetValue(1) as string ?? System.IO.Path.GetFileName(path),
                                Subtitle = folder + when,
                                Target = path,
                                Icon = ShellImage.Get(path, FileIconPx, ShellImage.ThumbnailOrIcon)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SearchService] No se pudo consultar el índice de Windows: {0}", ex.Message);
            }
            return results;
        }

        public static void Run(SearchResult result)
        {
            try
            {
                switch (result.Type)
                {
                    case SearchResult.Kind.App: System.Diagnostics.Process.Start("explorer.exe", "shell:AppsFolder\\" + result.Target); break;
                    case SearchResult.Kind.File: System.Diagnostics.Process.Start(result.Target); break;
                    case SearchResult.Kind.Web: System.Diagnostics.Process.Start(result.Target); break;
                    case SearchResult.Kind.Calculator: System.Windows.Forms.Clipboard.SetText(result.Target); break;
                    case SearchResult.Kind.Assistant: if (AssistantRequested != null) AssistantRequested(result.Target); break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SearchService] No se pudo abrir {0}: {1}", result.Target, ex.Message);
            }
        }

        private static string WebUrl(string query)
        {
            string q = Uri.EscapeDataString(query);
            switch (SuiteSettings.SearchEngine)
            {
                case SuiteSettings.WebSearchEngine.Bing: return "https://www.bing.com/search?q=" + q;
                case SuiteSettings.WebSearchEngine.DuckDuckGo: return "https://duckduckgo.com/?q=" + q;
                default: return "https://www.google.com/search?q=" + q;
            }
        }

        private static string RelativeDay(DateTime date)
        {
            DateTime local = date.Kind == DateTimeKind.Utc ? date.ToLocalTime() : date;
            int days = (DateTime.Today - local.Date).Days;
            if (days <= 0) return "modificado hoy";
            if (days == 1) return "modificado ayer";
            if (days < 7) return "hace " + days + " días";
            return local.ToString("d 'de' MMM", Spanish);
        }

        // Sin mayúsculas ni tildes: "música" encuentra "Musica" y al revés
        private static string Simplify(string text)
        {
            string decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder plain = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(c);
            }
            return plain.ToString();
        }

        #region Calculadora

        /// <summary>
        /// Operaciones con + - * / ^ %, paréntesis y decimales con punto o coma. Solo cuenta como cálculo si hay
        /// al menos un operador (un número suelto se busca como texto).
        /// </summary>
        internal static bool TryCalculate(string text, out double value)
        {
            value = 0;
            string expression = text.Replace(" ", "").Replace(',', '.').Replace('x', '*').Replace('×', '*').Replace('÷', '/');
            if (expression.Length < 3 || expression.IndexOfAny(new[] { '+', '-', '*', '/', '^', '%' }) <= 0) return false;
            foreach (char c in expression)
            {
                if (!char.IsDigit(c) && "+-*/^%().".IndexOf(c) < 0) return false;
            }
            try
            {
                int position = 0;
                value = ParseSum(expression, ref position);
                return position == expression.Length && !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static double ParseSum(string s, ref int i)
        {
            double value = ParseProduct(s, ref i);
            while (i < s.Length && (s[i] == '+' || s[i] == '-'))
            {
                char op = s[i++];
                double right = ParseProduct(s, ref i);
                value = op == '+' ? value + right : value - right;
            }
            return value;
        }

        private static double ParseProduct(string s, ref int i)
        {
            double value = ParsePower(s, ref i);
            while (i < s.Length && (s[i] == '*' || s[i] == '/' || s[i] == '%'))
            {
                char op = s[i++];
                double right = ParsePower(s, ref i);
                value = op == '*' ? value * right : op == '/' ? value / right : value % right;
            }
            return value;
        }

        private static double ParsePower(string s, ref int i)
        {
            double value = ParseUnary(s, ref i);
            if (i < s.Length && s[i] == '^')
            {
                i++;
                value = Math.Pow(value, ParsePower(s, ref i)); // ^ asocia por la derecha
            }
            return value;
        }

        private static double ParseUnary(string s, ref int i)
        {
            if (i < s.Length && s[i] == '-') { i++; return -ParseUnary(s, ref i); }
            if (i < s.Length && s[i] == '+') { i++; return ParseUnary(s, ref i); }
            if (i < s.Length && s[i] == '(')
            {
                i++;
                double inner = ParseSum(s, ref i);
                if (i >= s.Length || s[i] != ')') throw new FormatException();
                i++;
                return inner;
            }
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (start == i) throw new FormatException();
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
