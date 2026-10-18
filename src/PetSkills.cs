using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Glasswing
{
    // ---------------------------------------------------------------- питомец-помощник

    // Команды в разговоре (подросток и взрослый): дела, помидор, музыка, игры из хаба, перевод, фокус.
    partial class PetLife
    {
        const string SkillsHelp = "\n• «добавь дело купить хлеб»\n• «помидор», «сколько осталось», «отмени таймер»\n• «пауза», «играй», «дальше», «назад»\n• «запусти …», «во что поиграть?»\n• «переведи …»\n• «покажи фокус»";

        static readonly Dictionary<string, string[]> SkillSay = new Dictionary<string, string[]>
        {
            // {x} подставляется после F(): дело, игра, минуты
            ["todo"] = new[] { "{Записал|Записала} в дела: {x}.", "Добавлено: {x}." },
            ["todo.kitsune"] = new[] { "Готово! «{x}» в списке. Не забудь, а то хвостом напомню." },
            ["todo.shukaku"] = new[] { "Записал: {x}. Теперь делай." },
            ["todo.matatabi"] = new[] { "Мур, записала: {x}. Ты справишься." },
            ["todo.gyuki"] = new[] { "«{x}» — в список, йо, сделаешь легко." },
            ["pomodoro"] = new[] { "Помидор пошёл: 25 минут работы. Я скажу, когда отдыхать." },
            ["pomodoro.kitsune"] = new[] { "Помидор! 25 минут работаем, потом играем. Я засёк!" },
            ["pomodoro.shukaku"] = new[] { "25 минут. Работай. Я скажу, когда хватит." },
            ["pomodoro.matatabi"] = new[] { "25 минут тишины и работы… Я посторожу время, мур." },
            ["pomodoro.gyuki"] = new[] { "Двадцать пять минут труда, а потом отдых, как всегда." },
            ["pause"] = new[] { "Пауза.", "Тсс, остановил{|а}." },
            ["pause.shukaku"] = new[] { "Тихо. Наконец-то." },
            ["pause.matatabi"] = new[] { "Тишина… мур." },
            ["play"] = new[] { "Играем!", "Включаю." },
            ["play.kitsune"] = new[] { "Музыка! Хвосты, в пляс!" },
            ["play.gyuki"] = new[] { "Бит пошёл, йо, хорошо." },
            ["next"] = new[] { "Следующий трек!", "Дальше." },
            ["next.shukaku"] = new[] { "Этот надоел. Дальше." },
            ["prev"] = new[] { "Назад, ещё раз.", "Возвращаю." },
            ["launch"] = new[] { "Запускаю «{x}». Удачной игры!", "«{x}» — поехали!" },
            ["launch.kitsune"] = new[] { "«{x}»! Ура, играем! Я буду болеть за тебя." },
            ["launch.shukaku"] = new[] { "«{x}». Запускаю. Не проиграй." },
            ["launch.matatabi"] = new[] { "Запускаю «{x}»… Только не засиживайся, ладно?" },
            ["launch.gyuki"] = new[] { "«{x}» в бой, я рядом с тобой." },
            ["suggest"] = new[] { "Может, «{x}»? Скажи «запусти {x}».", "Давно не было «{x}». Запустить?" },
            ["suggest.shukaku"] = new[] { "«{x}». Скажи «запусти», и всё." },
            ["suggest.gyuki"] = new[] { "Сыграй в «{x}», йо, не пожалеешь потом." },
            ["trick.kitsune"] = new[] { "Лисьи огоньки! Ну как?", "Та-дам! Кицунэ-би!" },
            ["trick.shukaku"] = new[] { "Песчаный вихрь. Не чихни.", "Смотри. Песок слушается меня." },
            ["trick.matatabi"] = new[] { "Синее пламя… Не обожжёт, не бойся.", "Мур! Кошачьи огоньки." },
            ["trick.gyuki"] = new[] { "Чернильный взрыв, вот это порыв!", "Ink-бах! Йо!" },
            ["trick"] = new[] { "Та-дам!" },
        };

        string SkillPick(string kind, string x = "")
        {
            string[] l;
            if (!SkillSay.TryGetValue(kind + "." + ch.Id, out l) && !SkillSay.TryGetValue(kind, out l)) return null;
            return Pick(l).Replace("{x}", x);
        }

        // t: как написали, l: строчными и без «ё». Пусто, если это не команда помощника.
        string SkillReply(string t, string l)
        {
            if (GetStage() == "baby") return null;
            string c = l.Trim(' ', '.', '!', '?', ',');
            Match m;

            // фокус
            if (Has(c, @"(покажи|сделай|давай) (\S+ )?фокус|^фокус$"))
            {
                pet.Trick();
                P.Mood = Clamp(P.Mood + 4);
                return SkillPick("trick");
            }

            // дела
            const string addTodo = @"^(?:добавь|добавить|внеси|запиши)\s+(?:в\s+)?(?:список\s+)?(?:дела|дело|дел|задачу|задачи|список)\b[\s:,—-]*(.+)$";
            if ((m = M(t.Trim(), addTodo)).Success || ((m = M(t.Trim(), @"^напомни\s+(?:мне\s+)?(.+)$")).Success && !Has(l, @"напомни\s*(мне\s*)?(через|в\s+\d|на\s+\d)")))
            {
                string what = m.Groups[1].Value.Trim(' ', '.', ':');
                if (what.Length == 0) return "Что записать? Например: «добавь дело купить хлеб».";
                TodoModule.AddItem(what);
                var tm = Mod<TodoModule>();
                return SkillPick("todo", Quote(what, 50)) + (tm == null || !tm.Enabled ? "\nВключи «Список дел» в центре управления, чтобы видеть его на столе." : "");
            }

            // помидор и таймеры (сам таймер «таймер 10 минут» уже умеет PetLife)
            if (Has(c, @"^(помидор\w*|pomodoro|поставь помидор|давай помидор|включи помидор)"))
            {
                AddReminder(DateTime.Now.AddMinutes(25), F("Помидор готов! 25 минут позади, отдохни пять минут. {S}!"));
                return SkillPick("pomodoro");
            }
            if (Has(c, @"сколько (еще )?(осталось|ждать)|когда таймер"))
            {
                var tmr = Timers().OrderBy(r => r.At).FirstOrDefault();
                if (tmr == null) return "Таймер не запущен. Скажи «таймер 10 минут» или «помидор».";
                var left = tmr.At - DateTime.Now;
                if (left.TotalSeconds < 0) left = TimeSpan.Zero;
                int mm = (int)left.TotalMinutes, ss = left.Seconds;
                return "Осталось " + (mm > 0 ? mm + " мин " : "") + ss + " с.";
            }
            if (Has(c, @"(отмени|останови|убери|сбрось|выключи) (\S+ )?(таймер|помидор)"))
            {
                int n = Timers().Count;
                if (n == 0) return "Таймеров нет, отменять нечего.";
                P.Reminders = P.Reminders.Where(r => !IsTimer(r)).ToList();
                SaveState();
                return F("{Отменил|Отменила}.");
            }

            // музыка
            string media = Has(c, @"^(пауза|на паузу|поставь на паузу|стоп|останови музыку|выключи музыку|тихо)$") ? "pause" :
                           Has(c, @"^(играй|продолжи|продолжай|включи музыку|play|плей)$") ? "play" :
                           Has(c, @"^(дальше|следующ\w*|некст|next|переключи)( трек| песн\w*)?$") ? "next" :
                           Has(c, @"^(назад|предыдущ\w*|верни)( трек| песн\w*)?$") ? "prev" : null;
            if (media != null) return Music(media);

            // игры из хаба
            if (Has(c, @"во что (бы )?(мне )?(по)?играть|что (бы )?поиграть|посоветуй игру|какую игру"))
            {
                var hub = Hub();
                if (hub == null || !hub.Running) return "Включи «Игровой хаб» в центре управления, и я буду знать твои игры.";
                var list = hub.ShelfGames.Take(8).ToList();
                if (list.Count == 0) return "На полке пока пусто. Добавь игры в хаб.";
                var g = list[rnd.Next(list.Count)];
                return SkillPick("suggest", g.Name);
            }
            if ((m = M(c, @"^(?:запусти|запускай|включи игру|сыграем в|поиграем в|давай (?:сыграем|поиграем) в)\s+(.+)$")).Success)
            {
                string what = m.Groups[1].Value.Trim();
                if (Has(what, "^(мяч|догонял|догони|поймай|прятк)")) return null;   // это свои игры питомца
                var hub = Hub();
                var g = hub != null && hub.Running ? FindGame(hub.ShelfGames, what) : null;
                if (g != null) { hub.Launch(g); return SkillPick("launch", g.Name); }
                if (GetStage() == "adult") { try { if (OpenThing(what)) return "Открываю " + what + "."; } catch { } }
                return F("Не {нашёл|нашла} «") + what + "» на полке игр.";
            }

            // перевод
            if ((m = M(t.Trim(), @"^переведи(?:\s+на\s+([а-яё]+)(?:\s+язык)?)?[\s:,—-]+(.+)$")).Success)
            {
                string force = "", text = m.Groups[2].Value.Trim(' ', '«', '»', '"');
                if (m.Groups[1].Success)
                {
                    string stem = m.Groups[1].Value.ToLower();
                    stem = stem.Substring(0, Math.Min(4, stem.Length));
                    var lang = TranslatorModule.Languages.FirstOrDefault(x => x[1].ToLower().StartsWith(stem));
                    if (lang != null) force = lang[0];
                }
                if (text.Length == 0) return "Что перевести? Например: «переведи good morning».";
                var tr = Mod<TranslatorModule>();
                string my = tr != null ? tr.My : "ru", second = tr != null ? tr.Second : "en";
                Translator.Begin(form, 0, text, my, second, force, r =>
                {
                    if (hidden) return;
                    if (r.Text.Trim().Length == 0) { Say(F("Не получилось перевести. Кажется, нет связи.")); return; }
                    Say("«" + r.Text.Trim() + "»", Math.Min(22, 6 + r.Text.Length / 14.0));
                });
                return F("Перевожу… {s}.");
            }
            return null;
        }

        static bool IsTimer(Reminder r) { return r.Text.StartsWith("Таймер") || r.Text.StartsWith("Помидор"); }
        List<Reminder> Timers() { return P.Reminders.Where(IsTimer).ToList(); }

        static HubModule Hub()
        {
            var o = Mod<OverlayModule>();
            return o == null || o.Tabs == null ? null : o.Tabs.OfType<HubModule>().FirstOrDefault();
        }

        // ---------------------------------------------------------------- музыка

        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        string Music(string cmd)
        {
            var np = Mod<NowPlayingModule>();
            var i = np == null ? null : np.Current;
            bool ok = true;
            if (i != null && i.Has)
            {
                // пауза и «играй» в плеере одна кнопка: жмём, только если нужно
                if (cmd == "pause" && !i.Playing) return "Уже на паузе.";
                if (cmd == "play" && i.Playing) return "И так играет: «" + Quote(i.Title, 40) + "».";
                ok = np.Command(cmd == "pause" ? "play" : cmd);
            }
            else
            {
                // виджета нет: медиаклавиши, их понимают почти все плееры
                byte vk = cmd == "next" ? (byte)0xB0 : cmd == "prev" ? (byte)0xB1 : (byte)0xB3;
                try { keybd_event(vk, 0, 1, UIntPtr.Zero); keybd_event(vk, 0, 3, UIntPtr.Zero); }
                catch { ok = false; }
            }
            return ok ? SkillPick(cmd) : "Плеер не отвечает.";
        }

        // ---------------------------------------------------------------- поиск игры по названию

        static string Norm(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch0 in (s ?? "").ToLowerInvariant().Replace('ё', 'е'))
                sb.Append(char.IsLetterOrDigit(ch0) ? ch0 : ' ');
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        // Точное совпадение, потом «начинается с», потом все слова, потом с опечатками.
        static HubGame FindGame(List<HubGame> games, string query)
        {
            string q = Norm(query);
            if (q.Length == 0 || games.Count == 0) return null;
            string qq = q.Replace(" ", "");
            var words = q.Split(' ');
            HubGame best = null;
            double bestScore = 0;
            foreach (var g in games)
            {
                string n = Norm(g.Name), nn = n.Replace(" ", "");
                double s;
                if (nn == qq) s = 100;
                else if (nn.StartsWith(qq)) s = 80;
                else if (nn.Contains(qq)) s = 70;
                else if (words.All(w => n.Split(' ').Any(x => x.StartsWith(w)))) s = 60;
                else
                {
                    // опечатки: доля совпадения по расстоянию Левенштейна
                    int d = Lev(nn, qq);
                    s = 50 * (1 - d / (double)Math.Max(nn.Length, qq.Length));
                    if (s < 33) s = 0;
                }
                if (s > bestScore) { bestScore = s; best = g; }
            }
            return best;
        }

        static int Lev(string a, string b)
        {
            var d = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) d[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int prev = d[0];
                d[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int tmp = d[j];
                    d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                    prev = tmp;
                }
            }
            return d[b.Length];
        }
    }
}
