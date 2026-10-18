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

    // Open-Meteo: прогноз и поиск городов без ключа.
    static class WeatherApi
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static readonly string CachePath = Path.Combine(Settings.Dir, "weather.json");

        static string Get(string url)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Timeout = 15000;
            rq.ReadWriteTimeout = 15000;
            rq.UserAgent = App.Name + "/" + App.Version;
            rq.Accept = "application/json";
            // Без сжатия: встроенная распаковка .NET Framework ломается на части ответов
            // («недопустимые данные при декодировании»). Если gzip всё же пришёл, распаковываем сами.
            rq.AutomaticDecompression = DecompressionMethods.None;
            rq.Headers[HttpRequestHeader.AcceptEncoding] = "identity";
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

        public static string Forecast(double lat, double lon)
        {
            return Get("https://api.open-meteo.com/v1/forecast?latitude=" + Coord(lat) + "&longitude=" + Coord(lon) +
                "&current=temperature_2m,apparent_temperature,weather_code,is_day" +
                "&hourly=precipitation_probability,weather_code" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4");
        }

        public static List<WeatherPlace> Search(string name)
        {
            string json = Get("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(name) + "&count=5&language=ru&format=json");
            var list = new List<WeatherPlace>();
            var root = Json.Parse(json) as Dictionary<string, object>;
            object o;
            var res = root != null && root.TryGetValue("results", out o) ? o as List<object> : null;
            if (res == null) return list;
            foreach (var x in res)
            {
                var d = x as Dictionary<string, object>;
                if (d == null || !(d.TryGetValue("latitude", out o) && o is double)) continue;
                var p = new WeatherPlace { Lat = (double)o };
                if (!(d.TryGetValue("longitude", out o) && o is double)) continue;
                p.Lon = (double)o;
                p.Name = d.TryGetValue("name", out o) ? Convert.ToString(o) : "";
                var parts = new List<string>();
                foreach (var k in new[] { "admin1", "country" })
                {
                    string s = d.TryGetValue(k, out o) ? Convert.ToString(o) : "";
                    if (s.Length > 0 && s != p.Name && !parts.Contains(s)) parts.Add(s);
                }
                p.Region = string.Join(", ", parts);
                if (p.Name.Length > 0) list.Add(p);
            }
            return list;
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
