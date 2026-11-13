using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Acción hecha por el asistente (mockup G1): lo que se muestra en la tarjeta verde.
    /// </summary>
    internal class AssistantAction
    {
        public string Glyph;     // Segoe Fluent Icons
        public string Title;
        public string Detail;
    }

    /// <summary>
    /// Órdenes que el asistente cumple directamente, sin preguntar a la IA: temporizadores, música, volumen, abrir apps,
    /// buscar en la web y modo privacidad. Solo acciones de la suite y seguras (nada que borre o envíe cosas).
    /// Se reconocen por frases en español; si no encaja ninguna, la pregunta va a la IA.
    /// </summary>
    internal static class AssistantActions
    {
        private const byte VkVolumeMute = 0xAD, VkVolumeDown = 0xAE, VkVolumeUp = 0xAF;
        private const uint KeyUp = 0x0002;

        private static readonly Dictionary<string, int> Numbers = new Dictionary<string, int>
        {
            { "un", 1 }, { "uno", 1 }, { "una", 1 }, { "dos", 2 }, { "tres", 3 }, { "cuatro", 4 }, { "cinco", 5 }, { "seis", 6 },
            { "siete", 7 }, { "ocho", 8 }, { "nueve", 9 }, { "diez", 10 }, { "once", 11 }, { "doce", 12 }, { "quince", 15 },
            { "veinte", 20 }, { "veinticinco", 25 }, { "treinta", 30 }, { "cuarenta", 40 }, { "cuarenta y cinco", 45 },
            { "cincuenta", 50 }, { "sesenta", 60 }, { "noventa", 90 }
        };

        /// <summary>
        /// Si la frase es una orden, la cumple y devuelve qué hizo; si no, null (va a la IA).
        /// Llamar desde el hilo de la interfaz.
        /// </summary>
        public static AssistantAction TryRun(string phrase)
        {
            string text = Simplify(phrase);
            Match m;

            // Tareas con hora (fase J4): "recuérdame enviar la factura a las 6", "anota llamar al proveedor mañana a las 9:30"
            m = Regex.Match(text, @"^(?:recuerdame|avisame|anota|agrega(?:r)?(?: una)? tarea|nueva tarea)\s+(?:que\s+)?(?<what>.+?)\s+(?<day>manana\s+|hoy\s+)?a las\s+(?<h>\d{1,2})(?::(?<min>\d{2}))?\s*(?<ap>de la manana|de la tarde|de la noche|am|pm)?(?:\s+(?<day2>manana|hoy))?$");
            if (m.Success)
            {
                int hour = int.Parse(m.Groups["h"].Value), minute = m.Groups["min"].Success ? int.Parse(m.Groups["min"].Value) : 0;
                string ap = m.Groups["ap"].Value;
                if ((ap == "de la tarde" || ap == "de la noche" || ap == "pm") && hour < 12) hour += 12;
                if (ap == "de la manana" && hour == 12) hour = 0;
                if (hour <= 23 && minute <= 59)
                {
                    bool tomorrow = m.Groups["day"].Value.Trim() == "manana" || m.Groups["day2"].Value == "manana";
                    DateTime due = DateTime.Today.AddHours(hour).AddMinutes(minute);
                    if (tomorrow) due = due.AddDays(1);
                    else if (due < DateTime.Now && ap.Length == 0 && hour < 12 && due.AddHours(12) > DateTime.Now) due = due.AddHours(12); // "a las 6" ya pasó: las 18:00
                    else if (due < DateTime.Now) due = due.AddDays(1);
                    string what = OriginalWords(phrase, text, m.Groups["what"].Value);
                    TasksService.Add(what, due);
                    return new AssistantAction { Glyph = "", Title = "Tarea: «" + what + "»", Detail = "Te aviso en la isla " + (due.Date == DateTime.Today ? "hoy" : "mañana") + " a las " + TimeText.Short(due) };
                }
            }
            m = Regex.Match(text, @"^(?:anota(?: una tarea)?|agrega(?:r)?(?: una)? tarea|nueva tarea)\s+(?:que\s+)?(?<what>.+)$");
            if (m.Success)
            {
                string what = OriginalWords(phrase, text, m.Groups["what"].Value);
                TasksService.Add(what, null);
                return new AssistantAction { Glyph = "", Title = "Tarea: «" + what + "»", Detail = "La verás en el calendario de la isla" };
            }

            // Temporizadores: "recuérdame en 20 minutos sacar la ropa", "pon un temporizador de media hora"
            m = Regex.Match(text, @"^(?:recuerdame|avisame|pon(?:me)?(?: un)? (?:temporizador|alarma|timer)|temporizador|timer)\s+(?:de\s+|en\s+|dentro de\s+)?(?<n>\d+|[a-z ]+?|media)\s*(?<u>hora|horas|minuto|minutos|min|segundos)?(?:\s+(?:para|que|a)\s+(?<what>.+)|\s+(?<what2>.+))?$");
            if (m.Success)
            {
                int minutes = ToMinutes(m.Groups["n"].Value.Trim(), m.Groups["u"].Value);
                if (minutes > 0 && minutes <= 180)
                {
                    string what = m.Groups["what"].Success ? m.Groups["what"].Value : m.Groups["what2"].Success ? m.Groups["what2"].Value : "";
                    TimerService.SetMinutes(minutes);
                    TimerService.Toggle();
                    string label = what.Length > 0 ? ": «" + OriginalTail(phrase, what) + "»" : "";
                    return new AssistantAction { Glyph = "", Title = "Temporizador de " + DurationText(minutes) + label, Detail = "Te aviso en la isla cuando termine" };
                }
            }

            // Música
            if (Regex.IsMatch(text, @"^(pausa|pausar|para|deten|deten la musica|pausa la musica|para la musica|quita la musica)( la musica| la cancion)?$"))
            {
                if (MediaService.Current.IsPlaying) MediaService.TogglePlayPause();
                return new AssistantAction { Glyph = "", Title = "Música en pausa", Detail = MediaService.Current.Title ?? "" };
            }
            if (Regex.IsMatch(text, @"^(reanuda|continua|pon la musica|play|reproduce|dale play)( la musica| la cancion)?$"))
            {
                if (!MediaService.Current.IsPlaying) MediaService.TogglePlayPause();
                return new AssistantAction { Glyph = "", Title = "Reproduciendo", Detail = MediaService.Current.Title ?? "" };
            }
            if (Regex.IsMatch(text, @"^(siguiente|la siguiente|pasa(la)?|salta(la)?)( cancion| tema)?$|^siguiente cancion$"))
            {
                MediaService.Next();
                return new AssistantAction { Glyph = "", Title = "Siguiente canción", Detail = "" };
            }
            if (Regex.IsMatch(text, @"^(anterior|la anterior|cancion anterior|regresa la cancion)$"))
            {
                MediaService.Previous();
                return new AssistantAction { Glyph = "", Title = "Canción anterior", Detail = "" };
            }

            // Volumen (las mismas teclas multimedia del teclado: cada pulsación son 2 puntos)
            if (Regex.IsMatch(text, @"^(sube|subele|subir)( el| al)? volumen( un poco)?$|^mas volumen$"))
            {
                PressKey(VkVolumeUp, 5);
                return new AssistantAction { Glyph = "", Title = "Volumen +10", Detail = "" };
            }
            if (Regex.IsMatch(text, @"^(baja|bajale|bajar)( el| al)? volumen( un poco)?$|^menos volumen$"))
            {
                PressKey(VkVolumeDown, 5);
                return new AssistantAction { Glyph = "", Title = "Volumen −10", Detail = "" };
            }
            if (Regex.IsMatch(text, @"^(silencia|silencio|mutea|quita el sonido|activa el sonido)( el sonido| todo)?$"))
            {
                PressKey(VkVolumeMute, 1);
                return new AssistantAction { Glyph = "", Title = "Silencio activado o quitado", Detail = "" };
            }

            // No molestar / Pausar notificaciones
            if (Regex.IsMatch(text, @"^(pausar|pausa|silenciar|silencia|no molestar|desactivar)( las| los)? (notificaciones|avisos)$|^no molestar$"))
            {
                if (!SuiteSettings.DoNotDisturb) NotificationService.ToggleDoNotDisturb();
                return new AssistantAction { Glyph = "", Title = "Notificaciones en pausa", Detail = "Atajo: Ctrl + Alt + N" };
            }
            if (Regex.IsMatch(text, @"^(activar|activa|reanudar|reanuda|quitar silencio)( las| los)? (notificaciones|avisos)$"))
            {
                if (SuiteSettings.DoNotDisturb) NotificationService.ToggleDoNotDisturb();
                return new AssistantAction { Glyph = "", Title = "Notificaciones activadas", Detail = "Atajo: Ctrl + Alt + N" };
            }

            // Modo privacidad
            m = Regex.Match(text, @"^(activa|desactiva|enciende|apaga|quita|pon)( el)? modo (de )?privacidad$");
            if (m.Success)
            {
                bool wantOn = m.Groups[1].Value == "activa" || m.Groups[1].Value == "enciende" || m.Groups[1].Value == "pon";
                AeroDwmModule privacy = AeroDwmModule.Current;
                if (privacy != null && privacy.IsPrivacyActive != wantOn) EventBus.Instance.Publish(Core.Events.PrivacyToggle);
                return new AssistantAction { Glyph = "", Title = wantOn ? "Modo privacidad activado" : "Modo privacidad desactivado", Detail = "Atajo: Ctrl + Alt + P" };
            }

            // Buscar en la web: "busca recetas de pozole en internet / en google"
            m = Regex.Match(text, @"^(?:busca|buscame|googlea)\s+(?<q>.+?)\s+(?:en internet|en la web|en google|en linea)$");
            if (m.Success)
            {
                string query = OriginalTail(phrase, m.Groups["q"].Value);
                List<SearchResult> results = SearchService.QuickResults(query);
                SearchResult web = results.Find(r => r.Type == SearchResult.Kind.Web);
                if (web != null) SearchService.Run(web);
                return new AssistantAction { Glyph = "", Title = "Buscando «" + query + "»", Detail = "En tu navegador" };
            }

            // Abrir apps: "abre spotify"
            m = Regex.Match(text, @"^(?:abre|abrir|abreme|inicia|lanza)\s+(?:la app |el programa )?(?<app>.+)$");
            if (m.Success)
            {
                SearchService.WarmUp();
                SearchResult app = SearchService.QuickResults(m.Groups["app"].Value).Find(r => r.Type == SearchResult.Kind.App);
                if (app != null)
                {
                    SearchService.Run(app);
                    return new AssistantAction { Glyph = "", Title = "Abriendo " + app.Title, Detail = "" };
                }
            }
            return null;
        }

        private static int ToMinutes(string amount, string unit)
        {
            int value;
            if (amount == "media") value = 30;
            else if (!int.TryParse(amount, out value) && !Numbers.TryGetValue(amount, out value)) return -1;
            if (amount == "media" && unit.StartsWith("hora")) return 30;
            if (unit.StartsWith("hora")) return value * 60;
            if (unit == "segundos") return Math.Max(1, value / 60);
            return value;
        }

        private static string DurationText(int minutes)
        {
            if (minutes % 60 == 0) return minutes / 60 == 1 ? "1 hora" : minutes / 60 + " horas";
            return minutes + " min";
        }

        // Recupera el trozo final de la frase original (con mayúsculas y tildes) que corresponde al texto simplificado
        // Las palabras originales (con tildes) que ocupan el lugar de "part" dentro del texto simplificado
        private static string OriginalWords(string phrase, string simplified, string part)
        {
            int at = simplified.IndexOf(part, StringComparison.Ordinal);
            if (at < 0) return part;
            int skip = simplified.Substring(0, at).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            int take = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            string[] words = phrase.Trim().TrimEnd('.', '!', '?').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (skip + take > words.Length) return part;
            return string.Join(" ", words, skip, take).Trim(',', '.', '«', '»', '"');
        }

        private static string OriginalTail(string phrase, string simplifiedTail)
        {
            string trimmed = phrase.Trim().TrimEnd('.', '!', '?');
            return simplifiedTail.Length <= trimmed.Length ? trimmed.Substring(trimmed.Length - simplifiedTail.Length).Trim() : simplifiedTail;
        }

        // Minúsculas, sin tildes, sin signos ni espacios dobles
        private static string Simplify(string text)
        {
            string decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder plain = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark) continue;
                plain.Append(char.IsLetterOrDigit(c) || c == ' ' ? c : ' ');
            }
            return Regex.Replace(plain.ToString(), @"\s+", " ").Trim();
        }

        private static void PressKey(byte key, int times)
        {
            for (int i = 0; i < times; i++)
            {
                NativeMethods.keybd_event(key, 0, 0, UIntPtr.Zero);
                NativeMethods.keybd_event(key, 0, KeyUp, UIntPtr.Zero);
            }
        }
    }
}
