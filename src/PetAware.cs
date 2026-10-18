using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Glasswing
{
    // ---------------------------------------------------------------- питомец замечает, что происходит

    // Погода, дела, музыка, долгая игра, горячий компьютер. Говорит редко и по-своему.
    partial class PetLife
    {
        static readonly Dictionary<string, string[]> AwareSay = new Dictionary<string, string[]>
        {
            // {when} «около 15:00», {t} температура, {n} «2 дела», {list} первые дела,
            // {song} название, {by} « — исполнитель» или пусто, {h} «два часа», {app} « в «Игре»» или пусто
            ["rain"] = new[] { "Скоро дождь, {when}. Возьми зонт!", "Кажется, {when} пойдёт дождь." },
            ["rain.kitsune"] = new[] { "Чую носом: {when} будет дождь! Хвосты не промочи. То есть ты себя не промочи.", "Дождь {when}! Огонь его не любит, так что зонт за тобой." },
            ["rain.shukaku"] = new[] { "Хм. Дождь {when}. Песок мокнет, я злюсь. Зонт возьми.", "Дождь {when}. Не говори потом, что я не {предупреждал|предупреждала}." },
            ["rain.matatabi"] = new[] { "Мур… {when} обещают дождик. Возьми зонтик, хорошо?", "Скоро дождь, {when}. Я посижу у окна, а ты не промокни." },
            ["rain.gyuki"] = new[] { "Дождь {when} придёт, зонт с собой бери в поход.", "{when} капли упадут, пусть зонт будет тут." },
            ["snow"] = new[] { "Скоро снег, {when}. Оденься теплее!" },
            ["snow.kitsune"] = new[] { "Снег {when}! Буду ловить снежинки ртом. А ты оденься теплее." },
            ["snow.shukaku"] = new[] { "Снег {when}. Не песок, но тоже ничего. Шапку надень." },
            ["snow.matatabi"] = new[] { "{when} пойдёт снег… Укутайся потеплее, мур." },
            ["snow.gyuki"] = new[] { "Снег {when} кружит, шарф тебя сторожит." },
            ["storm"] = new[] { "{when} обещают грозу. Лучше переждать дома." },
            ["storm.shukaku"] = new[] { "Гроза {when}. Громко будет. Сиди дома." },
            ["storm.gyuki"] = new[] { "Гроза {when} гремит, дом тебя хранит." },
            ["hot"] = new[] { "На улице {t}! Пей больше воды.", "Жара, {t}. Береги голову от солнца." },
            ["hot.kitsune"] = new[] { "{t} на улице! Даже мне, огненному лису, жарковато. Попей воды!" },
            ["hot.shukaku"] = new[] { "{t}. Как в пустыне. Мне нравится, а тебе вода нужна. Пей." },
            ["hot.matatabi"] = new[] { "Ох, {t}… Я лягу в тенёк. А ты попей водички, ладно?" },
            ["hot.gyuki"] = new[] { "{t}, жара стоит, вода от зноя защитит." },
            ["cold"] = new[] { "На улице {t}! Одевайся теплее.", "Мороз, {t}. Не забудь перчатки." },
            ["cold.kitsune"] = new[] { "{t}! Хочешь, погрею хвостами? Но шарф всё равно надень." },
            ["cold.shukaku"] = new[] { "{t}. Холодно. Не люблю. Оденься нормально." },
            ["cold.matatabi"] = new[] { "Брр, {t}… Свернусь клубочком. А ты одевайся теплее." },
            ["cold.gyuki"] = new[] { "{t} мороз трещит, тёплый шарф тебя хранит." },
            ["todo"] = new[] { "Осталось {n}: {list}.", "В списке ещё {n}: {list}." },
            ["todo.kitsune"] = new[] { "Эй! Осталось {n}: {list}. Давай быстрее, а потом поиграем!", "Хи-хи, я подглядел: ещё {n}. {list}." },
            ["todo.shukaku"] = new[] { "Ещё {n}: {list}. Сами себя не сделают.", "{n} висит. {list}. Займись." },
            ["todo.matatabi"] = new[] { "Осталось {n}: {list}. Не торопись, ты справишься.", "Мур, ещё {n}: {list}. Я рядом." },
            ["todo.gyuki"] = new[] { "Осталось {n}, дело — не беда: {list}.", "{n} ждут тебя, сделаешь любя: {list}." },
            ["todo.done"] = new[] { "Все дела сделаны! Ты молодец." },
            ["todo.done.kitsune"] = new[] { "Всё сделано! Хвосты пляшут от радости!" },
            ["todo.done.shukaku"] = new[] { "Всё сделал. Неплохо. Очень даже." },
            ["todo.done.matatabi"] = new[] { "Все дела готовы… Ты умничка. Отдохни." },
            ["todo.done.gyuki"] = new[] { "Дела сделаны в срок, отдохни чуток." },
            ["song"] = new[] { "О, «{song}»{by}. Хороший выбор!", "«{song}»{by}… Мне нравится." },
            ["song.kitsune"] = new[] { "«{song}»{by}! Под это хвосты сами пляшут!", "О-о, «{song}»! Сделай погромче!" },
            ["song.shukaku"] = new[] { "«{song}»{by}. Сойдёт.", "Хм. «{song}». Ладно, пусть играет." },
            ["song.matatabi"] = new[] { "Мур… «{song}»{by}. Красиво.", "«{song}»… Под такое хорошо дремать." },
            ["song.gyuki"] = new[] { "«{song}» звучит, сердце в такт стучит.", "«{song}»{by}. Йо, ритм в щупальцах." },
            ["game"] = new[] { "Ты уже {h}{app}. Может, перерыв?", "{h}{app} без перерыва! Разомнись немного." },
            ["game.kitsune"] = new[] { "Ты уже {h}{app}! Я тоже хочу играть. Давай пять минут перерыва?" },
            ["game.shukaku"] = new[] { "{h}{app}. Глаза побереги. Перерыв." },
            ["game.matatabi"] = new[] { "Уже {h}{app}… Глазкам нужен отдых. Потянись, ладно?" },
            ["game.gyuki"] = new[] { "{h}{app} подряд — пять минут отдыха, брат." },
            ["cpu"] = new[] { "Компьютер пыхтит: процессор занят на {cpu}." },
            ["cpu.kitsune"] = new[] { "Процессор на {cpu}! Он горячее моих хвостов." },
            ["cpu.shukaku"] = new[] { "Процессор на {cpu}. Что-то его грузит. Глянь." },
            ["cpu.matatabi"] = new[] { "Процессор совсем устал, {cpu}… Может, что-то закрыть?" },
            ["cpu.gyuki"] = new[] { "Процессор на {cpu} кипит, пусть немного поспит." },
            ["gpu"] = new[] { "Видеокарта нагрелась до {temp}. Пусть остынет." },
            ["gpu.kitsune"] = new[] { "Видеокарта {temp}! Это уже моя территория, огненная." },
            ["gpu.shukaku"] = new[] { "Видеокарта {temp}. Горячо. Дай ей передохнуть." },
            ["gpu.matatabi"] = new[] { "Видеокарта {temp}… Ей жарко, как мне на солнышке." },
            ["gpu.gyuki"] = new[] { "Видеокарта {temp}, жар — передохнуть ей дар." },
            ["notodo"] = new[] { "Дел нет. Свободен как ветер!" },
            ["notodo.kitsune"] = new[] { "Список пуст! Значит, время играть!" },
            ["notodo.shukaku"] = new[] { "Дел нет. Отдыхай." },
            ["notodo.matatabi"] = new[] { "Дел нет… Можно спокойно помурлыкать." },
            ["notodo.gyuki"] = new[] { "Дел нет — свободен, как рассвет." },
        };

        readonly System.Windows.Forms.Timer aware = new System.Windows.Forms.Timer { Interval = 2 * 60 * 1000 };
        readonly Dictionary<string, DateTime> awareNextKind = new Dictionary<string, DateTime>();
        DateTime awareNext;
        string songNow;
        bool songFresh, todoDone;
        int todoOpen = -1, cpuHot;
        uint fullPid;
        DateTime fullSince;
        string fullTitle = "";

        void StartAware()
        {
            awareNext = DateTime.Now.AddMinutes(5);   // сначала поздоровается
            aware.Tick += (s, e) =>
            {
                try { AwareTick(); }
                catch (Exception ex) { Log.Write("питомец замечает: " + ex.Message); }
            };
            aware.Start();
        }

        void StopAware()
        {
            aware.Stop();
            aware.Dispose();
            PetVoice.Stop();
        }

        static T Mod<T>() where T : Module
        {
            return App.Core == null ? null : App.Core.Modules.OfType<T>().FirstOrDefault();
        }

        // Строка персонажа для вида: своя, если есть, иначе общая.
        string AwarePick(string kind)
        {
            string[] l;
            if (!AwareSay.TryGetValue(kind + "." + ch.Id, out l) && !AwareSay.TryGetValue(kind, out l)) return null;
            return Pick(l);
        }

        static string Quote(string s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length > max ? s.Substring(0, max - 1).TrimEnd() + "…" : s;
        }

        // ---------------------------------------------------------------- раз в две минуты

        void AwareTick()
        {
            var now = DateTime.Now;
            // наблюдаем всегда, чтобы заметить смену трека и долгую игру
            bool full = ObserveFullscreen(now);
            ObserveSong();
            ObserveTodo();
            var sens = Mod<SysMonitorModule>() != null ? Mod<SysMonitorModule>().Sensors : null;
            cpuHot = sens.HasValue && sens.Value.Cpu >= 90 ? cpuHot + 1 : 0;

            var c = new List<KeyValuePair<string, string>>();
            try
            {
                if (!mod.Aware || hidden || pet.Sleeping || pet.Bubble != null || pet.Mode >= 2 || now < awareNext) return;
                string st = GetStage();
                Action<string, string> add = (k, text) =>
                {
                    DateTime t;
                    if (text != null && (!awareNextKind.TryGetValue(k, out t) || now >= t)) c.Add(new KeyValuePair<string, string>(k, text));
                };

                if (full)
                {
                    // за полноэкранной игрой отвлекаем только просьбой отдохнуть
                    double h = (now - fullSince).TotalHours;
                    if (h >= 2 && st != "baby")
                    {
                        int n = (int)Math.Floor(h);
                        string hh = (n < Numbers.Length ? Numbers[n] : n.ToString()) + " " + Plural(n, "час", "часа", "часов");
                        string app = fullTitle.Length > 0 ? " в «" + fullTitle + "»" : "";
                        add("game", Fill(AwarePick("game"), "{h}", hh, "{app}", app));
                    }
                }
                else if (PetIdle.Seconds() < 120)
                {
                    string kind, when, temp;
                    if (WeatherNews(out kind, out when, out temp))
                        add(kind, st == "baby" ? F("{S}?.. ({он|она} смотрит в окно: кажется, погода меняется)") : Fill(AwarePick(kind), "{when}", when, "{t}", temp));
                    if (songFresh && rnd.NextDouble() < 0.5)
                    {
                        var np = Mod<NowPlayingModule>().Current;
                        if (np != null) add("song", st == "baby" ? F("{S}-{s}! ♪ ({он|она} пританцовывает)") :
                            Fill(AwarePick("song"), "{song}", Quote(np.Title, 40), "{by}", np.Artist.Trim().Length > 0 ? " — " + Quote(np.Artist, 30) : ""));
                    }
                    if (st != "baby")
                    {
                        if (todoDone) add("todo.done", AwarePick("todo.done"));
                        else if (todoOpen > 0 && rnd.NextDouble() < 0.4) add("todo", TodoLine("todo"));
                        if (cpuHot >= 2) add("cpu", Fill(AwarePick("cpu"), "{cpu}", (int)Math.Round(sens.Value.Cpu) + " %"));
                        if (sens.HasValue && sens.Value.GpuTemp >= 85) add("gpu", Fill(AwarePick("gpu"), "{temp}", (int)Math.Round(sens.Value.GpuTemp) + "°"));
                    }
                }
                if (c.Count == 0) return;
                var pick = c[rnd.Next(c.Count)];
                if (st == "baby" && rnd.NextDouble() < 0.5) { awareNext = now.AddMinutes(12); return; }   // малыш замечает пореже
                Say(pick.Value, pick.Key == "game" ? 20 : 0);
                awareNext = now.AddMinutes(12 + rnd.Next(6));
                awareNextKind[pick.Key] = now.AddHours(KindHours(pick.Key));
            }
            finally
            {
                songFresh = false;
                todoDone = false;
            }
        }

        static double KindHours(string k)
        {
            switch (k)
            {
                case "rain": case "snow": case "storm": return 3;
                case "hot": case "cold": return 5;
                case "todo": return 2;
                default: return 1;
            }
        }

        // Подставляет данные после F(): название песни может содержать фигурные скобки.
        static string Fill(string s, params string[] kv)
        {
            if (s == null) return null;
            for (int i = 0; i + 1 < kv.Length; i += 2) s = s.Replace(kv[i], kv[i + 1]);
            return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;
        }

        void ObserveSong()
        {
            var np = Mod<NowPlayingModule>();
            var i = np == null ? null : np.Current;
            string key = i != null && i.Has && i.Playing && i.Title.Trim().Length > 0 ? i.Title + "|" + i.Artist : null;
            if (key != null && key != songNow) songFresh = true;
            if (key != null) songNow = key;
        }

        void ObserveTodo()
        {
            var t = Mod<TodoModule>();
            if (t == null || !t.Running) { todoOpen = -1; return; }
            int n = TodoModule.OpenItems().Count;
            if (todoOpen > 0 && n == 0) todoDone = true;
            todoOpen = n;
        }

        string TodoLine(string kind)
        {
            var items = TodoModule.OpenItems();
            string list = string.Join(", ", items.Take(3).Select(x => Quote(x, 30))) + (items.Count > 3 ? " и ещё " + (items.Count - 3) : "");
            return Fill(AwarePick(kind), "{n}", TodoModule.Count(items.Count), "{list}", list);
        }

        // ---------------------------------------------------------------- погода

        static string Deg(double c, bool fahr)
        {
            if (double.IsNaN(c)) return "—";
            int r = (int)Math.Round(fahr ? c * 9 / 5 + 32 : c);
            return (r < 0 ? "−" + (-r) : (r > 0 && !fahr ? "+" : "") + r) + "°";
        }

        static WeatherData FreshWeather(WeatherModule wm)
        {
            var d = wm == null ? null : wm.Data;
            return d != null && (DateTime.UtcNow - d.Fetched).TotalHours < 3 ? d : null;
        }

        // Что стоит сказать о погоде: осадки в ближайшие три часа, жара или мороз.
        bool WeatherNews(out string kind, out string when, out string temp)
        {
            kind = when = temp = null;
            var wm = Mod<WeatherModule>();
            var d = FreshWeather(wm);
            if (d == null) return false;
            temp = Deg(d.Temp, wm.Fahrenheit);
            var now = d.LocalNow();
            if (!WeatherIcons.Precip(d.Code))
            {
                for (int i = 0; i < d.HourTime.Length && i < d.HourCode.Length && i < d.HourProb.Length; i++)
                {
                    var t = d.HourTime[i];
                    if (t <= now || t > now.AddHours(3)) continue;
                    int code = d.HourCode[i];
                    if (!WeatherIcons.Precip(code) || (d.HourProb[i] >= 0 && d.HourProb[i] < 50)) continue;
                    kind = WeatherIcons.Storm(code) ? "storm" : WeatherIcons.Snow(code) ? "snow" : "rain";
                    when = "около " + t.ToString("HH:mm");
                    return true;
                }
            }
            if (double.IsNaN(d.Temp)) return false;
            if (d.Temp >= 30) { kind = "hot"; return true; }
            if (d.Temp <= -15) { kind = "cold"; return true; }
            return false;
        }

        // ---------------------------------------------------------------- вопросы в чате

        // «какая погода», «что по делам», «что играет». Пусто, если вопрос не про это.
        string AwareReply(string l)
        {
            if (GetStage() == "baby") return null;
            if (Has(l, @"что (сейчас |там |у нас )?(играет|звучит|за песн|за трек|за музык)|как(ая|ой) (это |сейчас )?(песня|трек)|что слушаем"))
            {
                var np = Mod<NowPlayingModule>();
                if (np == null || !np.Running) return "Включи виджет «Сейчас играет» в центре управления, и я буду слышать музыку.";
                var i = np.Current;
                if (i == null || !i.Has || i.Title.Trim().Length == 0) return "Сейчас тихо, ничего не играет.";
                string s = "Играет «" + Quote(i.Title, 50) + "»" + (i.Artist.Trim().Length > 0 ? " — " + Quote(i.Artist, 40) : "") + (i.Playing ? "." : ", но на паузе.");
                string extra = i.Playing ? AwarePick("song") : null;
                return extra != null && rnd.NextDouble() < 0.5 ? s + "\n" + Fill(extra, "{song}", Quote(i.Title, 40), "{by}", "") : s;
            }
            if (Has(l, @"(что|какие|сколько|мои|список|покажи)\s+(\S+\s+){0,2}(дел\b|дела\b|делам|задач)"))
            {
                var t = Mod<TodoModule>();
                if (t == null || !t.Enabled) return "Включи «Список дел» в центре управления, и я буду за ним следить.";
                var items = TodoModule.OpenItems();
                if (items.Count == 0) return AwarePick("notodo");
                return "Осталось " + TodoModule.Count(items.Count) + ":\n" +
                       string.Join("\n", items.Take(6).Select(x => "• " + Quote(x, 40))) + (items.Count > 6 ? "\n…и ещё " + (items.Count - 6) : "");
            }
            if (Has(l, @"погод|дожд|зонт|снег|на улице"))
            {
                var wm = Mod<WeatherModule>();
                var d = FreshWeather(wm);
                if (d == null) return null;   // старый ответ про «не выглядываю в интернет»
                string s = "В городе " + wm.City + " сейчас " + Deg(d.Temp, wm.Fahrenheit) + ", " + WeatherIcons.Describe(d.Code) +
                           ", ощущается как " + Deg(d.Feels, wm.Fahrenheit) + ".";
                string kind, when, temp;
                if (WeatherNews(out kind, out when, out temp)) s += "\n" + Fill(AwarePick(kind), "{when}", when, "{t}", temp);
                return s;
            }
            return null;
        }

        // ---------------------------------------------------------------- долгая игра

        // Полноэкранное окно на переднем плане: игра или фильм. Считаем, сколько оно не меняется.
        bool ObserveFullscreen(DateTime now)
        {
            uint pid;
            string title;
            if (!AwareNative.Fullscreen(out pid, out title)) { fullPid = 0; return false; }
            if (pid != fullPid) { fullPid = pid; fullSince = now; }
            fullTitle = title.Length > 1 && title.Length <= 32 && !title.Contains(" - ") && !title.Contains(" — ") ? title : "";
            return true;
        }
    }

    static class AwareNative
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);

        static readonly uint Self = (uint)Process.GetCurrentProcess().Id;

        public static bool Fullscreen(out uint pid, out string title)
        {
            pid = 0; title = "";
            try
            {
                var h = GetForegroundWindow();
                if (h == IntPtr.Zero) return false;
                var sb = new StringBuilder(256);
                GetClassName(h, sb, sb.Capacity);
                string cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;   // рабочий стол и панель задач
                RECT r;
                if (!GetWindowRect(h, out r)) return false;
                var scr = Screen.FromHandle(h).Bounds;
                if (r.Left > scr.Left || r.Top > scr.Top || r.Right < scr.Right || r.Bottom < scr.Bottom) return false;
                GetWindowThreadProcessId(h, out pid);
                if (pid == 0 || pid == Self) return false;
                sb.Clear();
                GetWindowText(h, sb, sb.Capacity);
                title = sb.ToString().Trim();
                return true;
            }
            catch { return false; }
        }
    }
}
