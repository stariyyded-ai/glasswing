using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Glasswing
{
    // ---------------------------------------------------------------- питомец

    class PetModule : Module
    {
        PetLife life;
        bool hidden;
        public override string Id => "fox";
        public override string Name => "Питомец";
        public override string Short => "Живёт внизу экрана";
        public override string Description => "Питомец внизу экрана: растёт от малыша до взрослого, играет, болтает и помогает с делами. Персонажа можно выбрать: " +
            string.Join(", ", PetChar.Available().Select(c => c.Title)) + ".";
        public override IconKind Icon => IconKind.Fox;
        public override bool Running => life != null;
        public override bool CanReset => true;
        public bool Hidden => hidden;

        public bool TopMost
        {
            get { return Settings.GetBool(Id, "TopMost", false); }
            set { Settings.Set(Id, "TopMost", value); if (Running) life.ApplyTopMost(); }
        }

        public bool Locked
        {
            get { return Settings.GetBool(Id, "Locked", false); }
            set { Settings.Set(Id, "Locked", value); }
        }

        public bool Walk
        {
            get { return Settings.GetBool(Id, "Walk", true); }
            set { Settings.Set(Id, "Walk", value); }
        }

        public bool Breaks
        {
            get { return Settings.GetBool(Id, "Breaks", true); }
            set { Settings.Set(Id, "Breaks", value); }
        }

        public PetChar Char => PetChar.Find(Settings.GetString(Id, "Char", "kitsune"));

        // Смена персонажа: рост, еда, настроение и напоминания остаются, а имя у каждого персонажа своё.
        public void SetChar(PetChar c)
        {
            if (c == null) return;
            var old = Char;
            Settings.Set(Id, "Char", c.Id);
            if (Running) { life.SwitchChar(old, c); return; }
            var st = PetState.Load();
            st.SwapName(old, c); st.Save();
        }

        public override void Start()
        {
            if (Running || PetChar.Available().Count == 0) return;
            life = new PetLife(this);
            life.Start();
            if (hidden) life.SetHidden(true);
        }

        public override void Stop()
        {
            if (!Running) return;
            var l = life;
            life = null;
            l.Dispose();
        }

        public override void SetHidden(bool hide) { hidden = hide; if (Running) life.SetHidden(hide); }
        public override void ResetPosition() { if (Running) life.ResetPosition(); }

        public override IEnumerable<Row> Rows()
        {
            yield return new Row
            {
                Label = "Персонаж",
                Value = () => Char == null ? "нет рисунков" : Char.Title,
                Click = () =>
                {
                    var list = PetChar.Available();
                    if (list.Count < 2) return;
                    SetChar(list[(list.IndexOf(Char) + 1) % list.Count]);
                },
            };
            yield return new Row { Label = "Поверх всех окон", On = () => TopMost, Click = () => TopMost = !TopMost };
            yield return new Row { Label = "Закрепить на месте", On = () => Locked, Click = () => Locked = !Locked };
            yield return new Row { Label = "Гулять по экрану", On = () => Walk, Click = () => Walk = !Walk };
            yield return new Row { Label = "Напоминать о перерывах", On = () => Breaks, Click = () => Breaks = !Breaks };
        }
    }

    class Reminder { public DateTime At; public string Text; }

    // Память питомца: %APPDATA%\Glasswing\pet.json. Настройки окна живут в settings.json, в разделе "fox".
    class PetState
    {
        public static readonly string FilePath = Path.Combine(Settings.Dir, "pet.json");

        public string Name = "Хоно", UserName = "";
        public DateTime Born = DateTime.Now, Last = DateTime.Now;
        public double Xp, Food = 70, Mood = 75;
        public int Fed, Pats;
        public List<string> Notes = new List<string>();
        public List<Reminder> Reminders = new List<Reminder>();
        public Dictionary<string, string> Names = new Dictionary<string, string>();   // имя каждого персонажа

        // Запоминаем имя прежнего персонажа и берём имя нового (или его имя по умолчанию).
        public void SwapName(PetChar old, PetChar c)
        {
            if (old != null) Names[old.Id] = Name;
            string n;
            Name = Names.TryGetValue(c.Id, out n) && !string.IsNullOrEmpty(n) ? n : c.DefName;
        }

        static DateTime Date(object o, DateTime def)
        {
            var s = o as string;
            DateTime d;
            if (s == null) return def;
            var m = Regex.Match(s, @"^/Date\((-?\d+)");          // так пишет старый PowerShell
            if (m.Success) return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(double.Parse(m.Groups[1].Value)).ToLocalTime();
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d) ? (d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : d) : def;
        }

        static double Num(Dictionary<string, object> j, string key, double def)
        {
            object o;
            return j.TryGetValue(key, out o) && o is double ? (double)o : def;
        }

        static IEnumerable<object> Items(object o)
        {
            if (o is List<object>) return (List<object>)o;
            if (o != null) return new[] { o };
            return new object[0];
        }

        public static PetState Load(string path = null)
        {
            var s = new PetState();
            try
            {
                path = path ?? FilePath;
                if (!File.Exists(path)) return s;
                var j = Json.Parse(File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿')) as Dictionary<string, object>;
                if (j == null) return s;
                object o;
                if (j.TryGetValue("Name", out o) && o is string && ((string)o).Length > 0) s.Name = (string)o;
                if (j.TryGetValue("UserName", out o) && o is string) s.UserName = (string)o;
                if (j.TryGetValue("Names", out o) && o is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)o)
                        if (kv.Value is string && ((string)kv.Value).Length > 0) s.Names[kv.Key] = (string)kv.Value;
                s.Born = Date(j.ContainsKey("Born") ? j["Born"] : null, s.Born);
                s.Last = Date(j.ContainsKey("Last") ? j["Last"] : null, s.Last);
                s.Xp = Num(j, "Xp", 0); s.Food = Num(j, "Food", 70); s.Mood = Num(j, "Mood", 75);
                s.Fed = (int)Num(j, "Fed", 0); s.Pats = (int)Num(j, "Pats", 0);
                if (j.TryGetValue("Notes", out o)) s.Notes = Items(o).OfType<string>().Where(x => x.Length > 0).ToList();
                if (j.TryGetValue("Reminders", out o))
                    foreach (var r in Items(o).OfType<Dictionary<string, object>>())
                    {
                        object at, text;
                        if (!r.TryGetValue("At", out at) || !r.TryGetValue("Text", out text)) continue;
                        s.Reminders.Add(new Reminder { At = Date(at, DateTime.Now), Text = Convert.ToString(text) });
                    }
            }
            catch (Exception ex) { Log.Write("питомец, память: " + ex.Message); }
            if (s.Name == "Юки") s.Name = "Хоно";
            return s;
        }

        public void Save(string path = null)
        {
            var o = new Dictionary<string, object>
            {
                ["Name"] = Name, ["UserName"] = UserName, ["Born"] = Born.ToString("o"), ["Xp"] = Xp, ["Food"] = Food, ["Mood"] = Mood,
                ["Last"] = Last.ToString("o"), ["Fed"] = Fed, ["Pats"] = Pats, ["Notes"] = Notes,
                ["Names"] = Names.ToDictionary(kv => kv.Key, kv => (object)kv.Value),
                ["Reminders"] = Reminders.Select(r => new Dictionary<string, object> { ["At"] = r.At.ToString("o"), ["Text"] = r.Text }).ToList(),
            };
            try
            {
                path = path ?? FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Json.Write(o), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception ex) { Log.Write("питомец, сохранение: " + ex.Message); }
        }
    }

    class PetFood { public string Name, Short, Acc, Gen; public int Food, Mood; }

    // Характер питомца: забота, рост, разговоры, игры и напоминания.
    class PetLife : IDisposable
    {
        static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        const double TeenXp = 120, AdultXp = 400;          // сколько заботы нужно, чтобы подрасти

        public static readonly PetFood[] Foods =
        {
            new PetFood { Name = "Инари-суши", Short = "Инари", Acc = "инари", Gen = "инари", Food = 30, Mood = 6 },
            new PetFood { Name = "Рисовый шарик", Short = "Рисовый шарик", Acc = "рисовый шарик", Gen = "рисового шарика", Food = 35, Mood = 5 },
            new PetFood { Name = "Рыбка", Short = "Рыбка", Acc = "рыбку", Gen = "рыбки", Food = 40, Mood = 6 },
            new PetFood { Name = "Ягоды", Short = "Ягоды", Acc = "ягод", Gen = "ягод", Food = 20, Mood = 9 },
        };

        static readonly Dictionary<string, string[]> Say0 = new Dictionary<string, string[]>
        {
            ["baby.idle"] = new[] { "{S}?", "{S}-{s}!", "{S2}!", "…{s}", "Пи!", "{S}? {S}!" },
            ["baby.pat"] = new[] { "{S}-{s}! ♥", "{S2} ♥", "{S}!", "♥" },
            ["baby.hungry"] = new[] { "{S}… ({малыш|малышка} {проголодался|проголодалась})", "Пи-и… ({он|она} хочет кушать)" },
            ["baby.bored"] = new[] { "{S}?.. ({ему|ей} скучно, поиграйте с {ним|ней})", "…{s} ({он|она} скучает по вам)" },
            ["baby.eat"] = new[] { "Ням-ням! {S}!", "Ням! ♥", "{S}-{s}-{s}!" },
            ["baby.full"] = new[] { "{S}! ({он|она} уже {наелся|наелась})" },
            ["baby.talk"] = new[] { "{S}? ({малыш|малышка} пока не понимает слов. Кормите, гладьте и играйте с {ним|ней}, и {он|она} подрастёт)", "{S}-{s}! ♥", "{S2}?" },
            ["baby.wake"] = new[] { "Мм… {s}?" },
            ["teen.idle"] = new[] { "Чем займёмся?", "А можно мне ещё {favAcc}?", "Ты сегодня молодец.", "Если что, я рядом.", "Скажи «помощь», и я расскажу, что умею." },
            ["teen.pat"] = new[] { "Хи-хи, щекотно!", "Ещё!", "{S}! Обожаю, когда ты так делаешь.", "Мур… то есть {s}!" },
            ["teen.hungry"] = new[] { "Я {проголодался|проголодалась}… покормишь?", "В животике урчит. Может, {favAcc}?" },
            ["teen.bored"] = new[] { "Мне скучно. Давай поиграем в мячик!", "Эй, я тут! Погладь меня." },
            ["teen.eat"] = new[] { "Вкуснотища!", "Ням! Спасибо!", "Ещё бы одну порцию…" },
            ["teen.full"] = new[] { "Я уже {сыт|сыта}, спасибо!" },
            ["teen.wake"] = new[] { "Ой… я {задремал|задремала}." },
            ["adult.idle"] = new[] { "Не забывай пить воду.", "Если нужно о чём-то напомнить, просто скажи.", "Сделай паузу и посмотри в окно.", "Хочешь, запишу что-нибудь важное?" },
            ["adult.pat"] = new[] { "Спасибо, мне приятно.", "Ты самый лучший.", "{S}… как же хорошо." },
            ["adult.hungry"] = new[] { "Я бы не {отказался|отказалась} от {favGen}." },
            ["adult.bored"] = new[] { "Я {соскучился|соскучилась}. Может, сыграем в мячик?", "Посиди со мной немного." },
            ["adult.eat"] = new[] { "Благодарю. Очень вкусно.", "Ты знаешь, как меня порадовать." },
            ["adult.full"] = new[] { "Спасибо, я {сыт|сыта}." },
            ["adult.wake"] = new string[0],
            ["fine"] = new[] { "Хорошо, ведь ты рядом.", "Замечательно. А у тебя?" },
            ["love"] = new[] { "И я тебя! ♥", "{S}! ♥" },
            ["joke"] = new[] { "Я не ленюсь. Я экономлю магию.", "{Сказал|Сказала} компьютеру «спи», а он ушёл в обновление. Вот и я так же." },
        };

        readonly PetModule mod;
        readonly PetBody pet = new PetBody();
        readonly PetForm form = new PetForm();
        readonly PetInput input = new PetInput();
        readonly Timer anim = new Timer { Interval = 33 }, life = new Timer { Interval = 1000 };
        readonly Random rnd = new Random();
        readonly PetState P;
        PetChar ch;
        DateTime lastTouch = DateTime.Now, lastTalk = DateTime.Now, lastPatXp = DateTime.MinValue, lastFeedXp = DateTime.MinValue,
                 lastTalkXp = DateTime.MinValue, lastGameXp = DateTime.MinValue;
        DateTime nextIdle = DateTime.Now.AddMinutes(4), nextNeed = DateTime.Now.AddMinutes(2), nextWalk = DateTime.Now.AddMinutes(2);
        DateTime? restoreTop;
        double activeMin, away;
        int gameMode;
        float? homeX;
        bool hidden;
        Point savedPos;

        static int S(double v) { return Ui.S(v); }

        public PetLife(PetModule m)
        {
            mod = m;
            P = PetState.Load();
            ch = m.Char;
            pet.Art = PetArt.Load(ch);

            // пока компьютер был выключен, питомец немного проголодался и соскучился
            away = (DateTime.Now - P.Last).TotalMinutes;
            if (away > 1)
            {
                double mm = Math.Min(away, 16 * 60);
                P.Food = Math.Max(8, P.Food - mm * 0.06);
                P.Mood = Math.Max(12, P.Mood - mm * 0.05);
            }

            pet.Scale = Ui.Scale;
            pet.W = S(450);
            pet.H = S(400);
            pet.Stage = GetStage();
            pet.Main = form;
            UpdateInfo();

            form.Text = App.Name + ": " + m.Name;
            form.TopMost = mod.TopMost;
            var pos = DefaultPos();
            if (Settings.Has(m.Id, "CX"))
            {
                var saved = new Point(Settings.GetInt(m.Id, "CX", 0) - pet.W / 2, Settings.GetInt(m.Id, "Y", 0));
                var probe = new Point(saved.X + pet.W / 2, saved.Y + pet.H - S(40));
                if (Screen.AllScreens.Any(s => s.Bounds.Contains(probe))) pos = saved;
            }
            form.Bounds = new Rectangle(pos.X, pos.Y, pet.W, pet.H);
            savedPos = pos;

            form.MouseMove += (s, e) => pet.HoverUntil = pet.T() + 1.6;
            form.MouseDown += OnMouseDown;
            form.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) { Touch(); BuildMenu().Show(form, e.Location); } };
            form.Shown += (s, e) => { anim.Start(); life.Start(); Greet(); };
            input.Submit += OnInput;

            anim.Tick += (s, e) =>
            {
                if (hidden) return;
                try { pet.Animate(); }
                catch (Exception ex) { Log.Write("питомец, кадр: " + ex.Message); }
                string ev = pet.Event;
                if (ev != null) { pet.Event = null; OnPetEvent(ev); }
            };
            life.Tick += (s, e) =>
            {
                try { LifeTick(); }
                catch (Exception ex) { Log.Write("питомец: " + ex.Message); }
            };
        }

        Point DefaultPos()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            return new Point(wa.Right - pet.W - S(10), wa.Bottom - pet.H + S(4));
        }

        public void Start() { form.Show(); SaveState(); }

        public void SetHidden(bool hide)
        {
            hidden = hide;
            if (hide) input.Hide();
            form.Visible = !hide;
            pet.SyncBall();
        }

        public void ApplyTopMost() { if (restoreTop == null) form.TopMost = mod.TopMost; }

        public void ResetPosition()
        {
            pet.StopMode();
            homeX = null;
            form.Location = DefaultPos();
            SaveState();
        }

        public void SwitchChar(PetChar old, PetChar c)
        {
            if (c == ch) return;
            var art = PetArt.Load(c);
            var was = pet.Art;
            pet.Art = art;
            ch = c;
            was.Dispose();
            P.SwapName(old, c);
            UpdateInfo();
            SaveState();
            pet.Celebrate();
            Say(F(GetStage() == "baby" ? "{S}! ♥" : "Привет! Это я, {name}."), 5);
        }

        public void Dispose()
        {
            anim.Stop(); life.Stop();
            pet.StopMode();
            SaveState();
            anim.Dispose(); life.Dispose();
            input.Dispose();
            form.Close();
            form.Dispose();
            pet.Dispose();
            if (pet.Art != null) pet.Art.Dispose();
        }

        // ---------------------------------------------------------------- слова

        // Подставляет слова персонажа: {s} голос, {name} имя, {он|она} по роду и так далее.
        string F(string s)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                int a = s.IndexOf('{', i);
                int b = a < 0 ? -1 : s.IndexOf('}', a);
                if (b < 0) { sb.Append(s, i, s.Length - i); break; }
                sb.Append(s, i, a - i);
                string key = s.Substring(a + 1, b - a - 1);
                int bar = key.IndexOf('|');
                if (bar >= 0) sb.Append(ch.Female ? key.Substring(bar + 1) : key.Substring(0, bar));
                else sb.Append(Word(key));
                i = b + 1;
            }
            return sb.ToString();
        }

        string Word(string key)
        {
            switch (key)
            {
                case "s": return ch.Sound;
                case "S": return Cap(ch.Sound);
                case "S2": return Cap(ch.Sound2);
                case "name": return P.Name;
                case "favAcc": return Foods[Fav].Acc;
                case "favGen": return Foods[Fav].Gen;
                default: return "{" + key + "}";
            }
        }

        int Fav => Math.Max(0, Math.Min(Foods.Length - 1, ch.Fav));
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Ru) + s.Substring(1); }

        string[] Lines(string key)
        {
            string[] a, b;
            Say0.TryGetValue(key, out a);
            ch.Lines.TryGetValue(key, out b);
            return (a ?? new string[0]).Concat(b ?? new string[0]).ToArray();
        }

        string Pick(params string[] list) { return F(list[rnd.Next(list.Length)]); }

        string PickLines(string key)
        {
            var l = Lines(key);
            return l.Length == 0 ? F("{S}.") : Pick(l);
        }

        static string Plural(int n, string one, string few, string many)
        {
            if (n % 10 == 1 && n % 100 != 11) return one;
            if (n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 12 || n % 100 > 14)) return few;
            return many;
        }

        static readonly string[] Numbers = { "ноль", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять" };

        // «три хвоста», «восемь щупалец»
        string Tails(int n) { return (n < Numbers.Length ? Numbers[n] : n.ToString()) + " " + Plural(n, ch.Tail[0], ch.Tail[1], ch.Tail[2]); }
        int TailCount(string st) { return pet.Art.Stages[st].Tails.Length; }

        string StageName(string st)
        {
            if (st == "baby") return F("{малыш|малышка}");
            if (st == "teen") return "подросток";
            return ch.AdultName;
        }

        void Say(string text, double sec = 0)
        {
            if (sec <= 0) sec = Math.Min(22, 3.5 + text.Length / 16.0);
            pet.Say(text, sec);
            lastTalk = DateTime.Now;
        }

        void SayMood(string kind) { Say(PickLines(GetStage() + "." + kind)); }

        void Touch()
        {
            lastTouch = DateTime.Now;
            if (pet.Sleeping) { pet.Sleeping = false; SayMood("wake"); }
        }

        string You => P.UserName.Length > 0 ? ", " + P.UserName : "";

        static string Greeting()
        {
            int h = DateTime.Now.Hour;
            return h < 5 ? "Доброй ночи" : h < 12 ? "Доброе утро" : h < 18 ? "Добрый день" : "Добрый вечер";
        }

        void Greet()
        {
            if (GetStage() == "baby") Say(F(away > 180 ? "{S}-{s}! ♥ ({он|она} {соскучился|соскучилась})" : "{S}!"), 5);
            else Say(away > 180 ? Greeting() + You + F("! Ты вернулся… Я так {скучал|скучала}!") : Greeting() + You + "!", 7);
        }

        // ---------------------------------------------------------------- забота

        void SaveState()
        {
            P.Last = DateTime.Now;
            if (!form.IsDisposed)
            {
                var pos = form.Location;
                if (pos != savedPos)
                {
                    savedPos = pos;
                    Settings.Set(mod.Id, "CX", pos.X + form.Width / 2, false);
                    Settings.Set(mod.Id, "Y", pos.Y);
                }
            }
            P.Save();
        }

        string GetStage() { return P.Xp >= AdultXp ? "adult" : P.Xp >= TeenXp ? "teen" : "baby"; }

        static double Clamp(double v) { return Math.Max(0.0, Math.Min(100.0, v)); }

        string DaysTogether()
        {
            int d = (int)Math.Floor((DateTime.Now - P.Born).TotalDays) + 1;
            return d + " " + Plural(d, "день", "дня", "дней") + " вместе";
        }

        void UpdateInfo()
        {
            string st = GetStage();
            pet.InfoTitle = P.Name + " · " + StageName(st);
            pet.InfoSub = DaysTogether();
            pet.Grow = st == "baby" ? P.Xp / TeenXp : st == "teen" ? (P.Xp - TeenXp) / (AdultXp - TeenXp) : 1.0;
            pet.Mood = P.Mood;
            pet.Food = P.Food;
        }

        void AddXp(double v)
        {
            string before = GetStage();
            P.Xp += v;
            string after = GetStage();
            if (after == before) return;
            UpdateInfo();
            pet.Stage = after;
            pet.Celebrate();
            if (after == "teen")
            {
                string grew = TailCount("teen") > TailCount("baby") ? "У меня теперь " + Tails(TailCount("teen")) + "." : F("Посмотри, как я {подрос|подросла}!");
                Say("Смотри! " + grew + F(" И я {научился|научилась} говорить! Напиши мне что-нибудь."), 12);
            }
            else
            {
                string grew = TailCount("adult") > TailCount("teen") ? Cap(Tails(TailCount("adult"))) + "… " : "Посмотри на меня… ";
                Say(grew + F("Я {вырос|выросла}! Теперь я могу тебе помогать. Скажи «помощь»."), 14);
            }
            SaveState();
        }

        void DoPat()
        {
            Touch();
            P.Mood = Clamp(P.Mood + 7);
            P.Pats++;
            pet.Pat();
            if ((DateTime.Now - lastPatXp).TotalSeconds > 15) { AddXp(1.5); lastPatXp = DateTime.Now; }
            if (rnd.NextDouble() < 0.45 && pet.Bubble == null) SayMood("pat");
            UpdateInfo();
        }

        void DoFeed(int i)
        {
            Touch();
            var fd = Foods[i];
            if (P.Food >= 92) { SayMood("full"); return; }
            P.Food = Clamp(P.Food + fd.Food);
            P.Mood = Clamp(P.Mood + (i == Fav ? 15 : fd.Mood));
            P.Fed++;
            pet.Feed(i);
            if ((DateTime.Now - lastFeedXp).TotalSeconds > 60) { AddXp(4); lastFeedXp = DateTime.Now; }
            string st = GetStage();
            if (i == Fav && st != "baby") Say(st == "teen" ? fd.Short + "! Моё любимое!" : fd.Short + "… ты помнишь, что я люблю.");
            else SayMood("eat");
            UpdateInfo();
            SaveState();
        }

        void AddReminder(DateTime at, string text)
        {
            P.Reminders.Add(new Reminder { At = at, Text = text });
            SaveState();
        }

        // ---------------------------------------------------------------- игры

        void StartGame(int mode)
        {
            Touch();
            if (pet.Mode == 0) homeX = pet.GroundX();
            pet.StopMode();
            pet.StartMode(mode, mode == 2 ? 75 : 45);
            gameMode = mode;
            bool baby = GetStage() == "baby";
            string line =
                mode == 2 ? (baby ? "{S}! {S}! ({он|она} прыгает за мячиком)" : "Мячик! Кидай его, хватай мышкой и бросай, а я буду ловить!") :
                mode == 3 ? (baby ? "{S}-{s}! ({он|она} бежит за курсором)" : "Догонялки! Води курсором, я тебя догоню!") :
                            (baby ? "{S}! ({он|она} убегает, поймай {его|её} щелчком)" : "Попробуй поймать меня! Щёлкни по мне, если догонишь.");
            Say(F(line), 5);
        }

        void EndGame(bool won)
        {
            int mode = gameMode;
            gameMode = 0;
            P.Mood = Clamp(P.Mood + 15);
            if ((DateTime.Now - lastGameXp).TotalMinutes > 3) { AddXp(5); lastGameXp = DateTime.Now; }
            bool baby = GetStage() == "baby";
            if (won)
            {
                pet.Celebrate();
                Say(baby ? F("{S}!! ♥") : Pick("Поймал меня! Твоя взяла ♥", "Ой! Ну ладно, ты быстрее.", "Попался! Давай ещё раз?"), 5);
            }
            else if (baby) Say(F("{S}-{s} ♥ ({он|она} {доволен|довольна} и {запыхался|запыхалась})"), 5);
            else
            {
                string extra = mode == 2 && pet.Kicks > 0 ? F(" Я {отбил|отбила} мячик ") + pet.Kicks + " " + Plural(pet.Kicks, "раз", "раза", "раз") + "!" : "";
                Say(Pick("Уф, {набегался|набегалась}! Было весело.", "Как здорово поиграли!", "Хи-хи, ещё поиграем потом?") + extra, 6);
            }
            pet.Kicks = 0;
            if (mod.Locked && homeX != null) pet.WalkTo(homeX.Value, 220);
            UpdateInfo();
            SaveState();
        }

        void OnPetEvent(string ev)
        {
            if (ev == "kick")
            {
                if (rnd.NextDouble() < 0.12 && pet.Bubble == null) Say(GetStage() == "baby" ? F("{S}!") : Pick("Лови!", "Ещё!", "Хоп!", "Ха!"), 1.5);
            }
            else if (ev == "gameover") EndGame(false);
            else if (ev == "arrived") SaveState();
        }

        // ---------------------------------------------------------------- разговор

        static TimeSpan ParseDelay(string num, string unit)
        {
            double n = double.Parse(num.Replace(',', '.'), CultureInfo.InvariantCulture);
            if (unit.StartsWith("сек")) return TimeSpan.FromSeconds(n);
            if (unit.StartsWith("ч")) return TimeSpan.FromHours(n);
            return TimeSpan.FromMinutes(n);
        }

        static void Run(string what) { Process.Start(new ProcessStartInfo(what) { UseShellExecute = true }); }

        static bool OpenThing(string what)
        {
            string w = what.Trim().ToLower();
            var known = new Dictionary<string, string>
            {
                ["блокнот"] = "notepad.exe", ["калькулятор"] = "calc.exe", ["проводник"] = "explorer.exe", ["паинт"] = "mspaint.exe",
                ["диспетчер задач"] = "taskmgr.exe", ["настройки"] = "ms-settings:", ["параметры"] = "ms-settings:",
                ["загрузки"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                ["документы"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                ["рабочий стол"] = Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ["браузер"] = "https://ya.ru",
            };
            if (known.ContainsKey(w)) { Run(known[w]); return true; }
            var alias = new Dictionary<string, string>
            {
                ["стим"] = "steam", ["дискорд"] = "discord", ["телеграм"] = "telegram", ["телеграмм"] = "telegram", ["хром"] = "chrome",
                ["обс"] = "obs", ["спотифай"] = "spotify", ["ворд"] = "word", ["эксель"] = "excel",
            };
            if (alias.ContainsKey(w)) w = alias[w];
            if (w == "ютуб" || w == "youtube") { Run("https://youtube.com"); return true; }
            var found = new List<string>();
            foreach (var f in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Desktop, Environment.SpecialFolder.CommonDesktopDirectory })
                FindLinks(Environment.GetFolderPath(f), w, found, 0);
            var best = found.OrderBy(p => Path.GetFileNameWithoutExtension(p).Length).FirstOrDefault();
            if (best == null) return false;
            Run(best);
            return true;
        }

        static void FindLinks(string dir, string w, List<string> found, int depth)
        {
            if (string.IsNullOrEmpty(dir) || depth > 6 || !Directory.Exists(dir)) return;
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(f).ToLower();
                    if ((ext == ".lnk" || ext == ".url") && Path.GetFileNameWithoutExtension(f).ToLower().Contains(w)) found.Add(f);
                }
                foreach (var d in Directory.GetDirectories(dir)) FindLinks(d, w, found, depth + 1);
            }
            catch { }
        }

        static string Help(string st)
        {
            if (st == "teen")
                return "Я умею:\n• сказать, который час и какое сегодня число\n• поставить таймер: «таймер 10 минут»\n• подбросить монетку и бросить кубик\n• рассказать шутку\nПодрасту и научусь помогать по-настоящему!";
            return "Я умею:\n• время и дата\n• «таймер 10 минут»\n• «напомни через 20 минут выпить воды»\n• «напомни в 18:30 позвонить маме»\n• «запомни купить молоко», «что ты помнишь», «забудь заметки»\n• «открой steam», «открой блокнот»\n• «посчитай 25*4+7»\n• монетка, кубик, шутка\n• «перерывы выкл» или «перерывы вкл»";
        }

        static Match M(string s, string pattern) { return Regex.Match(s, pattern, RegexOptions.IgnoreCase); }
        static bool Has(string s, string pattern) { return Regex.IsMatch(s, pattern, RegexOptions.IgnoreCase); }

        string GetReply(string text)
        {
            string st = GetStage();
            string t = text.Trim();
            string l = t.ToLower().Replace('ё', 'е');
            Match m;
            P.Mood = Clamp(P.Mood + 3);
            if ((DateTime.Now - lastTalkXp).TotalSeconds > 30) { AddXp(2); lastTalkXp = DateTime.Now; }
            if (Has(l, "мяч|поигра|играть|игра")) { StartGame(2); return null; }
            if (Has(l, "догонял|догони")) { StartGame(3); return null; }
            if (Has(l, "поймай|убегай|прятк")) { StartGame(4); return null; }
            if (st == "baby")
            {
                if (Has(l, "привет|здравствуй|хай")) { pet.Pat(); return F("{S}-{s}! ♥"); }
                return PickLines("baby.talk");
            }

            if (Has(l, "^(помощь|что ты умеешь|команды|help)")) return Help(st);
            if ((m = M(l, @"меня зовут\s+([^\s,.!]+)")).Success)
            {
                P.UserName = Ru.TextInfo.ToTitleCase(m.Groups[1].Value);
                SaveState();
                return "Приятно познакомиться, " + P.UserName + "! Я запомню.";
            }
            if (Has(l, "(как тебя зовут|кто ты|ты кто)")) return "Я " + P.Name + ", " + PickLines("who");
            if (Has(l, "^(привет|здравствуй|хай|салют|добр)")) { pet.Pat(); return Greeting() + You + F("! Я {рад|рада} тебя видеть."); }
            if (Has(l, "как (у тебя )?дела|как ты|как настроение"))
            {
                if (P.Food < 30) return F("Честно? Я {голодный|голодная}. Покормишь меня?");
                if (P.Mood < 30) return "Немножко грустно. Погладь меня, и станет лучше.";
                return PickLines("fine");
            }
            if (Has(l, "который час|сколько времени|время")) return "Сейчас " + DateTime.Now.ToString("HH:mm") + ".";
            if (Has(l, "какое (сегодня )?число|какой (сегодня )?день|дата")) return "Сегодня " + DateTime.Now.ToString("dddd, d MMMM yyyy", Ru) + ".";
            if (Has(l, "спасибо|благодарю")) return Pick("Всегда пожалуйста!", "Обращайся!", "Для тебя что угодно.");
            if (Has(l, "люблю|обожаю|ты милый|ты красивый|ты милая|ты красивая")) { pet.Pat(); P.Mood = Clamp(P.Mood + 10); return PickLines("love"); }
            if (Has(l, "шутк|анекдот|рассмеши")) return PickLines("joke");
            if (Has(l, "монет")) return rnd.Next(2) == 0 ? "Орёл!" : "Решка!";
            if (Has(l, "кубик|кость")) return "Выпало " + rnd.Next(1, 7) + ".";
            if (Has(l, "спокойной ночи|иди спать|спи")) { pet.Sleeping = true; return F("Спокойной ночи… {s}."); }
            if (Has(l, "погод")) return "Я не выглядываю в интернет, так что погоду не знаю. Но у нас тут тепло и уютно.";
            if (Has(l, "^пока|до свидания|до завтра")) return Pick("Пока! Я буду ждать.", "До встречи!");
            if ((m = M(l, @"таймер\s+(?:на\s+)?(\d+(?:[.,]\d+)?)\s*(сек\w*|мин\w*|ч\w*)")).Success)
            {
                var span = ParseDelay(m.Groups[1].Value, m.Groups[2].Value);
                string n = m.Groups[1].Value, u = m.Groups[2].Value;
                AddReminder(DateTime.Now + span, "Таймер на " + n + " " + u + " закончился!");
                return F("{Засёк|Засекла} ") + n + " " + u + ". Скажу, когда время выйдет.";
            }

            if (st != "adult")
            {
                if (Has(l, "напомни|запомни|запиши|открой|посчитай|сколько будет")) return "Этому я научусь, когда вырасту. Корми и гладь меня, и это будет скоро!";
                return Pick("Интересно! Расскажи ещё.", "Хм, я пока не всё понимаю. Скажи «помощь».", "{S}? Я ещё учусь.");
            }

            // взрослый питомец помогает
            if ((m = M(l, @"напомни\s*(?:мне\s*)?через\s+(?:(\d+(?:[.,]\d+)?)\s*)?(сек\w*|мин\w*|час\w*|полчаса)\s*(.*)")).Success)
            {
                string num = m.Groups[1].Value, unit = m.Groups[2].Value, what = m.Groups[3].Value.Trim();
                var span = unit == "полчаса" ? TimeSpan.FromMinutes(30) : ParseDelay(num.Length > 0 ? num : "1", unit);
                if (what.Length == 0) what = "ты просил(а) напомнить";
                var at = DateTime.Now + span;
                AddReminder(at, what);
                return "Хорошо, в " + at.ToString("HH:mm") + " напомню: " + what + ".";
            }
            if ((m = M(l, @"напомни\s*(?:мне\s*)?(?:в|на)\s+(\d{1,2})[:.](\d{2})\s*(.*)")).Success)
            {
                var at = DateTime.Today.AddHours(int.Parse(m.Groups[1].Value)).AddMinutes(int.Parse(m.Groups[2].Value));
                if (at < DateTime.Now) at = at.AddDays(1);
                string what = m.Groups[3].Value.Trim();
                if (what.Length == 0) what = "ты просил(а) напомнить";
                AddReminder(at, what);
                string day = at.Date == DateTime.Today ? "сегодня" : "завтра";
                return F("{Записал|Записала}: ") + day + " в " + at.ToString("HH:mm") + " напомню — " + what + ".";
            }
            if (Has(l, "напомина") && Has(l, "мои|какие|список|покажи"))
            {
                if (P.Reminders.Count == 0) return "Напоминаний нет.";
                return "Напоминания:\n" + string.Join("\n", P.Reminders.OrderBy(r => r.At).Select(r => "• " + r.At.ToString("dd.MM HH:mm") + " — " + r.Text));
            }
            if (Has(l, "отмени напомин|удали напомин")) { P.Reminders.Clear(); SaveState(); return "Все напоминания отменены."; }
            if ((m = M(l, @"^(запомни|запиши|заметка)[:\s]+(.+)")).Success)
            {
                string w = m.Groups[1].Value;
                int at = t.ToLower().IndexOf(w, StringComparison.Ordinal);
                string note = (at >= 0 ? t.Substring(at + w.Length) : m.Groups[2].Value).Trim(' ', ':');
                P.Notes.Add(DateTime.Now.ToString("dd.MM") + " " + note);
                SaveState();
                return F("{Запомнил|Запомнила}!");
            }
            if (Has(l, "что ты помнишь|мои заметки|заметки|что записано"))
            {
                if (P.Notes.Count == 0) return "Пока ничего не записано. Скажи «запомни …».";
                return "Я помню:\n" + string.Join("\n", P.Notes.Skip(Math.Max(0, P.Notes.Count - 6)).Select(n => "• " + n));
            }
            if (Has(l, "забудь (все|заметки)|очисти заметки")) { P.Notes.Clear(); SaveState(); return F("Всё {забыл|забыла}. Как будто и не было."); }
            if ((m = M(l, @"^открой\s+(.+)")).Success)
            {
                string what = m.Groups[1].Value;
                try { if (OpenThing(what)) return "Открываю " + what + "."; } catch { }
                return F("Не {нашёл|нашла} «") + what + "» среди программ.";
            }
            if ((m = M(l, @"^(посчитай|сколько будет|вычисли)\s+(.+)")).Success)
            {
                string expr = Regex.Replace(m.Groups[2].Value.Replace(',', '.'), "[x×х]", "*").Replace(':', '/').Replace("?", "").Trim();
                double v;
                if (Regex.IsMatch(expr, @"^[\d\s\.\+\-\*/\(\)%]+$") && PetCalc.TryEval(expr, out v))
                    return "Получается " + Math.Round(v, 6).ToString(CultureInfo.InvariantCulture) + ".";
                return "Не получилось посчитать. Попробуй так: «посчитай 25*4+7».";
            }
            if (Has(l, @"перерыв\w*\s+(выкл|нет|не надо)")) { mod.Breaks = false; return "Хорошо, не буду напоминать о перерывах."; }
            if (Has(l, @"перерыв\w*\s+(вкл|да)")) { mod.Breaks = true; return "Буду напоминать размяться каждые полтора часа."; }
            return Pick("Интересно! Расскажи ещё.", "Я тебя слушаю.", "Хм… Если нужна помощь, скажи «помощь».", "{S}. Я подумаю над этим.");
        }

        // ---------------------------------------------------------------- окно ввода (разговор и имя)

        void OnInput(string text)
        {
            if (text.Length == 0) { input.Hide(); return; }
            Touch();
            if (input.Mode == "name")
            {
                P.Name = text.Substring(0, Math.Min(20, text.Length));
                SaveState(); UpdateInfo(); input.Hide();
                Say(GetStage() == "baby" ? F("{S}! ♥ ({ему|ей} нравится имя «") + P.Name + "»)" : P.Name + "? Красиво! Мне нравится.");
                return;
            }
            string r;
            try { r = GetReply(text); }
            catch { r = F("{S}? Я {запутался|запуталась}."); }
            if (r != null) Say(r);
            UpdateInfo();
        }

        void ShowInput(string mode)
        {
            input.Mode = mode;
            var wa = Screen.FromControl(form).WorkingArea;
            int x = form.Left + (form.Width - input.Width) / 2;
            int y = form.Bottom + S(4);
            if (y + input.Height > wa.Bottom) y = form.Bottom - input.Height - S(2);
            input.Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - input.Width, x)), y);
            input.Box.Text = mode == "name" ? P.Name : "";
            input.Show();
            input.Activate();
            input.Box.Focus();
            input.Box.SelectAll();
            if (mode == "chat" && pet.Bubble == null) Say(GetStage() == "baby" ? F("{S}?") : "Я слушаю. Напиши и нажми Enter.", 5);
        }

        // ---------------------------------------------------------------- мышь и меню

        void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var br = pet.BubbleRect;
            if (br.Width > 0 && br.Contains(e.Location)) { pet.HideBubble(); return; }
            if (pet.Mode == 4) { pet.StopMode(); EndGame(true); return; }
            if (e.Clicks >= 2) { ShowInput("chat"); return; }
            if (pet.Mode >= 2) { DoPat(); return; }
            if (pet.Mode == 1) pet.StopMode();
            if (!mod.Locked)
            {
                var before = form.Location;
                WNative.DragWindow(form);
                if (form.Location != before) { SaveState(); return; }
            }
            DoPat();
        }

        ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 9.5f) };
            menu.Closed += (s, e) => menu.BeginInvoke(new Action(menu.Dispose));
            menu.Items.Add(new ToolStripMenuItem("Погладить", null, (s, e) => DoPat()));
            var feed = new ToolStripMenuItem("Покормить");
            for (int i = 0; i < Foods.Length; i++)
            {
                int k = i;
                feed.DropDownItems.Add(new ToolStripMenuItem(Foods[i].Name + (i == Fav ? " (любимое)" : ""), null, (s, e) => DoFeed(k)));
            }
            menu.Items.Add(feed);
            var play = new ToolStripMenuItem("Поиграть");
            play.DropDownItems.Add(new ToolStripMenuItem("Мячик", null, (s, e) => StartGame(2)));
            play.DropDownItems.Add(new ToolStripMenuItem("Догонялки: бежит за курсором", null, (s, e) => StartGame(3)));
            play.DropDownItems.Add(new ToolStripMenuItem("Поймай меня", null, (s, e) => StartGame(4)));
            play.DropDownItems.Add(new ToolStripSeparator());
            play.DropDownItems.Add(new ToolStripMenuItem("Хватит играть", null, (s, e) => { if (pet.Mode >= 2) { pet.StopMode(); EndGame(false); } }));
            menu.Items.Add(play);
            menu.Items.Add(new ToolStripMenuItem("Поговорить…", null, (s, e) => ShowInput("chat")));
            menu.Items.Add(new ToolStripMenuItem(F("Как {он|она}?"), null, (s, e) => HowIsIt()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Дать имя…", null, (s, e) => ShowInput("name")));
            var chars = new ToolStripMenuItem("Персонаж");
            foreach (var c in PetChar.Available())
            {
                var cc = c;
                chars.DropDownItems.Add(new ToolStripMenuItem(c.Title, null, (s, e) => mod.SetChar(cc)) { Checked = c == ch, ToolTipText = Cap(c.About) });
            }
            menu.Items.Add(chars);
            menu.Items.Add(new ToolStripMenuItem("Поверх всех окон", null, (s, e) => mod.TopMost = !mod.TopMost) { Checked = mod.TopMost });
            menu.Items.Add(new ToolStripMenuItem("Закрепить на месте", null, (s, e) => mod.Locked = !mod.Locked) { Checked = mod.Locked });
            menu.Items.Add(new ToolStripMenuItem("Гулять по экрану", null, (s, e) => mod.Walk = !mod.Walk) { Checked = mod.Walk });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Центр управления " + App.Name, null, (s, e) => App.Core.ShowCenter(mod)));
            menu.Items.Add(new ToolStripMenuItem("Убрать «" + mod.Name + "»", null, (s, e) => App.Core.SetEnabled(mod, false)));
            return menu;
        }

        void HowIsIt()
        {
            Touch();
            string st = GetStage();
            string food = P.Food < 25 ? F("очень {голоден|голодна}") : P.Food < 55 ? "не против перекусить" : F("{сыт|сыта}");
            string mood = P.Mood < 25 ? "скучает" : P.Mood < 60 ? F("{спокоен|спокойна}") : F("{счастлив|счастлива}");
            string next = st == "baby" ? "До подросткового возраста: " + (int)(100 * P.Xp / TeenXp) + "%." :
                          st == "teen" ? "До взрослого возраста: " + (int)(100 * (P.Xp - TeenXp) / (AdultXp - TeenXp)) + "%." :
                          F("{Он уже взрослый|Она уже взрослая}") + (TailCount("adult") > 1 ? F(", у {него|неё} ") + Tails(TailCount("adult")) : "") + ".";
            Say(P.Name + ", " + StageName(st) + ", " + DaysTogether() + ".\n" + F("Сейчас {он|она} ") + food + " и " + mood + ".\n" + next, 9);
        }

        // ---------------------------------------------------------------- жизнь: раз в секунду

        void LifeTick()
        {
            var now = DateTime.Now;
            double min = 1 / 60.0;
            bool night = now.Hour >= 1 && now.Hour < 7;
            double k = pet.Sleeping ? 0.4 : 1.0;
            P.Food = Clamp(P.Food - 0.11 * min * k);
            P.Mood = Clamp(P.Mood - (0.09 + (P.Food < 30 ? 0.08 : 0)) * min * k);
            // растёт, пока вы вместе и о нём заботятся
            if (P.Food > 30 && P.Mood > 30) AddXp(0.1 * min);

            double idle = PetIdle.Seconds();
            if (idle < 300) activeMin += min; else activeMin = 0;

            // ночью засыпает, если его не трогают
            if (!pet.Sleeping && night && (now - lastTouch).TotalMinutes > 10 && idle > 120) pet.Sleeping = true;
            if (pet.Sleeping && !night && (now - lastTouch).TotalMinutes > 1 && idle < 5 && (now - lastTouch).TotalHours > 0.5) pet.Sleeping = false;

            // напоминания и таймеры
            var due = P.Reminders.Where(r => r.At <= now).ToList();
            if (due.Count > 0)
            {
                P.Reminders = P.Reminders.Where(r => r.At > now).ToList();
                foreach (var r in due)
                {
                    pet.Sleeping = false;
                    pet.Pat();
                    Say("Напоминаю: " + r.Text, 60);
                    System.Media.SystemSounds.Asterisk.Play();
                    App.Core.Notify(P.Name, "Напоминаю: " + r.Text);
                    form.TopMost = true;
                    restoreTop = now.AddSeconds(20);
                }
                SaveState();
            }
            if (restoreTop != null && now > restoreTop.Value) { restoreTop = null; form.TopMost = mod.TopMost; }

            if (pet.Bubble == null && !pet.Sleeping && !hidden)
            {
                string st = GetStage();
                if (st == "adult" && mod.Breaks && activeMin >= 90)
                {
                    activeMin = 0;
                    Say(Pick("Ты уже полтора часа за компьютером. Встань, потянись и попей воды!", "Пора сделать перерыв: разомни спину и дай глазам отдохнуть."), 15);
                }
                else if (now > nextNeed && (P.Food < 30 || P.Mood < 30))
                {
                    nextNeed = now.AddMinutes(15 + rnd.Next(10));
                    SayMood(P.Food < 30 ? "hungry" : "bored");
                }
                else if (now > nextIdle && idle < 60)
                {
                    nextIdle = now.AddMinutes(14 + rnd.Next(18));
                    SayMood("idle");
                }
            }
            // сам гуляет по экрану, когда ему хорошо
            if (mod.Walk && !mod.Locked && !hidden && pet.Mode == 0 && !pet.Sleeping && pet.Bubble == null && P.Mood > 35 && now > nextWalk)
            {
                nextWalk = now.AddSeconds(50 + rnd.Next(110));
                var a = pet.Area();
                float x = pet.GroundX();
                float nx = Math.Max(a.Left + S(80), Math.Min(a.Right - S(80), x + (rnd.Next(2) * 2 - 1) * S(120 + rnd.Next(380))));
                pet.WalkTo(nx, rnd.NextDouble() < 0.3 ? 300 : 140);
            }
            UpdateInfo();
            if (now.Second == 0) SaveState();
        }
    }

    static class PetIdle
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        // Сколько секунд пользователь не трогал мышь и клавиатуру.
        public static double Seconds()
        {
            var i = new LASTINPUTINFO();
            i.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(i);
            if (!GetLastInputInfo(ref i)) return 0;
            return unchecked((uint)Environment.TickCount - i.dwTime) / 1000.0;
        }
    }

    // Маленький калькулятор для «посчитай 25*4+7»: + - * / % и скобки.
    static class PetCalc
    {
        public static bool TryEval(string s, out double v)
        {
            v = 0;
            try
            {
                int i = 0;
                v = Sum(s.Replace(" ", ""), ref i);
                return i == s.Replace(" ", "").Length && !double.IsNaN(v) && !double.IsInfinity(v);
            }
            catch { return false; }
        }

        static double Sum(string s, ref int i)
        {
            double v = Prod(s, ref i);
            while (i < s.Length && (s[i] == '+' || s[i] == '-'))
            {
                char op = s[i++];
                double r = Prod(s, ref i);
                v = op == '+' ? v + r : v - r;
            }
            return v;
        }

        static double Prod(string s, ref int i)
        {
            double v = Unary(s, ref i);
            while (i < s.Length && (s[i] == '*' || s[i] == '/' || s[i] == '%'))
            {
                char op = s[i++];
                double r = Unary(s, ref i);
                v = op == '*' ? v * r : op == '/' ? v / r : v % r;
            }
            return v;
        }

        static double Unary(string s, ref int i)
        {
            if (i < s.Length && s[i] == '-') { i++; return -Unary(s, ref i); }
            if (i < s.Length && s[i] == '+') { i++; return Unary(s, ref i); }
            if (i < s.Length && s[i] == '(')
            {
                i++;
                double v = Sum(s, ref i);
                if (i >= s.Length || s[i] != ')') throw new FormatException();
                i++;
                return v;
            }
            int st = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }
    }

    // Строка ввода под питомцем: разговор или новое имя.
    class PetInput : Form
    {
        public readonly TextBox Box = new TextBox();
        public string Mode = "chat";
        public event Action<string> Submit;

        public PetInput()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            BackColor = Color.FromArgb(30, 33, 44);
            Size = new Size(Ui.S(270), Ui.S(38));
            Padding = new Padding(Ui.S(10), Ui.S(9), Ui.S(10), Ui.S(6));
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = BackColor;
            Box.ForeColor = Color.FromArgb(240, 240, 250);
            Box.Font = new Font("Segoe UI", 10.5f);
            Box.Dock = DockStyle.Fill;
            Controls.Add(Box);
            Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { Hide(); e.SuppressKeyPress = true; return; }
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                string text = Box.Text.Trim();
                Box.Text = "";
                if (Submit != null) Submit(text);
            };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WNative.RoundDark(Handle);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Hide();
        }
    }
}
