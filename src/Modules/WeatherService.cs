using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Clima actual con Open-Meteo (gratuito y sin clave). La ubicación aproximada se obtiene una vez a partir de la IP
    /// (ipapi.co). Se actualiza cada 30 minutos en segundo plano; solo funciona mientras alguien lo tenga activado.
    /// </summary>
    internal class WeatherService
    {
        public class Conditions
        {
            public float TemperatureC;
            public float MaxC, MinC;  // Máxima y mínima de hoy
            public int Code;          // Código WMO de Open-Meteo
            public bool IsDay;
            public string City = "";
            public System.Collections.Generic.List<HourForecast> NextHours = new System.Collections.Generic.List<HourForecast>();

            public string Description
            {
                get { return DescribeCode(Code); }
            }
        }

        private const int RefreshMinutes = 30;
        private const int RetryMinutes = 5;
        private const int HoursAhead = 4;  // Horas de pronóstico que muestra la isla

        private static readonly object _gate = new object();
        private static WeatherService _shared;

        private readonly Timer _timer;
        private double? _latitude, _longitude;
        private string _city = "";
        private bool _usingManual;

        /// <summary>
        /// Vuelve a consultar ya (al cambiar Mi ciudad).
        /// </summary>
        public void RefreshNow()
        {
            try { _timer.Change(0, Timeout.Infinite); } catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Una ciudad encontrada por nombre (buscador de Mi ciudad).
        /// </summary>
        public class Place
        {
            public string Name, Region, Country;
            public double Latitude, Longitude;
            public string Label { get { return Name + (Region.Length > 0 ? ", " + Region : "") + (Country.Length > 0 ? ", " + Country : ""); } }
        }

        /// <summary>
        /// Busca ciudades por nombre con el geocodificador de Open-Meteo (gratis, sin clave). Lento: llamar en segundo plano.
        /// </summary>
        public static System.Collections.Generic.List<Place> SearchPlaces(string name)
        {
            System.Collections.Generic.List<Place> places = new System.Collections.Generic.List<Place>();
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2) return places;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "StreeckSuite/1.0";
                client.Encoding = System.Text.Encoding.UTF8;
                string url = "https://geocoding-api.open-meteo.com/v1/search?count=8&language=es&format=json&name=" + Uri.EscapeDataString(name.Trim());
                object data = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(client.DownloadString(url));
                System.Collections.Generic.Dictionary<string, object> root = data as System.Collections.Generic.Dictionary<string, object>;
                if (root == null || !root.ContainsKey("results")) return places;
                foreach (object item in (System.Collections.IEnumerable)root["results"])
                {
                    System.Collections.Generic.Dictionary<string, object> r = item as System.Collections.Generic.Dictionary<string, object>;
                    if (r == null || !r.ContainsKey("latitude")) continue;
                    places.Add(new Place
                    {
                        Name = Convert.ToString(r["name"]),
                        Region = r.ContainsKey("admin1") ? Convert.ToString(r["admin1"]) : "",
                        Country = r.ContainsKey("country") ? Convert.ToString(r["country"]) : "",
                        Latitude = Convert.ToDouble(r["latitude"], CultureInfo.InvariantCulture),
                        Longitude = Convert.ToDouble(r["longitude"], CultureInfo.InvariantCulture)
                    });
                }
            }
            return places;
        }

        /// <summary>
        /// Instancia única compartida por el reloj y la isla (así no se consulta internet dos veces).
        /// Empieza a consultar la primera vez que alguien la pide.
        /// </summary>
        public static WeatherService Shared
        {
            get
            {
                Start();
                return _shared;
            }
        }

        /// <summary>
        /// Empieza a consultar el clima (no hace nada si ya estaba en marcha).
        /// </summary>
        public static void Start()
        {
            lock (_gate)
            {
                if (_shared == null) _shared = new WeatherService();
            }
        }

        /// <summary>
        /// Se lanza en un hilo en segundo plano cada vez que llegan datos nuevos.
        /// </summary>
        public event Action Updated;

        public Conditions Current { get; private set; }

        private WeatherService()
        {
            _timer = new Timer(state => Refresh(), null, 0, Timeout.Infinite);
        }

        private void Refresh()
        {
            int nextMinutes = RefreshMinutes;
            try
            {
                // .NET Framework 4 no activa TLS 1.2 por defecto y ambos servicios lo exigen
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                using (WebClient client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "StreeckSuite/1.0";
                    // Mi ciudad (Ajustes › Tiempo y agenda): fija; si no, se adivina por la conexión a internet
                    bool manual = SuiteSettings.WeatherCity.Length > 0 && !double.IsNaN(SuiteSettings.WeatherLat) && !double.IsNaN(SuiteSettings.WeatherLon);
                    if (manual)
                    {
                        _latitude = SuiteSettings.WeatherLat;
                        _longitude = SuiteSettings.WeatherLon;
                        _city = SuiteSettings.WeatherCity;
                        _usingManual = true;
                    }
                    else if (_usingManual)
                    {
                        _latitude = null; // Se volvió a «Automática»: hay que adivinar otra vez
                        _usingManual = false;
                    }
                    if (_latitude == null)
                    {
                        string location = client.DownloadString("https://ipapi.co/json/");
                        _latitude = ReadNumber(location, "latitude");
                        _longitude = ReadNumber(location, "longitude");
                        Match city = Regex.Match(location, "\"city\"\\s*:\\s*\"([^\"]*)\"");
                        _city = city.Success ? Regex.Unescape(city.Groups[1].Value) : "";
                    }
                    if (_latitude == null || _longitude == null) throw new InvalidOperationException("No se pudo determinar la ubicación");

                    string url = string.Format(CultureInfo.InvariantCulture,
                        "https://api.open-meteo.com/v1/forecast?latitude={0:0.###}&longitude={1:0.###}&current=temperature_2m,weather_code,is_day" +
                        "&daily=temperature_2m_max,temperature_2m_min&forecast_days=1" +
                        "&hourly=temperature_2m,weather_code,is_day&forecast_hours=" + (HoursAhead + 1) + "&timezone=auto",
                        _latitude, _longitude);
                    string json = client.DownloadString(url);
                    double? max = ReadFirstOfArray(json, "temperature_2m_max");
                    double? min = ReadFirstOfArray(json, "temperature_2m_min");

                    // Solo el bloque "current" (en "current_units" las mismas claves llevan texto, no números)
                    int current = json.IndexOf("\"current\":", StringComparison.Ordinal);
                    string block = current >= 0 ? json.Substring(current) : json;
                    double? temperature = ReadNumber(block, "temperature_2m");
                    double? code = ReadNumber(block, "weather_code");
                    double? isDay = ReadNumber(block, "is_day");
                    if (temperature == null || code == null) throw new InvalidOperationException("Respuesta de clima incompleta");

                    Conditions conditions = new Conditions();
                    conditions.TemperatureC = (float)temperature.Value;
                    conditions.Code = (int)code.Value;
                    conditions.IsDay = isDay == null || isDay.Value >= 1;
                    conditions.MaxC = (float)(max ?? temperature.Value);
                    conditions.MinC = (float)(min ?? temperature.Value);
                    conditions.City = _city;
                    conditions.NextHours = ReadHourly(json);
                    Current = conditions;
                }

                Action handler = Updated;
                if (handler != null) handler();
            }
            catch (Exception ex)
            {
                Log.Error("[WeatherService] No se pudo actualizar el clima: {0}", ex.Message);
                nextMinutes = RetryMinutes;
            }

            try
            {
                _timer.Change(TimeSpan.FromMinutes(nextMinutes), Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Se detuvo mientras se consultaba
            }
        }

        public class HourForecast
        {
            public string Time;        // "21:00"
            public float TemperatureC;
            public int Code;
            public bool IsDay;
        }

        /// <summary>
        /// Las próximas horas del bloque "hourly" (la primera entrada es la hora en curso y se salta).
        /// </summary>
        private static System.Collections.Generic.List<HourForecast> ReadHourly(string json)
        {
            System.Collections.Generic.List<HourForecast> hours = new System.Collections.Generic.List<HourForecast>();
            int start = json.IndexOf("\"hourly\":{", StringComparison.Ordinal);
            if (start < 0) return hours;
            int end = json.IndexOf('}', start);
            string block = end > start ? json.Substring(start, end - start) : json.Substring(start);

            string[] times = ReadArray(block, "time");
            string[] temperatures = ReadArray(block, "temperature_2m");
            string[] codes = ReadArray(block, "weather_code");
            string[] isDay = ReadArray(block, "is_day");
            for (int i = 1; i < times.Length && i < temperatures.Length && hours.Count < HoursAhead; i++)
            {
                HourForecast hour = new HourForecast();
                string time = times[i].Trim('"');
                int t = time.IndexOf('T');
                hour.Time = t >= 0 ? time.Substring(t + 1) : time;
                float temperature;
                float.TryParse(temperatures[i], NumberStyles.Float, CultureInfo.InvariantCulture, out temperature);
                hour.TemperatureC = temperature;
                int code;
                if (i < codes.Length && int.TryParse(codes[i], out code)) hour.Code = code;
                hour.IsDay = i >= isDay.Length || isDay[i] != "0";
                hours.Add(hour);
            }
            return hours;
        }

        private static string[] ReadArray(string block, string key)
        {
            Match match = Regex.Match(block, "\"" + key + "\"\\s*:\\s*\\[([^\\]]*)\\]");
            return match.Success ? match.Groups[1].Value.Split(',') : new string[0];
        }

        // "temperature_2m_max":[24.1] (en "daily_units" la misma clave lleva texto y no coincide)
        private static double? ReadFirstOfArray(string json, string key)
        {
            Match match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\\[\\s*(-?[0-9]+(\\.[0-9]+)?)");
            double value;
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
            return null;
        }

        private static double? ReadNumber(string json, string key)
        {
            Match match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?[0-9]+(\\.[0-9]+)?)");
            double value;
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
            return null;
        }

        /// <summary>
        /// Descripción en español de los códigos WMO que usa Open-Meteo.
        /// </summary>
        public static string DescribeCode(int code)
        {
            if (code == 0) return "Despejado";
            if (code <= 2) return "Parcialmente nublado";
            if (code == 3) return "Nublado";
            if (code == 45 || code == 48) return "Niebla";
            if (code >= 51 && code <= 57) return "Llovizna";
            if (code >= 61 && code <= 67) return "Lluvia";
            if (code >= 71 && code <= 77) return "Nieve";
            if (code >= 80 && code <= 82) return "Chubascos";
            if (code == 85 || code == 86) return "Chubascos de nieve";
            if (code >= 95) return "Tormenta";
            return "Variable";
        }
    }
}
