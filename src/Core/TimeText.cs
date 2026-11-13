using System;
using System.Globalization;

namespace StreeckSuite.Core
{
    /// <summary>
    /// La hora como la quiere el usuario: 24 h, 12 h, o automática (el formato que tiene Windows en Región).
    /// </summary>
    internal static class TimeText
    {
        public static bool Use12h
        {
            get
            {
                switch (SuiteSettings.TimeFormat)
                {
                    case SuiteSettings.TimeFormatKind.H12: return true;
                    case SuiteSettings.TimeFormatKind.H24: return false;
                    default: return WindowsUses12h();
                }
            }
        }

        private static bool _windows12h;
        private static DateTime _checkedAt = DateTime.MinValue;

        // Windows guarda el formato de hora en Región; .NET lo lee con las preferencias del usuario incluidas.
        // Se vuelve a mirar cada 30 s por si lo cambian, sin leerlo en cada cuadro de la isla.
        public static bool WindowsUses12h()
        {
            if ((DateTime.UtcNow - _checkedAt).TotalSeconds < 30) return _windows12h;
            _checkedAt = DateTime.UtcNow;
            try
            {
                CultureInfo culture = new CultureInfo(CultureInfo.CurrentCulture.Name, true);
                _windows12h = culture.DateTimeFormat.ShortTimePattern.Replace("'", "").IndexOf('h') >= 0;
            }
            catch (Exception)
            {
                _windows12h = false;
            }
            return _windows12h;
        }

        /// <summary>Hora corta para textos: «20:06» o «8:06 pm».</summary>
        public static string Short(DateTime time)
        {
            if (!Use12h) return time.ToString("HH:mm", CultureInfo.InvariantCulture);
            return time.ToString("h:mm", CultureInfo.InvariantCulture) + (time.Hour < 12 ? " am" : " pm");
        }

        /// <summary>Formato de los números grandes del reloj 3D (sin am/pm, como el reloj de un celular).</summary>
        public static string ClockFormat(bool seconds)
        {
            if (Use12h) return seconds ? "h:mm:ss" : "h:mm";
            return seconds ? "HH:mm:ss" : "HH:mm";
        }
    }
}
