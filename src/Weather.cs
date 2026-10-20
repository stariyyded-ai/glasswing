using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Glasswing
{
    // ---------------------------------------------------------------- погода

    class WeatherModule : Module
    {
        WeatherCard card;
        public override string Id => "weather";
        public override string Name => "Погода";
        public override string Short => "Сейчас и на 3 дня";
        public override string Description => "Погода в вашем городе сейчас и на три дня вперёд. Без Edge и без рекламы, щелчок открывает подробный прогноз.";
        public override IconKind Icon => IconKind.Weather;
        public override bool Running => card != null && !card.IsDisposed;
        public override bool CanReset => true;

        public static readonly string[] Modes = { "Сейчас и 3 дня", "Только сейчас" };
        public static readonly string[] Units = { "°C", "°F" };

        // Город по умолчанию: Москва. Определять по часовому поясу ненадёжно, поэтому просто даём сменить.
        public string City => Settings.GetString(Id, "City", "Москва");
        public double Lat => Num("Lat", 55.7558);
        public double Lon => Num("Lon", 37.6173);

        double Num(string key, double def)
        {
            object o;
            return Settings.Section(Id).TryGetValue(key, out o) && o is double ? (double)o : def;
        }

        public int Mode
        {
            get { return Math.Max(0, Math.Min(1, Settings.GetInt(Id, "Mode", 0))); }
            set { Settings.Set(Id, "Mode", value); if (Running) card.Refit(); }
        }

        public bool Fahrenheit
        {
            get { return Settings.GetInt(Id, "Units", 0) == 1; }
            set { Settings.Set(Id, "Units", value ? 1 : 0); if (Running) card.Refit(); }
        }

        public bool WarnRain
        {
            get { return Settings.GetBool(Id, "WarnRain", true); }
            set { Settings.Set(Id, "WarnRain", value); if (Running) card.Refit(); }
        }

        public bool TopMost
        {
            get { return Settings.GetBool(Id, "TopMost", false); }
            set { Settings.Set(Id, "TopMost", value); if (Running) card.TopMost = value; }
        }

        public bool Locked
        {
            get { return Settings.GetBool(Id, "Locked", false); }
            set { Settings.Set(Id, "Locked", value); }
        }

        public void SetCity(string name, double lat, double lon)
        {
            Settings.Set(Id, "City", name, false);
            Settings.Set(Id, "Lat", lat, false);
            Settings.Set(Id, "Lon", lon);
            if (Running) card.CityChanged();
        }

        public void PickCity()
        {
            using (var d = new CityDialog(City))
            {
                if (d.ShowDialog() == DialogResult.OK && d.Picked != null)
                    SetCity(d.Picked.Name, d.Picked.Lat, d.Picked.Lon);
            }
        }

        public override void Start()
        {
            if (Running) return;
            card = new WeatherCard(this);
            card.PlaceFromSettings();
            card.Show();
        }

        public override void Stop()
        {
            if (!Running) return;
            card.Close();
            card.Dispose();
            card = null;
        }

        public override void SetHidden(bool hidden) { if (Running) card.Visible = !hidden; }
        // Последний прогноз для питомца: только читать. Пусто, если виджет выключен или ещё грузится.
        public WeatherData Data => Running ? card.Data : null;
        public override void GlassChanged() { if (Running) card.ApplyGlass(); }
        public override bool HasGlass => true;
        public override void ResetPosition() { if (Running) card.ResetPosition(); }

        public override IEnumerable<Row> Rows()
        {
            yield return new Row { Label = "Город", Value = () => City, Click = PickCity };
            yield return new Row { Label = "Что показывать", Value = () => Modes[Mode], Click = () => Mode = (Mode + 1) % Modes.Length };
            yield return new Row { Label = "Температура", Value = () => Units[Fahrenheit ? 1 : 0], Click = () => Fahrenheit = !Fahrenheit };
            yield return new Row { Label = "Предупреждать о дожде и снеге", On = () => WarnRain, Click = () => WarnRain = !WarnRain };
            yield return new Row { Label = "Поверх всех окон", On = () => TopMost, Click = () => TopMost = !TopMost };
            yield return new Row { Label = "Закрепить на месте", On = () => Locked, Click = () => Locked = !Locked };
        }
    }

    // Ответ Open-Meteo. Время в часах и днях местное для города, смещение от UTC лежит в Offset.
    class WeatherData
    {
        public double Lat, Lon;
        public DateTime Fetched;      // UTC
        public int Offset;
        public double Temp, Feels;
        public int Code;
        public bool Day = true;
        public DateTime[] HourTime = new DateTime[0];
        public int[] HourCode = new int[0], HourProb = new int[0];
        public DateTime[] DayDate = new DateTime[0];
        public int[] DayCode = new int[0];
        public double[] DayMax = new double[0], DayMin = new double[0];

        public DateTime LocalNow() { return new DateTime(DateTime.UtcNow.AddSeconds(Offset).Ticks); }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static WeatherData Parse(string json)
        {
            var root = Json.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("ответ не похож на JSON");
            object o;
            if (root.TryGetValue("error", out o) && o is bool && (bool)o)
                throw new Exception(root.TryGetValue("reason", out o) ? Convert.ToString(o) : "ошибка сервера");
            var cur = Obj(root, "current");
            if (cur == null) throw new FormatException("нет current");
            var d = new WeatherData
            {
                Temp = Num(cur, "temperature_2m"),
                Feels = Num(cur, "apparent_temperature"),
                Code = (int)Num(cur, "weather_code", -1),
                Day = Num(cur, "is_day", 1) != 0,
                Offset = (int)Num(root, "utc_offset_seconds", 0)
            };
            if (double.IsNaN(d.Temp)) throw new FormatException("нет температуры");
            if (double.IsNaN(d.Feels)) d.Feels = d.Temp;
            var h = Obj(root, "hourly");
            if (h != null)
            {
                d.HourTime = Times(h, "time", "yyyy-MM-dd'T'HH:mm");
                d.HourCode = Ints(h, "weather_code", d.HourTime.Length);
                d.HourProb = Ints(h, "precipitation_probability", d.HourTime.Length);
            }
            var dy = Obj(root, "daily");
            if (dy != null)
            {
                d.DayDate = Times(dy, "time", "yyyy-MM-dd");
                d.DayCode = Ints(dy, "weather_code", d.DayDate.Length);
                d.DayMax = Nums(dy, "temperature_2m_max", d.DayDate.Length);
                d.DayMin = Nums(dy, "temperature_2m_min", d.DayDate.Length);
            }
            return d;
        }

        static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object o;
            return d.TryGetValue(key, out o) ? o as Dictionary<string, object> : null;
        }

        static double Num(Dictionary<string, object> d, string key, double def = double.NaN)
        {
            object o;
            return d.TryGetValue(key, out o) && o is double ? (double)o : def;
        }

        static List<object> Arr(Dictionary<string, object> d, string key)
        {
            object o;
            return d.TryGetValue(key, out o) ? o as List<object> ?? new List<object>() : new List<object>();
        }

        static DateTime[] Times(Dictionary<string, object> d, string key, string fmt)
        {
            return Arr(d, key).Select(x =>
            {
                DateTime t;
                return x is string && DateTime.TryParseExact((string)x, fmt, Inv, DateTimeStyles.None, out t) ? t : DateTime.MinValue;
            }).ToArray();
        }

        static int[] Ints(Dictionary<string, object> d, string key, int n)
        {
            var a = Arr(d, key);
            var r = new int[n];
            for (int i = 0; i < n; i++) r[i] = i < a.Count && a[i] is double ? (int)Math.Round((double)a[i]) : -1;
            return r;
        }

        static double[] Nums(Dictionary<string, object> d, string key, int n)
        {
            var a = Arr(d, key);
            var r = new double[n];
            for (int i = 0; i < n; i++) r[i] = i < a.Count && a[i] is double ? (double)a[i] : double.NaN;
            return r;
        }
    }

    class WeatherPlace
    {
        public string Name = "", Region = "";
        public double Lat, Lon;
    }

    // Прогноз и поиск городов без ключа. Первым идёт Open-Meteo; если он недоступен
    // (у части российских провайдеров он висит), берём MET Norway, затем wttr.in.
    // Ответы запасных служб пересобираем в формат Open-Meteo, который читает WeatherData.Parse.
    static class WeatherApi
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static readonly string CachePath = Path.Combine(Settings.Dir, "weather.json");
        // MET Norway и Nominatim требуют подпись с контактом, без неё отвечают 403.
        static readonly string Agent = App.Name + "/" + App.Version + " github.com/" + Updater.Repo;

        // Служба, ответившая последней: в следующий раз спрашиваем её первой.
        static volatile int okForecast, okSearch;
        // Смещение от UTC по городам: MET Norway отдаёт время только в UTC.
        static readonly Dictionary<string, int> offsets = new Dictionary<string, int>();
        static int netReady;

        static void Prepare()
        {
            if (Interlocked.Exchange(ref netReady, 1) == 1) return;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            // Без лишнего круга «100-continue» и с короткой памятью DNS: после смены сети адреса не залипают.
            try { ServicePointManager.Expect100Continue = false; } catch { }
            try { ServicePointManager.DnsRefreshTimeout = 60000; } catch { }
        }

        // Запрос с жёстким потолком по времени. Поиск прокси (WPAD) и DNS в .NET Framework идут вне Timeout,
        // поэтому запрос живёт в отдельной задаче и обрывается через Abort, если не уложился.
        static string Get(string url, int timeout)
        {
            Prepare();
            var rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Timeout = timeout;
            rq.ReadWriteTimeout = timeout;
            rq.UserAgent = Agent;
            rq.Accept = "application/json";
            try
            {
                rq.Proxy = WebRequest.DefaultWebProxy;
                if (rq.Proxy != null) rq.Proxy.Credentials = CredentialCache.DefaultCredentials;
            }
            catch { }
            // Без сжатия: встроенная распаковка .NET Framework ломается на части ответов
            // («недопустимые данные при декодировании»). Если gzip всё же пришёл, распаковываем сами.
            rq.AutomaticDecompression = DecompressionMethods.None;
            rq.Headers[HttpRequestHeader.AcceptEncoding] = "identity";
            var task = System.Threading.Tasks.Task.Run(() => Read(rq));
            bool done;
            try { done = task.Wait(timeout + 1000); }
            catch (AggregateException ex) { throw ex.InnerException ?? ex; }
            if (!done)
            {
                try { rq.Abort(); } catch { }
                task.ContinueWith(x => { var e = x.Exception; }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("Время ожидания операции истекло");
            }
            return task.Result;
        }

        static string Read(HttpWebRequest rq)
        {
            byte[] body;
            using (var rs = (HttpWebResponse)rq.GetResponse())
            using (var st = rs.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                st.CopyTo(ms);
                body = ms.ToArray();
            }
            if (body.Length > 2 && body[0] == 0x1f && body[1] == 0x8b)
                using (var gz = new System.IO.Compression.GZipStream(new MemoryStream(body), System.IO.Compression.CompressionMode.Decompress))
                using (var ms = new MemoryStream())
                {
                    gz.CopyTo(ms);
                    body = ms.ToArray();
                }
            return Encoding.UTF8.GetString(body);
        }

        static string Coord(double v) { return v.ToString("0.####", Inv); }

        static string PlaceKey(double lat, double lon) { return Math.Round(lat, 2).ToString(Inv) + "," + Math.Round(lon, 2).ToString(Inv); }

        // Перебирает службы, начиная с последней удачной. Каждая неудача пишется в журнал коротко.
        static T Try<T>(string what, string[] names, Func<int, T> call, ref int ok, Func<T, bool> good)
        {
            Exception last = null;
            T empty = default(T);
            bool gotEmpty = false;
            int first = ok;
            for (int k = 0; k < names.Length; k++)
            {
                int i = (first + k) % names.Length;
                try
                {
                    T r = call(i);
                    if (good(r)) { ok = i; return r; }
                    if (!gotEmpty) { empty = r; gotEmpty = true; }
                }
                catch (Exception ex)
                {
                    last = ex;
                    Log.Write(what + ", " + names[i] + ": " + ex.Message);
                }
            }
            if (gotEmpty) return empty;
            throw last ?? new Exception("нет ответа");
        }

        static readonly string[] ForecastNames = { "open-meteo", "met.no", "wttr.in" };

        public static string Forecast(double lat, double lon)
        {
            int ok = okForecast;
            string json = Try("погода", ForecastNames, i =>
            {
                string s = i == 0 ? OpenMeteo(lat, lon) : i == 1 ? MetNo(lat, lon) : Wttr(lat, lon);
                WeatherData.Parse(s);   // кривой ответ считаем отказом службы
                return s;
            }, ref ok, s => true);
            okForecast = ok;
            return json;
        }

        static string OpenMeteo(double lat, double lon)
        {
            string json = Get("https://api.open-meteo.com/v1/forecast?latitude=" + Coord(lat) + "&longitude=" + Coord(lon) +
                "&current=temperature_2m,apparent_temperature,weather_code,is_day" +
                "&hourly=precipitation_probability,weather_code" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4", 8000);
            var root = Json.Parse(json) as Dictionary<string, object>;
            object o;
            if (root != null && root.TryGetValue("utc_offset_seconds", out o) && o is double) Remember(lat, lon, (int)(double)o);
            return json;
        }

        static void Remember(double lat, double lon, int offset)
        {
            lock (offsets) offsets[PlaceKey(lat, lon)] = offset;
        }

        // Смещение для города: из прошлых ответов, из кэша, по часовому поясу компьютера или по долготе.
        static int OffsetFor(double lat, double lon)
        {
            int off;
            lock (offsets) if (offsets.TryGetValue(PlaceKey(lat, lon), out off)) return off;
            var c = LoadCache(lat, lon);
            if (c != null) return c.Offset;
            double mine = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalHours;
            if (Math.Abs(lon / 15 - mine) <= 1.5) return (int)(mine * 3600);
            return (int)Math.Round(lon / 15) * 3600;
        }

        // MET Norway Locationforecast 2.0 compact: ряд по часам (дальше по 6 часов) в UTC.
        static string MetNo(double lat, double lon)
        {
            string json = Get("https://api.met.no/weatherapi/locationforecast/2.0/compact?lat=" + Coord(lat) + "&lon=" + Coord(lon), 10000);
            var root = Json.Parse(json) as Dictionary<string, object>;
            var props = Obj(root, "properties");
            var ts = Arr(props, "timeseries");
            if (ts.Count == 0) throw new FormatException("пустой прогноз");
            int offset = OffsetFor(lat, lon);
            var now = DateTime.UtcNow;
            var b = new OmBuilder(offset);
            bool cur = false;
            foreach (var x in ts)
            {
                var e = x as Dictionary<string, object>;
                DateTime t;
                if (e == null || !(Str(e, "time") is string) ||
                    !DateTime.TryParse(Str(e, "time"), Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) continue;
                var data = Obj(e, "data");
                var inst = Obj(Obj(data, "instant"), "details");
                double temp = Num(inst, "air_temperature");
                var n1 = Obj(data, "next_1_hours");
                var n6 = Obj(data, "next_6_hours");
                int code1 = MetCode(Str(Obj(n1, "summary"), "symbol_code"));
                int code6 = MetCode(Str(Obj(n6, "summary"), "symbol_code"));
                if (code1 < 0) code1 = MetCode(Str(Obj(Obj(data, "next_12_hours"), "summary"), "symbol_code"));
                if (code6 < 0) code6 = code1;
                // текущая погода: последняя точка не позже чем через полчаса
                if (!double.IsNaN(temp) && (!cur || t <= now.AddMinutes(30)))
                {
                    double feels = Feels(temp, Num(inst, "wind_speed", 0), Num(inst, "relative_humidity", double.NaN));
                    b.Current(temp, feels, code1 >= 0 ? code1 : code6, SunUp(lat, lon, now));
                    cur = true;
                }
                if (n1 != null && code1 >= 0) b.Hour(t, code1, -1);
                var d6 = Obj(n6, "details");
                b.Day(t, n1 != null && code1 >= 0 ? code1 : code6, temp);
                b.Day(t, -1, Num(d6, "air_temperature_max"));
                b.Day(t, -1, Num(d6, "air_temperature_min"));
            }
            if (!cur) throw new FormatException("нет температуры");
            return b.Json();
        }

        // wttr.in j1: числа строками, время местное, часы через три.
        static string Wttr(double lat, double lon)
        {
            string json = Get("https://wttr.in/" + Coord(lat) + "," + Coord(lon) + "?format=j1", 12000);
            var root = Json.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("ответ не похож на JSON");
            var cc = Arr(root, "current_condition").FirstOrDefault() as Dictionary<string, object>;
            if (cc == null) throw new FormatException("нет current_condition");
            // смещение: местное время наблюдения минус то же время в UTC
            int offset = OffsetFor(lat, lon);
            DateTime lt, ut;
            if (DateTime.TryParseExact(Str(cc, "localObsDateTime") ?? "", "yyyy-MM-dd hh:mm tt", Inv, DateTimeStyles.None, out lt) &&
                DateTime.TryParseExact(Str(cc, "observation_time") ?? "", "hh:mm tt", Inv, DateTimeStyles.None, out ut))
            {
                double m = (lt.TimeOfDay - ut.TimeOfDay).TotalMinutes;
                if (m < -12 * 60) m += 24 * 60;
                if (m > 14 * 60) m -= 24 * 60;
                offset = (int)(Math.Round(m / 15) * 15 * 60);
                Remember(lat, lon, offset);
            }
            var b = new OmBuilder(offset);
            double temp = SNum(cc, "temp_C");
            if (double.IsNaN(temp)) throw new FormatException("нет температуры");
            double feels = SNum(cc, "FeelsLikeC");
            b.Current(temp, double.IsNaN(feels) ? temp : feels, WwoCode((int)SNum(cc, "weatherCode", -1)), SunUp(lat, lon, DateTime.UtcNow));
            foreach (var x in Arr(root, "weather"))
            {
                var w = x as Dictionary<string, object>;
                DateTime day;
                if (w == null || !DateTime.TryParseExact(Str(w, "date") ?? "", "yyyy-MM-dd", Inv, DateTimeStyles.None, out day)) continue;
                b.LocalDay(day, -1, SNum(w, "maxtempC"));
                b.LocalDay(day, -1, SNum(w, "mintempC"));
                foreach (var y in Arr(w, "hourly"))
                {
                    var h = y as Dictionary<string, object>;
                    if (h == null) continue;
                    double hm = SNum(h, "time");
                    if (double.IsNaN(hm)) continue;
                    var t = day.AddHours(Math.Floor(hm / 100));
                    int code = WwoCode((int)SNum(h, "weatherCode", -1));
                    int prob = (int)Math.Max(SNum(h, "chanceofrain", -1), Math.Max(SNum(h, "chanceofsnow", -1), SNum(h, "chanceofthunder", -1)));
                    b.LocalDay(day, code, double.NaN);
                    // карточка ждёт ряд по часам: каждую точку растягиваем на три часа
                    for (int k = 0; k < 3; k++) b.LocalHour(t.AddHours(k), code, prob);
                }
            }
            return b.Json();
        }

        // Собирает JSON в том виде, в каком его отдаёт Open-Meteo (время местное, без секунд).
        sealed class OmBuilder
        {
            readonly int offset;
            readonly SortedDictionary<DateTime, int[]> hours = new SortedDictionary<DateTime, int[]>();
            readonly SortedDictionary<DateTime, double[]> days = new SortedDictionary<DateTime, double[]>();
            double temp = double.NaN, feels;
            int code = -1;
            bool day = true;

            public OmBuilder(int offset) { this.offset = offset; }

            public void Current(double t, double f, int c, bool d) { temp = t; feels = f; code = c; day = d; }

            public void Hour(DateTime utc, int c, int prob) { LocalHour(utc.AddSeconds(offset), c, prob); }

            public void LocalHour(DateTime t, int c, int prob)
            {
                t = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0);
                if (!hours.ContainsKey(t)) hours[t] = new[] { c, prob };
            }

            public void Day(DateTime utc, int c, double t) { LocalDay(utc.AddSeconds(offset).Date, c, t); }

            // День: самый тяжёлый код погоды (как daily weather_code у Open-Meteo), максимум и минимум.
            public void LocalDay(DateTime date, int c, double t)
            {
                double[] v;
                if (!days.TryGetValue(date.Date, out v)) days[date.Date] = v = new[] { -1, double.NaN, double.NaN };
                if (c > v[0]) v[0] = c;
                if (!double.IsNaN(t))
                {
                    if (double.IsNaN(v[1]) || t > v[1]) v[1] = t;
                    if (double.IsNaN(v[2]) || t < v[2]) v[2] = t;
                }
            }

            static object N(double v) { return double.IsNaN(v) ? null : (object)Math.Round(v, 1); }
            static object C(double v) { return v < 0 ? null : (object)v; }

            public string Json()
            {
                var root = new Dictionary<string, object>();
                root["utc_offset_seconds"] = offset;
                var cur = new Dictionary<string, object>();
                cur["temperature_2m"] = N(temp);
                cur["apparent_temperature"] = N(feels);
                cur["weather_code"] = C(code);
                cur["is_day"] = day ? 1 : 0;
                root["current"] = cur;
                var h = new Dictionary<string, object>();
                h["time"] = hours.Keys.Select(t => t.ToString("yyyy-MM-dd'T'HH:mm", Inv)).ToList();
                h["precipitation_probability"] = hours.Values.Select(v => C(v[1])).ToList();
                h["weather_code"] = hours.Values.Select(v => C(v[0])).ToList();
                root["hourly"] = h;
                var dy = new Dictionary<string, object>();
                dy["time"] = days.Keys.Select(t => t.ToString("yyyy-MM-dd", Inv)).ToList();
                dy["weather_code"] = days.Values.Select(v => C(v[0])).ToList();
                dy["temperature_2m_max"] = days.Values.Select(v => N(v[1])).ToList();
                dy["temperature_2m_min"] = days.Values.Select(v => N(v[2])).ToList();
                root["daily"] = dy;
                return Glasswing.Json.Write(root);
            }
        }

        // Ощущаемая температура по Стедману (ветер в м/с), как у Open-Meteo.
        static double Feels(double t, double wind, double rh)
        {
            if (double.IsNaN(rh)) rh = 70;
            double e = rh / 100 * 6.105 * Math.Exp(17.27 * t / (237.7 + t));
            return t + 0.33 * e - 0.70 * wind - 4.00;
        }

        // Солнце над горизонтом (упрощённая формула, точности до пары минут хватает для значка).
        static bool SunUp(double lat, double lon, DateTime utc)
        {
            const double R = Math.PI / 180;
            double n = (utc - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;
            double g = (357.529 + 0.98560028 * n) * R;
            double q = 280.459 + 0.98564736 * n;
            double l = (q + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * R;
            double eps = (23.439 - 0.00000036 * n) * R;
            double ra = Math.Atan2(Math.Cos(eps) * Math.Sin(l), Math.Cos(l));
            double dec = Math.Asin(Math.Sin(eps) * Math.Sin(l));
            double gmst = (18.697374558 + 24.06570982441908 * n) % 24;
            double ha = (gmst * 15 + lon) * R - ra;
            double alt = Math.Asin(Math.Sin(lat * R) * Math.Sin(dec) + Math.Cos(lat * R) * Math.Cos(dec) * Math.Cos(ha));
            return alt > -0.833 * R;
        }

        // symbol_code MET Norway -> код WMO. Суффиксы _day/_night/_polartwilight отбрасываем.
        static int MetCode(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            int u = s.IndexOf('_');
            if (u > 0) s = s.Substring(0, u);
            if (s.EndsWith("andthunder")) return 95;
            switch (s)
            {
                case "clearsky": return 0;
                case "fair": return 1;
                case "partlycloudy": return 2;
                case "cloudy": return 3;
                case "fog": return 45;
                case "lightrain": return 61;
                case "rain": return 63;
                case "heavyrain": return 65;
                case "lightrainshowers": return 80;
                case "rainshowers": return 81;
                case "heavyrainshowers": return 82;
                // мокрый снег у Open-Meteo отдельного кода не имеет, ближе всего снег
                case "lightsleet": return 71;
                case "sleet": return 73;
                case "heavysleet": return 75;
                case "lightsleetshowers": return 85;
                case "sleetshowers": return 85;
                case "heavysleetshowers": return 86;
                case "lightsnow": return 71;
                case "snow": return 73;
                case "heavysnow": return 75;
                case "lightsnowshowers": return 85;
                case "snowshowers": return 85;
                case "heavysnowshowers": return 86;
                default: return -1;
            }
        }

        // Код погоды WorldWeatherOnline (wttr.in) -> код WMO.
        static int WwoCode(int c)
        {
            switch (c)
            {
                case 113: return 0;
                case 116: return 2;
                case 119: case 122: return 3;
                case 143: case 248: return 45;
                case 260: return 48;
                case 176: case 353: return 80;
                case 356: return 81;
                case 359: return 82;
                case 179: case 323: case 326: case 317: case 182: return 71;
                case 329: case 332: case 320: case 227: return 73;
                case 335: case 338: case 230: return 75;
                case 350: case 374: case 377: return 77;
                case 362: case 368: return 85;
                case 365: case 371: return 86;
                case 185: case 263: case 266: return 51;
                case 281: return 56;
                case 284: return 57;
                case 293: case 296: return 61;
                case 299: case 302: return 63;
                case 305: case 308: return 65;
                case 311: return 66;
                case 314: return 67;
                case 200: case 386: case 389: case 392: case 395: return 95;
                default: return -1;
            }
        }

        static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object o;
            return d != null && d.TryGetValue(key, out o) ? o as Dictionary<string, object> : null;
        }

        static List<object> Arr(Dictionary<string, object> d, string key)
        {
            object o;
            return d != null && d.TryGetValue(key, out o) ? o as List<object> ?? new List<object>() : new List<object>();
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object o;
            return d != null && d.TryGetValue(key, out o) ? o as string : null;
        }

        static double Num(Dictionary<string, object> d, string key, double def = double.NaN)
        {
            object o;
            return d != null && d.TryGetValue(key, out o) && o is double ? (double)o : def;
        }

        // Число, записанное строкой (wttr.in, Nominatim) или числом.
        static double SNum(Dictionary<string, object> d, string key, double def = double.NaN)
        {
            object o;
            if (d == null || !d.TryGetValue(key, out o) || o == null) return def;
            if (o is double) return (double)o;
            double v;
            return o is string && double.TryParse((string)o, NumberStyles.Float, Inv, out v) ? v : def;
        }

        static readonly string[] SearchNames = { "open-meteo", "nominatim", "photon" };

        // Пустой ответ тоже повод спросить следующую службу: у Open-Meteo бедный список малых городов.
        public static List<WeatherPlace> Search(string name)
        {
            int ok = okSearch;
            var list = Try("погода, поиск", SearchNames, i => i == 0 ? SearchOpenMeteo(name) : i == 1 ? SearchNominatim(name) : SearchPhoton(name),
                ref ok, r => r.Count > 0);
            okSearch = ok;
            return list;
        }

        static List<WeatherPlace> SearchOpenMeteo(string name)
        {
            string json = Get("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(name) + "&count=5&language=ru&format=json", 8000);
            var list = new List<WeatherPlace>();
            var root = Json.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("ответ не похож на JSON");
            foreach (var x in Arr(root, "results"))
            {
                var d = x as Dictionary<string, object>;
                double la = Num(d, "latitude"), lo = Num(d, "longitude");
                if (d == null || double.IsNaN(la) || double.IsNaN(lo)) continue;
                Add(list, Str(d, "name"), la, lo, Str(d, "admin1"), Str(d, "country"));
            }
            return list;
        }

        static List<WeatherPlace> SearchNominatim(string name)
        {
            string json = Get("https://nominatim.openstreetmap.org/search?q=" + Uri.EscapeDataString(name) +
                "&format=jsonv2&limit=5&accept-language=ru&addressdetails=1&featureType=settlement", 10000);
            var list = new List<WeatherPlace>();
            var res = Json.Parse(json) as List<object>;
            if (res == null) throw new FormatException("ответ не похож на JSON");
            foreach (var x in res)
            {
                var d = x as Dictionary<string, object>;
                double la = SNum(d, "lat"), lo = SNum(d, "lon");
                if (d == null || double.IsNaN(la) || double.IsNaN(lo)) continue;
                var a = Obj(d, "address");
                string n = Str(d, "name");
                if (string.IsNullOrEmpty(n)) n = (Str(d, "display_name") ?? "").Split(',')[0].Trim();
                Add(list, n, la, lo, Str(a, "state") ?? Str(a, "region"), Str(a, "country"));
            }
            return list;
        }

        static List<WeatherPlace> SearchPhoton(string name)
        {
            string json = Get("https://photon.komoot.io/api/?q=" + Uri.EscapeDataString(name) + "&limit=5&osm_tag=place", 10000);
            var list = new List<WeatherPlace>();
            var root = Json.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("ответ не похож на JSON");
            foreach (var x in Arr(root, "features"))
            {
                var f = x as Dictionary<string, object>;
                var c = Arr(Obj(f, "geometry"), "coordinates");
                var p = Obj(f, "properties");
                if (c.Count < 2 || !(c[0] is double) || !(c[1] is double) || p == null) continue;
                Add(list, Str(p, "name"), (double)c[1], (double)c[0], Str(p, "state"), Str(p, "country"));
            }
            return list;
        }

        static void Add(List<WeatherPlace> list, string name, double lat, double lon, params string[] region)
        {
            var p = new WeatherPlace { Name = name ?? "", Lat = lat, Lon = lon };
            var parts = new List<string>();
            foreach (var s in region)
                if (!string.IsNullOrEmpty(s) && s != p.Name && !parts.Contains(s)) parts.Add(s);
            p.Region = string.Join(", ", parts);
            if (p.Name.Length > 0) list.Add(p);
        }

        // Последний удачный ответ храним на диске: после перезапуска без сети есть что показать.
        public static void SaveCache(string json, double lat, double lon, DateTime fetched)
        {
            try
            {
                var d = new Dictionary<string, object>();
                d["lat"] = lat;
                d["lon"] = lon;
                d["fetched"] = fetched.ToString("o", Inv);
                d["data"] = json;
                string tmp = CachePath + ".tmp";
                File.WriteAllText(tmp, Json.Write(d), new UTF8Encoding(false));
                if (File.Exists(CachePath)) File.Replace(tmp, CachePath, null);
                else File.Move(tmp, CachePath);
            }
            catch (Exception ex) { Log.Write("погода, кэш: " + ex.Message); }
        }

        public static WeatherData LoadCache(double lat, double lon)
        {
            try
            {
                if (!File.Exists(CachePath)) return null;
                var d = Json.Parse(File.ReadAllText(CachePath, Encoding.UTF8)) as Dictionary<string, object>;
                object a, b, f, j;
                if (d == null || !d.TryGetValue("lat", out a) || !d.TryGetValue("lon", out b) || !(a is double) || !(b is double)) return null;
                if (Math.Abs((double)a - lat) > 0.01 || Math.Abs((double)b - lon) > 0.01) return null;
                if (!d.TryGetValue("data", out j) || !(j is string)) return null;
                var w = WeatherData.Parse((string)j);
                w.Lat = lat;
                w.Lon = lon;
                DateTime t;
                w.Fetched = d.TryGetValue("fetched", out f) && f is string && DateTime.TryParse((string)f, Inv, DateTimeStyles.RoundtripKind, out t)
                    ? t.ToUniversalTime() : DateTime.UtcNow.AddHours(-12);
                return w;
            }
            catch { return null; }
        }
    }

    // Фоновый поток: забирает прогноз раз в 30 минут, после ошибки пробует чаще. Окно не ждёт сеть.
    sealed class WeatherSource
    {
        readonly Action<WeatherData, bool> publish;
        readonly object sync = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        double lat, lon;
        DateTime nextAt;
        int fails;
        volatile bool stopped;

        public WeatherSource(Action<WeatherData, bool> publish) { this.publish = publish; }

        public void Start(double la, double lo, DateTime firstAt)
        {
            lat = la;
            lon = lo;
            nextAt = firstAt;
            var t = new Thread(Loop) { IsBackground = true, Name = "weather" };
            t.Start();
        }

        public void Stop() { stopped = true; wake.Set(); }

        public void Place(double la, double lo)
        {
            lock (sync) { lat = la; lon = lo; nextAt = DateTime.UtcNow; fails = 0; }
            wake.Set();
        }

        // Обновить через delay мс (после сна сеть поднимается не сразу).
        public void Refresh(int delay)
        {
            lock (sync)
            {
                var at = DateTime.UtcNow.AddMilliseconds(delay);
                if (at < nextAt) nextAt = at;
            }
            wake.Set();
        }

        void Loop()
        {
            while (!stopped)
            {
                double la, lo, wait;
                lock (sync)
                {
                    wait = (nextAt - DateTime.UtcNow).TotalMilliseconds;
                    la = lat;
                    lo = lon;
                    if (wait <= 0) nextAt = DateTime.UtcNow.AddMinutes(30);
                }
                if (wait > 0)
                {
                    wake.WaitOne((int)Math.Min(wait + 50, 60000));
                    continue;
                }
                WeatherData d = null;
                try
                {
                    string json = WeatherApi.Forecast(la, lo);
                    d = WeatherData.Parse(json);
                    d.Lat = la;
                    d.Lon = lo;
                    d.Fetched = DateTime.UtcNow;
                    WeatherApi.SaveCache(json, la, lo, d.Fetched);
                }
                catch (Exception ex)
                {
                    d = null;
                    if (fails < 2 || fails % 20 == 0) Log.Write("погода: " + ex.Message);
                }
                bool moved;
                lock (sync)
                {
                    moved = la != lat || lo != lon;
                    if (!moved)
                    {
                        if (d != null) fails = 0;
                        else
                        {
                            fails++;
                            int[] retry = { 1, 2, 5, 10, 15 };
                            nextAt = DateTime.UtcNow.AddMinutes(retry[Math.Min(fails, retry.Length) - 1]);
                        }
                    }
                }
                // пока ждали ответ, город сменили: этот прогноз уже не нужен
                if (!moved && !stopped) publish(d, d != null);
            }
        }
    }

    // Картинки погоды рисуем сами, в сетке 48×48.
    static class WeatherIcons
    {
        static readonly Color SunC = Color.FromArgb(255, 245, 196, 81);
        static readonly Color CloudC = Color.FromArgb(255, 223, 230, 240);
        static readonly Color GreyC = Color.FromArgb(255, 196, 205, 218);
        static readonly Color DarkC = Color.FromArgb(255, 170, 180, 196);
        static readonly Color DropC = Color.FromArgb(255, 159, 216, 245);
        static readonly Color MoonC = Color.FromArgb(255, 232, 228, 206);

        public static bool Precip(int c) { return (c >= 51 && c <= 67) || (c >= 71 && c <= 86) || c >= 95; }
        public static bool Snow(int c) { return (c >= 71 && c <= 77) || c == 85 || c == 86; }
        public static bool Storm(int c) { return c >= 95; }

        public static string Describe(int c)
        {
            switch (c)
            {
                case 0: return "ясно";
                case 1: return "почти ясно";
                case 2: return "облачно с прояснениями";
                case 3: return "пасмурно";
                case 45: case 48: return "туман";
                case 51: return "слабая морось";
                case 53: return "морось";
                case 55: return "сильная морось";
                case 56: case 57: return "ледяная морось";
                case 61: return "небольшой дождь";
                case 63: return "дождь";
                case 65: return "сильный дождь";
                case 66: case 67: return "ледяной дождь";
                case 71: return "небольшой снег";
                case 73: return "снег";
                case 75: return "сильный снег";
                case 77: return "снежная крупа";
                case 80: return "небольшой ливень";
                case 81: return "ливень";
                case 82: return "сильный ливень";
                case 85: return "небольшой снегопад";
                case 86: return "снегопад";
                case 95: return "гроза";
                case 96: case 99: return "гроза с градом";
                default: return "";
            }
        }

        public static void Draw(Graphics g, int code, bool day, RectangleF r)
        {
            var st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 48f, r.Height / 48f);
            if (code == 0) { if (day) Disc(g, 24, 24, 10, SunC); else Moon(g, 24, 24, 10); }
            else if (code == 1 || code == 2)
            {
                if (day) Disc(g, 19, 18, 8, SunC); else Moon(g, 19, 17, 8);
                Cloud(g, 0, 0, CloudC);
            }
            else if (code == 3) Cloud(g, 0, -4, GreyC);
            else if (code == 45 || code == 48)
            {
                using (var p = new Pen(GreyC, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(p, 10, 17, 34, 17);
                    g.DrawLine(p, 14, 24, 39, 24);
                    g.DrawLine(p, 9, 31, 33, 31);
                }
            }
            else if (Storm(code))
            {
                Cloud(g, 0, -4, DarkC);
                using (var b = new SolidBrush(SunC))
                    g.FillPolygon(b, new[] { new PointF(26, 32), new PointF(19, 40), new PointF(23.5f, 40), new PointF(20.5f, 46.5f), new PointF(29, 37), new PointF(24.5f, 37), new PointF(27.5f, 32) });
            }
            else if (Snow(code))
            {
                Cloud(g, 0, -3, CloudC);
                using (var b = new SolidBrush(Color.FromArgb(255, 226, 238, 250)))
                {
                    g.FillEllipse(b, 15, 39, 4, 4);
                    g.FillEllipse(b, 23, 42, 4, 4);
                    g.FillEllipse(b, 31, 39, 4, 4);
                }
            }
            else if (Precip(code))
            {
                Cloud(g, 0, -2, CloudC);
                using (var p = new Pen(DropC, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(p, 18, 40, 16, 44);
                    g.DrawLine(p, 26, 40, 24, 44);
                    if (code == 65 || code == 82 || code == 67) g.DrawLine(p, 34, 40, 32, 44);
                }
            }
            else Cloud(g, 0, -4, Color.FromArgb(90, 255, 255, 255));   // нет данных
            g.Restore(st);
        }

        static void Disc(Graphics g, float cx, float cy, float r, Color c)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
        }

        static void Moon(Graphics g, float cx, float cy, float r)
        {
            using (var full = new GraphicsPath())
            using (var bite = new GraphicsPath())
            {
                full.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                bite.AddEllipse(cx - r * 0.45f, cy - r * 1.45f, 2 * r, 2 * r);
                using (var reg = new Region(full))
                using (var b = new SolidBrush(MoonC))
                {
                    reg.Exclude(bite);
                    g.FillRegion(b, reg);
                }
            }
        }

        // Облако из трёх кругов и плоского низа, как на макете.
        static void Cloud(Graphics g, float dx, float dy, Color c)
        {
            using (var p = new GraphicsPath(FillMode.Winding))
            {
                p.AddEllipse(26 + dx, 24 + dy, 14, 14);
                p.AddEllipse(14 + dx, 18 + dy, 20, 20);
                p.AddEllipse(9 + dx, 27 + dy, 11, 11);
                p.AddRectangle(new RectangleF(14.5f + dx, 31 + dy, 18.5f, 7));
                using (var b = new SolidBrush(c)) g.FillPath(b, p);
            }
        }
    }

    class WeatherCard : GlassCard
    {
        static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly string[] WeekDays = { "Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };
        static readonly Color ColSub = Color.FromArgb(255, 201, 208, 219);
        static readonly Color HintBg = Color.FromArgb(31, 120, 200, 245);
        static readonly Color HintInk = Color.FromArgb(255, 207, 233, 251);
        static Graphics meter;

        readonly WeatherModule wm;
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
        readonly int cardW = Ui.S(330), padX = Ui.S(22), padY = Ui.S(18), iconSz = Ui.S(54), hintH = Ui.S(30), tileH = Ui.S(84), gapY = Ui.S(12);
        WeatherSource source;
        WeatherData data;
        bool failed, pressLocked;
        string layoutKey = "";
        public WeatherData Data => data;

        public WeatherCard(WeatherModule m) : base(m)
        {
            wm = m;
            data = WeatherApi.LoadCache(m.Lat, m.Lon);
            tick.Tick += (s, e) => Refit();
            SystemEvents.PowerModeChanged += PowerChanged;
            NetworkChange.NetworkAvailabilityChanged += NetChanged;
        }

        protected override bool ShowWithoutActivation => true;

        void PowerChanged(object s, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume && source != null) source.Refresh(8000);
        }

        void NetChanged(object s, NetworkAvailabilityEventArgs e)
        {
            if (e.IsAvailable && source != null) source.Refresh(3000);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (source != null) return;
            source = new WeatherSource(Deliver);
            // свежий кэш (меньше 30 минут) не перезапрашиваем сразу
            var first = data != null && (DateTime.UtcNow - data.Fetched).TotalMinutes < 30 ? data.Fetched.AddMinutes(30) : DateTime.UtcNow;
            source.Start(wm.Lat, wm.Lon, first);
            tick.Start();
        }

        void Deliver(WeatherData d, bool ok)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    if (d != null) data = d;
                    failed = !ok;
                    Refit();
                }));
            }
            catch (InvalidOperationException) { }
        }

        public void CityChanged()
        {
            data = null;
            failed = false;
            if (source != null) source.Place(wm.Lat, wm.Lon);
            Refit();
        }

        public void Refit()
        {
            FitSize();
            Invalidate();
        }

        // ---------------------------------------------------------------- текст

        string Deg(double c)
        {
            if (double.IsNaN(c)) return "—";
            double v = wm.Fahrenheit ? c * 9 / 5 + 32 : c;
            int r = (int)Math.Round(v);
            if (r < 0) return "−" + (-r) + "°";
            return (r > 0 && !wm.Fahrenheit ? "+" : "") + r + "°";
        }

        string Subtitle()
        {
            if (data == null) return wm.City + " · " + (failed ? "нет связи" : "загружаю…");
            string desc = WeatherIcons.Describe(data.Code);
            return wm.City + (desc.Length > 0 ? " · " + desc : "") + ", ощущается как " + Deg(data.Feels);
        }

        // Строка про связь: показываем, только если данные старые или последняя попытка не удалась.
        string Status()
        {
            if (data == null) return null;
            var local = data.Fetched.ToLocalTime();
            string when = local.Date == DateTime.Today ? local.ToString("HH:mm", Ru) : local.ToString("d MMMM, HH:mm", Ru);
            if (failed) return "нет связи · данные на " + when;
            if ((DateTime.UtcNow - data.Fetched).TotalMinutes > 75) return "данные на " + when;
            return null;
        }

        // Подсказка про осадки в ближайшие 12 часов. Возвращает текст и код погоды для значка.
        string Hint(out int code)
        {
            code = -1;
            if (data == null || !wm.WarnRain || data.HourTime.Length == 0) return null;
            var d = data;
            var now = d.LocalNow();
            var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
            int i0 = Array.FindIndex(d.HourTime, t => t >= hour);
            if (i0 < 0) return null;
            Func<int, bool> wet = i => WeatherIcons.Precip(d.HourCode[i]) && (d.HourProb[i] < 0 || d.HourProb[i] >= 40);
            int end = Math.Min(d.HourTime.Length, i0 + 13);
            if (WeatherIcons.Precip(d.Code))
            {
                code = d.Code;
                for (int i = i0 + 1; i < end; i++)
                    if (!wet(i)) return What(d.Code) + " закончится " + When(d.HourTime[i], now);
                return null;
            }
            for (int i = i0; i < end; i++)
            {
                if (!wet(i)) continue;
                code = d.HourCode[i];
                if (d.HourTime[i] <= now) return "Скоро " + What(code).ToLower(Ru);
                return What(code) + " начнётся " + When(d.HourTime[i], now);
            }
            return null;
        }

        static string What(int code)
        {
            return WeatherIcons.Storm(code) ? "Гроза" : WeatherIcons.Snow(code) ? "Снег" : "Дождь";
        }

        static string When(DateTime t, DateTime now)
        {
            return (t.Date > now.Date ? "завтра " : "") + "около " + t.ToString("HH:mm", Ru);
        }

        List<int> Days()
        {
            var r = new List<int>();
            if (data == null || wm.Mode == 1) return r;
            var today = data.LocalNow().Date;
            for (int i = 0; i < data.DayDate.Length && r.Count < 3; i++)
                if (data.DayDate[i] > today) r.Add(i);
            return r;
        }

        // ---------------------------------------------------------------- размеры

        static Graphics Meter()
        {
            if (meter == null)
            {
                meter = Graphics.FromImage(new Bitmap(1, 1));
                Ui.Prepare(meter);
            }
            return meter;
        }

        int TextX => padX + iconSz + Ui.S(14);

        int SubLines(string sub)
        {
            float w = cardW - TextX - padX;
            float h = Meter().MeasureString(sub, Ui.Font(13), (int)w).Height;
            return h > Ui.S(24) ? 2 : 1;
        }

        int TopBlock(string sub, string status)
        {
            int h = Ui.S(46) + SubLines(sub) * Ui.S(18) + (status != null ? Ui.S(18) : 0);
            return Math.Max(iconSz, h);
        }

        protected override Size CardSize()
        {
            int code;
            string sub = Subtitle(), status = Status(), hint = Hint(out code);
            int h = padY + TopBlock(sub, status);
            if (hint != null) h += gapY + hintH;
            if (Days().Count > 0) h += gapY + tileH;
            return new Size(cardW, h + padY);
        }

        protected override Point DefaultLocation(Rectangle wa)
        {
            int y = Math.Max(wa.Top, Math.Min(wa.Top + Ui.S(380), wa.Bottom - CardSize().Height - Ui.S(16)));
            return new Point(wa.Right - cardW - Ui.S(40), y);
        }

        // ---------------------------------------------------------------- отрисовка

        protected override void DrawContent(Graphics g, int w, int h)
        {
            int code;
            string sub = Subtitle(), status = Status(), hint = Hint(out code);
            int block = TopBlock(sub, status);
            float tx = TextX, tw = w - tx - padX;

            WeatherIcons.Draw(g, data != null ? data.Code : -1, data == null || data.Day,
                new RectangleF(padX, padY + (block - iconSz) / 2f, iconSz, iconSz));

            int lines = SubLines(sub);
            float textH = Ui.S(46) + lines * Ui.S(18) + (status != null ? Ui.S(18) : 0);
            float ty = padY + (block - textH) / 2f;
            Ui.Text2(g, data != null ? Deg(data.Temp) : "—", Ui.Font(42, false, "Segoe UI Light"), Ui.Text,
                new RectangleF(tx - Ui.S(3), ty - Ui.S(4), tw + Ui.S(3), Ui.S(52)), Ui.Left, true);
            ty += Ui.S(46);
            Ui.Text2(g, sub, Ui.Font(13), ColSub, new RectangleF(tx, ty, tw, lines * Ui.S(18) + Ui.S(2)), lines > 1 ? Ui.Wrap : Ui.Left, true);
            ty += lines * Ui.S(18);
            if (status != null) Ui.Text2(g, status, Ui.Font(11.5), Ui.Faint, new RectangleF(tx, ty, tw, Ui.S(18)), Ui.Left, true);

            float y = padY + block;
            if (hint != null)
            {
                y += gapY;
                var hr = new RectangleF(padX, y, w - 2 * padX, hintH);
                Ui.FillRound(g, hr, Ui.S(10), HintBg);
                WeatherIcons.Draw(g, code, true, new RectangleF(hr.X + Ui.S(8), hr.Y + (hintH - Ui.S(18)) / 2f, Ui.S(18), Ui.S(18)));
                Ui.Text2(g, hint, Ui.Font(12), HintInk, new RectangleF(hr.X + Ui.S(32), hr.Y, hr.Width - Ui.S(40), hintH), Ui.Left);
                y += hintH;
            }

            var days = Days();
            if (days.Count > 0)
            {
                y += gapY;
                float gap = Ui.S(8), tw3 = (w - 2 * padX - 2 * gap) / 3f;
                for (int k = 0; k < days.Count; k++)
                {
                    int i = days[k];
                    var tr = new RectangleF(padX + k * (tw3 + gap), y, tw3, tileH);
                    Ui.FillRound(g, tr, Ui.S(10), Ui.Panel);
                    string name = WeekDays[(int)data.DayDate[i].DayOfWeek];
                    Ui.Text2(g, name, Ui.Font(12), Ui.Faint, new RectangleF(tr.X, tr.Y + Ui.S(6), tr.Width, Ui.S(18)), Ui.Center);
                    float isz = Ui.S(26);
                    WeatherIcons.Draw(g, data.DayCode[i], true, new RectangleF(tr.X + (tr.Width - isz) / 2f, tr.Y + Ui.S(27), isz, isz));
                    Ui.Text2(g, Deg(data.DayMax[i]) + " / " + Deg(data.DayMin[i]), Ui.Font(13), Ui.Text,
                        new RectangleF(tr.X, tr.Y + Ui.S(57), tr.Width, Ui.S(20)), Ui.Center);
                }
            }

            // размер зависит от подсказки и строки связи: подгоняем, если что-то появилось или пропало
            string key = (hint != null) + "|" + (status != null) + "|" + lines + "|" + days.Count;
            if (key != layoutKey)
            {
                layoutKey = key;
                if (CardSize() != Size) BeginInvoke(new Action(FitSize));
            }
        }

        // ---------------------------------------------------------------- мышь

        void OpenForecast()
        {
            string la = wm.Lat.ToString("0.###", Inv), lo = wm.Lon.ToString("0.###", Inv);
            try { Process.Start(new ProcessStartInfo("https://www.windy.com/" + la + "/" + lo + "?" + la + "," + lo + ",9") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("погода, браузер: " + ex.Message); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var before = Location;
            var t0 = DateTime.UtcNow;
            pressLocked = e.Button == MouseButtons.Left && Locked;
            base.OnMouseDown(e);
            // окно тащили и отпустили на том же месте: это был щелчок
            if (e.Button == MouseButtons.Left && !pressLocked && Location == before &&
                (DateTime.UtcNow - t0).TotalMilliseconds < 1500 && ClientRectangle.Contains(PointToClient(Cursor.Position)))
                OpenForecast();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (pressLocked && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) OpenForecast();
            if (e.Button == MouseButtons.Left) pressLocked = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Cursor != Cursors.Hand) Cursor = Cursors.Hand;
        }

        protected override void AddMenuItems(ContextMenuStrip menu)
        {
            menu.Items.Add(new ToolStripMenuItem("Обновить погоду", null, (s, e) => { if (source != null) source.Refresh(0); }));
            menu.Items.Add(new ToolStripMenuItem("Сменить город…", null, (s, e) => wm.PickCity()));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.PowerModeChanged -= PowerChanged;
                NetworkChange.NetworkAvailabilityChanged -= NetChanged;
                if (source != null) source.Stop();
                tick.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Выбор города: пишем название, Open-Meteo предлагает до пяти вариантов.
    class CityDialog : Form
    {
        readonly GlassInput input;
        readonly Hits hits = new Hits();
        readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 600 };
        readonly int dlgW = Ui.S(400), rowH = Ui.S(50), listTop = Ui.S(104);
        List<WeatherPlace> found = new List<WeatherPlace>();
        string status = "Начните вводить название города", searched;
        string hover;
        int seq, sel;
        bool noGlass;
        public WeatherPlace Picked;

        public CityDialog(string current)
        {
            Text = App.Name + ": город";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            TopMost = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            ClientSize = new Size(dlgW, DialogH());
            input = new GlassInput(this, 80);
            input.Box.Text = current ?? "";
            input.Submit += SubmitQuery;
            input.Cancel += () => { DialogResult = DialogResult.Cancel; Close(); };
            input.Box.TextChanged += (s, e) => { debounce.Stop(); if (input.Text.Trim().Length >= 2) debounce.Start(); };
            input.Box.KeyDown += (s, e) =>
            {
                if (found.Count == 0) return;
                if (e.KeyCode == Keys.Down) { sel = (sel + 1) % found.Count; e.Handled = e.SuppressKeyPress = true; Invalidate(); }
                if (e.KeyCode == Keys.Up) { sel = (sel + found.Count - 1) % found.Count; e.Handled = e.SuppressKeyPress = true; Invalidate(); }
            };
            debounce.Tick += (s, e) => { debounce.Stop(); Search(); };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
        }

        int DialogH() { return listTop + Math.Max(Ui.S(40), found.Count * rowH) + Ui.S(14); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WNative.RoundDark(Handle);
            noGlass = !WNative.SetAcrylic(Handle, 4, 215, 18, 21, 30);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            input.Begin(input.Text, true);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible) BeginInvoke(new Action(() => { if (!IsDisposed && DialogResult == DialogResult.None) Close(); }));
        }

        void SubmitQuery()
        {
            string q = input.Text.Trim();
            if (found.Count > 0 && q == searched) { Pick(found[Math.Min(sel, found.Count - 1)]); return; }
            debounce.Stop();
            Search();
        }

        void Search()
        {
            string q = input.Text.Trim();
            if (q.Length == 0) return;
            int my = ++seq;
            status = "Ищу…";
            Invalidate();
            var t = new Thread(() =>
            {
                List<WeatherPlace> list = null;
                string err = null;
                try { list = WeatherApi.Search(q); }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed || my != seq) return;
                        if (err != null) { Log.Write("погода, поиск: " + err); found = new List<WeatherPlace>(); status = "Нет связи, попробуйте ещё раз"; }
                        else { found = list; status = list.Count == 0 ? "Ничего не нашлось" : ""; }
                        searched = q;
                        sel = 0;
                        ClientSize = new Size(dlgW, DialogH());
                        Invalidate();
                    }));
                }
                catch (InvalidOperationException) { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void Pick(WeatherPlace p)
        {
            Picked = p;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int cw = ClientSize.Width, ch = ClientSize.Height;
            if (cw <= 0 || ch <= 0) return;
            hits.Clear();
            using (var bmp = new Bitmap(cw, ch, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    Ui.Prepare(g);
                    if (noGlass) g.Clear(Ui.Fallback);
                    using (var wash = new SolidBrush(Color.FromArgb(10, 255, 255, 255))) g.FillRectangle(wash, 0, 0, cw, ch);
                    if (WNative.Build < 22000)
                        using (var edge = new Pen(Ui.Edge, 1)) g.DrawRectangle(edge, 0, 0, cw - 1, ch - 1);
                    try { Draw(g, cw); }
                    catch (Exception ex) { Log.Write("город: " + ex.Message); }
                }
                WNative.BlitArgb(e.Graphics, bmp, 0, 0);
            }
        }

        void Draw(Graphics g, int w)
        {
            int x = Ui.S(20);
            Ui.Text2(g, "Город для погоды", Ui.Font(15, true), Ui.Text, new RectangleF(x, Ui.S(12), w - 2 * x, Ui.S(28)), Ui.Left);
            Ui.Text2(g, "Esc — закрыть", Ui.Font(12), Ui.Faint, new RectangleF(x, Ui.S(12), w - 2 * x, Ui.S(28)), Ui.Right);

            var box = new Rectangle(x, Ui.S(48), w - 2 * x, Ui.S(42));
            Ui.FillRound(g, box, Ui.S(10), Ui.PanelHi);
            Ui.StrokeRound(g, box, Ui.S(10), Color.FromArgb(150, Ui.Accent));
            var tr = new RectangleF(box.X + Ui.S(14), box.Y, box.Width - Ui.S(28), box.Height);
            if (input.Text.Length == 0) Ui.Text2(g, "Например, Казань", Ui.Font(14), Ui.Faint, tr, Ui.Left);
            input.Draw(g, tr, Ui.Font(14), Ui.Text, Color.FromArgb(90, Ui.Accent));

            if (found.Count == 0)
            {
                Ui.Text2(g, status, Ui.Font(13), Ui.Muted, new RectangleF(x + Ui.S(4), listTop, w - 2 * x, Ui.S(36)), Ui.Left);
                return;
            }
            for (int i = 0; i < found.Count; i++)
            {
                var p = found[i];
                var r = new Rectangle(x, listTop + i * rowH, w - 2 * x, rowH - Ui.S(4));
                string key = "p:" + i;
                if (i == sel || hover == key) Ui.FillRound(g, r, Ui.S(10), i == sel ? Ui.PanelHi : Ui.Panel);
                Ui.Text2(g, p.Name, Ui.Font(14, true), Ui.Text, new RectangleF(r.X + Ui.S(14), r.Y + Ui.S(4), r.Width - Ui.S(28), Ui.S(22)), Ui.Left);
                Ui.Text2(g, p.Region, Ui.Font(12), Ui.Faint, new RectangleF(r.X + Ui.S(14), r.Y + Ui.S(24), r.Width - Ui.S(28), Ui.S(18)), Ui.Left);
                hits.Add(r, key, () => Pick(p));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string k = hits.KeyAt(e.Location);
            Cursor = hits.CursorAt(e.Location);
            if (k != hover) { hover = k; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && hits.KeyAt(e.Location) == null) WNative.DragWindow(this);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) hits.ClickAt(e.Location);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { debounce.Dispose(); input.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
