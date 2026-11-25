using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Lumo
{
    /// <summary>Počasí z Open-Meteo (zdarma, bez účtu a bez klíče).</summary>
    public class WeatherService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public bool HasData { get; private set; }
        public DateTime Updated { get; private set; }
        public double Temp, Feels, Wind, Humidity;
        public int Code;
        public bool IsDay = true;
        public string? RainHint;
        public readonly List<Day> Days = new();

        public record Day(DateTime Date, int Code, double Max, double Min, int RainChance);

        public async Task<bool> RefreshAsync(double lat, double lon)
        {
            try
            {
                string url = string.Format(CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}" +
                    "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m" +
                    "&hourly=precipitation_probability,precipitation" +
                    "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                    "&timezone=Europe%2FPrague&forecast_days=4&forecast_hours=12", lat, lon);

                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                var root = doc.RootElement;

                var cur = root.GetProperty("current");
                Temp = cur.GetProperty("temperature_2m").GetDouble();
                Feels = cur.GetProperty("apparent_temperature").GetDouble();
                Humidity = cur.GetProperty("relative_humidity_2m").GetDouble();
                Wind = cur.GetProperty("wind_speed_10m").GetDouble();
                Code = cur.GetProperty("weather_code").GetInt32();
                IsDay = cur.GetProperty("is_day").GetInt32() == 1;

                // déšť v příštích hodinách
                RainHint = null;
                if (!IsRainy(Code) && root.TryGetProperty("hourly", out var hourly))
                {
                    var times = hourly.GetProperty("time");
                    var prob = hourly.GetProperty("precipitation_probability");
                    var amount = hourly.GetProperty("precipitation");
                    var now = DateTime.Now;
                    for (int i = 0; i < times.GetArrayLength(); i++)
                    {
                        if (!DateTime.TryParse(times[i].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) continue;
                        if (t < now.AddMinutes(-30)) continue;
                        int p = prob[i].ValueKind == JsonValueKind.Number ? prob[i].GetInt32() : 0;
                        double mm = amount[i].ValueKind == JsonValueKind.Number ? amount[i].GetDouble() : 0;
                        if (p >= 60 && mm >= 0.2)
                        {
                            int h = (int)Math.Max(1, Math.Round((t - now).TotalHours));
                            RainHint = h <= 1 ? "Do hodiny začne pršet" : $"Za {h} h začne pršet";
                            break;
                        }
                    }
                }

                Days.Clear();
                var daily = root.GetProperty("daily");
                var dt = daily.GetProperty("time");
                for (int i = 0; i < dt.GetArrayLength(); i++)
                {
                    Days.Add(new Day(
                        DateTime.Parse(dt[i].GetString()!, CultureInfo.InvariantCulture),
                        daily.GetProperty("weather_code")[i].GetInt32(),
                        daily.GetProperty("temperature_2m_max")[i].GetDouble(),
                        daily.GetProperty("temperature_2m_min")[i].GetDouble(),
                        daily.GetProperty("precipitation_probability_max")[i].ValueKind == JsonValueKind.Number
                            ? daily.GetProperty("precipitation_probability_max")[i].GetInt32() : 0));
                }

                HasData = true;
                Updated = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Počasí: " + ex.Message);
                return false;
            }
        }

        private static bool IsRainy(int code) => code >= 51;

        /// <summary>WMO kód počasí → symbol (Segoe UI Symbol) a český popis.</summary>
        public static (string Icon, string Text) Describe(int code, bool isDay = true) => code switch
        {
            0 => (isDay ? "☀" : "☾", "Jasno"),
            1 => (isDay ? "🌤" : "☾", "Skoro jasno"),
            2 => ("⛅", "Polojasno"),
            3 => ("☁", "Zataženo"),
            45 or 48 => ("🌫", "Mlha"),
            51 or 53 or 55 => ("🌦", "Mrholení"),
            56 or 57 => ("🌧", "Mrznoucí mrholení"),
            61 => ("🌦", "Slabý déšť"),
            63 => ("🌧", "Déšť"),
            65 => ("🌧", "Silný déšť"),
            66 or 67 => ("🌧", "Mrznoucí déšť"),
            71 => ("🌨", "Slabé sněžení"),
            73 => ("🌨", "Sněžení"),
            75 => ("❄", "Silné sněžení"),
            77 => ("🌨", "Sněhová zrna"),
            80 or 81 => ("🌦", "Přeháňky"),
            82 => ("🌧", "Silné přeháňky"),
            85 or 86 => ("🌨", "Sněhové přeháňky"),
            95 => ("⛈", "Bouřka"),
            96 or 99 => ("⛈", "Bouřka s kroupami"),
            _ => ("☁", "—")
        };
    }
}
