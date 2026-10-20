using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Glasswing
{
    // ---------------------------------------------------------------- персонажи

    // Кто живёт внизу экрана. Рисунки лежат в ресурсах Glasswing.pets.<id>.<файл>,
    // а здесь то, чего в рисунках нет: имя, характер и слова, которыми он говорит.
    class PetChar
    {
        public string Id, Title, DefName, About;
        public bool Female;
        public string Sound, Sound2;             // «кон», «мяф»: голос малыша
        public string AdultName;                 // как называется взрослым
        public string[] Tail;                    // хвост, хвоста, хвостов
        public int Fav;                          // любимая еда, номер в PetLife.Foods
        public Dictionary<string, string[]> Lines = new Dictionary<string, string[]>();
        public Color Accent, Light, BallHi, BallLo;

        public static readonly PetChar[] Table =
        {
            new PetChar
            {
                Id = "kitsune", Title = "Кицунэ", DefName = "Хоно", About = "огненный лис, хвосты растут до девяти",
                Sound = "кон", Sound2 = "мяф", AdultName = "взрослый девятихвостый лис", Tail = new[] { "хвост", "хвоста", "хвостов" }, Fav = 0,
                Accent = Color.FromArgb(255, 140, 50), Light = Color.FromArgb(255, 230, 120),
                BallHi = Color.FromArgb(255, 110, 90), BallLo = Color.FromArgb(170, 20, 30),
                Lines =
                {
                    ["who"] = new[] { "огненный лис-кицунэ. У нас с каждым годом растёт новый хвост, пока их не станет девять. Ну, у меня быстрее." },
                    ["teen.idle"] = new[] { "У меня уже три хвоста!", "Хвосты чешутся… наверное, расту." },
                    ["adult.idle"] = new[] { "Девять огненных хвостов, а всё равно жду тебя.", "Говорят, девятихвостые лисы живут тысячу лет. Нам хватит." },
                    ["adult.pat"] = new[] { "Мои хвосты пылают от счастья." },
                    ["adult.hungry"] = new[] { "Даже великому девятихвостому нужен обед. Покорми меня." },
                    ["adult.wake"] = new[] { "Мм… мне снилось, что мои хвосты подожгли луну." },
                    ["fine"] = new[] { "Отлично! Хвосты пушатся." },
                    ["love"] = new[] { "Мои хвосты аж засветились. ♥" },
                    ["joke"] = new[] {
                        "Почему девятихвостый лис не играет в прятки? Хвосты всегда торчат из-за шкафа.",
                        "Говорят, лисы хитрые. Неправда, я просто заранее знаю, где лежит еда.",
                        "Девятый хвост выдают за терпение. Остальные восемь я получил за обаяние.",
                        "Мой любимый язык программирования — инари-скрипт. Компилируется прямо в счастье." },
                },
            },
            new PetChar
            {
                Id = "shukaku", Title = "Шукаку", DefName = "Суна", About = "песчаный тануки с одним хвостом",
                Sound = "пон", Sound2 = "пи", AdultName = "взрослый песчаный тануки", Tail = new[] { "хвост", "хвоста", "хвостов" }, Fav = 1,
                Accent = Color.FromArgb(214, 170, 96), Light = Color.FromArgb(250, 226, 160),
                BallHi = Color.FromArgb(236, 206, 150), BallLo = Color.FromArgb(150, 108, 56),
                Lines =
                {
                    ["who"] = new[] { "песчаный тануки. Хвост у меня один, зато песка хватит на целую пустыню." },
                    ["teen.idle"] = new[] { "Опять песок в шерсти… ну и пусть.", "Пон-пон! Это я барабаню по животу." },
                    ["adult.idle"] = new[] { "Песок шуршит, а я жду тебя.", "Если станет грустно, я построю тебе песчаный замок." },
                    ["adult.pat"] = new[] { "Аж песок посыпался от удовольствия." },
                    ["adult.hungry"] = new[] { "Даже великому тануки пустыни нужен обед. Покорми меня." },
                    ["adult.wake"] = new[] { "Мм… мне снилось, что я засыпал песком всю луну." },
                    ["fine"] = new[] { "Отлично! Песок тёплый, хвост пушистый." },
                    ["love"] = new[] { "Я аж зашуршал от радости. ♥" },
                    ["joke"] = new[] {
                        "Почему тануки не ходит на пляж? Он и так весь в песке.",
                        "Я не толстый. Это у меня запас песка на зиму." },
                },
            },
            new PetChar
            {
                Id = "matatabi", Title = "Мататаби", DefName = "Аои", About = "кошка синего пламени с двумя хвостами", Female = true,
                Sound = "мяу", Sound2 = "мур", AdultName = "взрослая двухвостая кошка", Tail = new[] { "хвост", "хвоста", "хвостов" }, Fav = 2,
                Accent = Color.FromArgb(90, 168, 255), Light = Color.FromArgb(182, 224, 255),
                BallHi = Color.FromArgb(140, 196, 255), BallLo = Color.FromArgb(30, 72, 170),
                Lines =
                {
                    ["who"] = new[] { "кошка синего пламени. Мой огонь не обжигает, он только греет." },
                    ["teen.idle"] = new[] { "Посмотри, как светится мой хвостик!", "Мур… можно я посплю у тебя на клавиатуре?" },
                    ["adult.idle"] = new[] { "Синее пламя горит тихо, а я жду тебя.", "Кошки гуляют сами по себе, но я лучше посижу с тобой." },
                    ["adult.pat"] = new[] { "Мур-р… моё пламя стало ещё синее." },
                    ["adult.hungry"] = new[] { "Даже великой двухвостой кошке нужен обед. Покорми меня." },
                    ["adult.wake"] = new[] { "Мм… мне снилось, что я ловлю синие звёзды." },
                    ["fine"] = new[] { "Отлично! Пламя ровное, усы торчком." },
                    ["love"] = new[] { "Мои хвосты аж вспыхнули синим. ♥" },
                    ["joke"] = new[] {
                        "Почему кошки любят клавиатуры? Они тёплые и всегда отвечают на лапки.",
                        "У меня не девять жизней. Зато два хвоста, это почти то же самое." },
                },
            },
            new PetChar
            {
                Id = "gyuki", Title = "Гьюки", DefName = "Хачи", About = "бык-осьминог с восемью щупальцами",
                Sound = "му", Sound2 = "буль", AdultName = "взрослый восьмихвостый бык-осьминог", Tail = new[] { "щупальце", "щупальца", "щупалец" }, Fav = 3,
                Accent = Color.FromArgb(160, 104, 240), Light = Color.FromArgb(222, 196, 255),
                BallHi = Color.FromArgb(190, 140, 255), BallLo = Color.FromArgb(80, 32, 140),
                Lines =
                {
                    ["who"] = new[] { "бык-осьминог. У меня рога быка и щупальца осьминога, и все они рады тебя видеть." },
                    ["teen.idle"] = new[] { "Щупальца растут! Скоро смогу обнять тебя всеми сразу.", "Му-у. Это я так пою." },
                    ["adult.idle"] = new[] { "Восемь щупалец, а все ждут тебя.", "Могу держать восемь дел сразу. Хочешь, напомню про одно?" },
                    ["adult.pat"] = new[] { "Все восемь щупалец довольны." },
                    ["adult.hungry"] = new[] { "Даже великому восьмихвостому нужен обед. Покорми меня." },
                    ["adult.wake"] = new[] { "Мм… мне снилось, что я рисую чернилами рэп." },
                    ["fine"] = new[] { "Отлично! Щупальца так и шевелятся." },
                    ["love"] = new[] { "Я тебя обнимаю всеми щупальцами. ♥" },
                    ["joke"] = new[] {
                        "Почему у меня восемь щупалец? Чтобы держать восемь чашек чая сразу.",
                        "Бык говорит «му», осьминог молчит. Поэтому я говорю «му» шёпотом." },
                },
            },
        };

        // Персонаж из ресурсов, которого нет в таблице: говорит общими словами.
        static PetChar Generic(string id)
        {
            return new PetChar
            {
                Id = id, Title = char.ToUpper(id[0]) + id.Substring(1), DefName = "Кроха", About = "новый друг",
                Sound = "пи", Sound2 = "пип", AdultName = "взрослый", Tail = new[] { "хвост", "хвоста", "хвостов" }, Fav = 0,
                Accent = Color.FromArgb(110, 215, 200), Light = Color.FromArgb(220, 255, 245),
                BallHi = Color.FromArgb(150, 230, 215), BallLo = Color.FromArgb(30, 110, 100),
                Lines = { ["who"] = new[] { "твой питомец. Я только недавно здесь поселился." } },
            };
        }

        public const string Prefix = "Glasswing.pets.";
        public static readonly string[] Stages = { "baby", "teen", "adult" };
        public static readonly string[] Faces = { "normal", "lookl", "lookr", "blink", "happy", "sad", "sleep", "eat", "talk" };
        static List<PetChar> avail;

        // Те, чьи рисунки есть в сборке целиком. Порядок: сначала по таблице, потом остальные.
        public static List<PetChar> Available()
        {
            if (avail != null) return avail;
            var names = new HashSet<string>(Assembly.GetExecutingAssembly().GetManifestResourceNames());
            var ids = new List<string>();
            foreach (var n in names)
            {
                if (!n.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                string rest = n.Substring(Prefix.Length);
                int dot = rest.IndexOf('.');
                if (dot <= 0) continue;
                string id = rest.Substring(0, dot);
                if (!ids.Contains(id) && Complete(id, names)) ids.Add(id);
            }
            avail = new List<PetChar>();
            foreach (var c in Table) if (ids.Contains(c.Id)) avail.Add(c);
            foreach (var id in ids.OrderBy(x => x)) if (!Table.Any(c => c.Id == id)) avail.Add(Generic(id));
            return avail;
        }

        static bool Complete(string id, HashSet<string> names)
        {
            if (!names.Contains(Prefix + id + ".manifest.json")) return false;
            foreach (var st in Stages)
                foreach (var part in new[] { "body", "head", "tail", "face_normal" })
                    if (!names.Contains(Prefix + id + "." + st + "_" + part + ".png")) return false;
            try { return PetArt.ReadManifest(id) != null; }
            catch { return false; }
        }

        public static PetChar Find(string id)
        {
            var list = Available();
            return list.FirstOrDefault(c => c.Id == id) ?? list.FirstOrDefault();
        }
    }

    class PetPart { public Bitmap Img; public float X, Y; }
    class PetStage { public float Neck, TailBase, R; public float[] Tails; }

    // Рисунки одного персонажа, загруженные из ресурсов.
    class PetArt : IDisposable
    {
        public readonly PetChar Char;
        public readonly Dictionary<string, PetPart> Parts = new Dictionary<string, PetPart>();
        public readonly Dictionary<string, PetStage> Stages = new Dictionary<string, PetStage>();

        static Stream Res(string id, string file)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(PetChar.Prefix + id + "." + file);
        }

        public static Dictionary<string, object> ReadManifest(string id)
        {
            using (var s = Res(id, "manifest.json"))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, System.Text.Encoding.UTF8))
                {
                    var m = Json.Parse(r.ReadToEnd().TrimStart('﻿')) as Dictionary<string, object>;
                    if (m == null) return null;
                    foreach (var st in PetChar.Stages)
                    {
                        var d = m.ContainsKey(st) ? m[st] as Dictionary<string, object> : null;
                        if (d == null || !(d.ContainsKey("body") && d.ContainsKey("head") && d.ContainsKey("tail") && d.ContainsKey("faces"))) return null;
                    }
                    return m;
                }
            }
        }

        static float Num(object o, float def = 0) { return o is double ? (float)(double)o : def; }

        static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object o;
            return d.TryGetValue(key, out o) ? o as Dictionary<string, object> : null;
        }

        PetArt(PetChar c) { Char = c; }

        public static PetArt Load(PetChar c)
        {
            var art = new PetArt(c);
            var m = ReadManifest(c.Id);
            foreach (var st in PetChar.Stages)
            {
                var d = (Dictionary<string, object>)m[st];
                foreach (var part in new[] { "tail", "body", "head" }) art.Add(st + "_" + part, Obj(d, part));
                var faces = Obj(d, "faces");
                foreach (var face in PetChar.Faces)
                {
                    var fo = faces == null ? null : Obj(faces, face);
                    if (fo == null || !art.Add(st + "_face_" + face, fo)) art.Parts[st + "_face_" + face] = null;
                }
                var normal = art.Parts[st + "_face_normal"] ?? art.Parts[st + "_head"];
                foreach (var face in PetChar.Faces)
                    if (art.Parts[st + "_face_" + face] == null) art.Parts[st + "_face_" + face] = normal;   // не нарисовано: обычная мордочка

                var ps = new PetStage { Neck = Num(d.ContainsKey("neckY") ? d["neckY"] : null), R = Num(d.ContainsKey("R") ? d["R"] : null, 60) };
                var tb = d.ContainsKey("tailBase") ? d["tailBase"] as List<object> : null;
                ps.TailBase = tb != null && tb.Count > 1 ? Num(tb[1]) : 0;
                var ta = d.ContainsKey("tailAngles") ? d["tailAngles"] as List<object> : null;
                ps.Tails = ta != null && ta.Count > 0 ? ta.Select(x => Num(x)).ToArray() : new[] { 0f };
                art.Stages[st] = ps;
            }
            return art;
        }

        bool Add(string key, Dictionary<string, object> pos)
        {
            using (var s = Res(Char.Id, key + ".png"))
            {
                if (s == null || pos == null) return false;
                using (var b = new Bitmap(s))
                {
                    Parts[key] = new PetPart { Img = new Bitmap(b), X = Num(pos.ContainsKey("x") ? pos["x"] : null), Y = Num(pos.ContainsKey("y") ? pos["y"] : null) };
                }
                return true;
            }
        }

        public void Dispose()
        {
            foreach (var p in Parts.Values.Distinct()) if (p != null) p.Img.Dispose();
            Parts.Clear();
        }
    }

    // ---------------------------------------------------------------- окно

    // Окно с попиксельной прозрачностью: виден только сам питомец, остальное прозрачно и пропускает клики.
    class PetForm : Form, WNative.IDeskWidget
    {
        public PetForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80; return cp; }   // LAYERED | TOOLWINDOW
        }

        protected override bool ShowWithoutActivation => true;

        // Показано ли окно по воле приложения: скрытое нарочно хук рабочего стола не возвращает.
        public bool Wanted { get; private set; }

        protected override void SetVisibleCore(bool value)
        {
            Wanted = value;
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WNative.KeepOnDesktop(this);   // «Свернуть все окна» (Win+D) питомца не прячет
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            WNative.OwnByDesktop(Handle);
        }

        [StructLayout(LayoutKind.Sequential)] struct PT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte op, flags, alpha, fmt; }
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref PT pos, ref SZ size, IntPtr src, ref PT srcPos, int key, ref BLEND b, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

        public void SetBitmapAt(Bitmap bmp, int x, int y)
        {
            if (!IsHandleCreated) return;
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(mem, hb);
            try
            {
                var pos = new PT { x = x, y = y };
                var sz = new SZ { cx = bmp.Width, cy = bmp.Height };
                var src = new PT();
                var b = new BLEND { op = 0, flags = 0, alpha = 255, fmt = 1 };
                UpdateLayeredWindow(Handle, screen, ref pos, ref sz, mem, ref src, 0, ref b, 2);
            }
            finally
            {
                SelectObject(mem, old);
                DeleteObject(hb);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }

    class Particle { public int Kind; public float X, Y, VX, VY, Size, Spin; public double Born, Life; }

    // Рисует питомца кадр за кадром: хвосты покачиваются, он дышит, моргает, смотрит на курсор.
    class PetBody : IDisposable
    {
        readonly List<Particle> ps = new List<Particle>();
        readonly Random rnd = new Random();
        readonly Stopwatch clock = Stopwatch.StartNew();
        Bitmap frame;
        double last, nextBlink = 2, blinkUntil, nextZ, bubbleStart;

        public PetArt Art;
        public float Scale = 1f;
        public int W = 320, H = 380;
        public string Stage = "baby";
        public double Mood = 80, Food = 80;
        public bool Sleeping;
        public double HoverUntil, HappyUntil, EatUntil, HopStart = -10, TalkUntil;
        public int FoodKind;
        public string Bubble;
        public double BubbleUntil;
        public string InfoTitle = "", InfoSub = "";
        public double Grow;
        public Rectangle BubbleRect;
        int formLeft;

        // Движение и игры
        public int Mode;                 // 0 сидит, 1 идёт к TargetX, 2 мячик, 3 бежит за курсором, 4 убегает от курсора
        public double ModeUntil;
        public float TargetX, MaxSpeed = 300, Speed, Facing = 1;
        public int Kicks;
        public string Event;             // что случилось за кадр: kick, gameover, arrived
        public PetForm Main, BallWin;
        public bool BallOn;
        public float BX, BY, BVX, BVY;
        float bSpin, runBlend, gvx, gvy;
        bool grab;
        float gdx, gdy;
        double runPhase, lastStep, nextKick, nextJump;
        Rectangle area;
        Bitmap ballFrame;
        readonly Matrix baseM = new Matrix();

        PetChar Ch => Art.Char;

        public double T() { return clock.Elapsed.TotalSeconds; }

        public void Say(string text, double seconds)
        {
            Bubble = text; bubbleStart = T(); BubbleUntil = T() + seconds;
            TalkUntil = T() + Math.Min(4.0, text.Length / 40.0 + 0.3);
        }
        public void HideBubble() { Bubble = null; BubbleRect = Rectangle.Empty; }
        public void Pat()
        {
            HappyUntil = T() + 1.5; HopStart = T();
            for (int i = 0; i < 5; i++) Spawn(0, (float)(rnd.NextDouble() - 0.5) * 70, -20, 1.6 + rnd.NextDouble() * 0.6);
        }
        public void Feed(int kind) { FoodKind = kind; EatUntil = T() + 2.4; HappyUntil = T() + 2.8; }
        public void Celebrate()
        {
            HopStart = T(); HappyUntil = T() + 3;
            for (int i = 0; i < 36; i++) Spawn(1, (float)(rnd.NextDouble() - 0.5) * 220, (float)(-rnd.NextDouble() * 160), 1.4 + rnd.NextDouble());
        }
        // Фокус персонажа: Кицунэ лисьи огоньки, Шукаку песчаный вихрь, Мататаби синее пламя, Гьюки чернильные брызги.
        public void Trick()
        {
            HopStart = T(); HappyUntil = T() + 2.5;
            for (int i = 0; i < 28; i++)
            {
                Spawn(3, (float)(rnd.NextDouble() - 0.5) * 60, (float)(-rnd.NextDouble() * 40), 1.6 + rnd.NextDouble() * 0.9);
                var p = ps[ps.Count - 1];
                p.Spin = (float)(rnd.NextDouble() * Math.PI * 2);
                if (Ch.Id == "gyuki") { p.VX = (float)Math.Cos(p.Spin) * 130; p.VY = (float)Math.Sin(p.Spin) * 90 - 70; }   // брызги во все стороны
                else if (Ch.Id == "shukaku") { p.VX = 0; p.VY = -14; }                                                     // вихрь крутится сам
                else { p.VX *= 1.5f; p.VY *= 1.6f; }
            }
        }
        void TrickDot(Graphics g, Particle p, float px, float py, float u, double age, int a)
        {
            switch (Ch.Id)
            {
                case "shukaku":
                {
                    // песок кружит вокруг, вихрь расширяется
                    double ang = p.Spin + age * 6;
                    float r = (30 + (float)age * 45) * u;
                    float x = px + (float)Math.Cos(ang) * r, y = py + (float)Math.Sin(ang) * r * 0.35f;
                    float s = 3.2f * u * p.Size;
                    using (var b = new SolidBrush(Color.FromArgb(a, Ch.Accent))) g.FillEllipse(b, x - s, y - s, 2 * s, 2 * s);
                    break;
                }
                case "gyuki":
                {
                    // чернила падают вниз
                    float y = py + (float)(age * age) * 160 * u;
                    float s = 5.5f * u * p.Size * (float)(1 + age * 0.4);
                    using (var b = new SolidBrush(Color.FromArgb(a * 220 / 255, 38, 24, 52))) g.FillEllipse(b, px - s, y - s * 0.8f, 2 * s, 1.6f * s);
                    break;
                }
                case "matatabi":
                {
                    // язычок синего пламени: капля острием вверх
                    float s = 7 * u * p.Size * (float)(0.8 + 0.2 * Math.Sin(age * 10 + p.Spin));
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(px, py - s * 1.8f, px + s, py - s * 0.4f, px + s, py + s * 0.6f, px, py + s * 0.6f);
                        path.AddBezier(px, py + s * 0.6f, px - s, py + s * 0.6f, px - s, py - s * 0.4f, px, py - s * 1.8f);
                        using (var b = new SolidBrush(Color.FromArgb(a * 200 / 255, Ch.Accent))) g.FillPath(b, path);
                    }
                    using (var b = new SolidBrush(Color.FromArgb(a, Ch.Light))) g.FillEllipse(b, px - s * 0.35f, py - s * 0.1f, s * 0.7f, s * 0.7f);
                    break;
                }
                default:
                {
                    // лисий огонёк: светящийся шарик с искрой
                    float s = 6 * u * p.Size;
                    using (var b = new SolidBrush(Color.FromArgb(a / 3, Ch.Accent))) g.FillEllipse(b, px - s * 1.8f, py - s * 1.8f, s * 3.6f, s * 3.6f);
                    Sparkle(g, px, py, s * (float)(0.8 + 0.3 * Math.Sin(age * 12 + p.Spin)), a, true);
                    break;
                }
            }
        }
        void Spawn(int kind, float dx, float dy, double life)
        {
            ps.Add(new Particle
            {
                Kind = kind, Born = T(), Life = life, X = dx, Y = dy,
                VX = (float)(rnd.NextDouble() - 0.5) * 18f,
                VY = -(float)(26 + rnd.NextDouble() * 22),
                Size = (float)(0.8 + rnd.NextDouble() * 0.5),
                Spin = (float)(rnd.NextDouble() * 6),
            });
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        void DrawPart(Graphics g, string key, float k)
        {
            PetPart p = Art.Parts[key];
            g.DrawImage(p.Img, new RectangleF(p.X * k, p.Y * k, p.Img.Width * k, p.Img.Height * k));
        }
        static void Heart(Graphics g, float x, float y, float s, int a)
        {
            using (var p = new GraphicsPath())
            {
                p.AddBezier(x, y + s * 0.35f, x - s * 0.1f, y - s * 0.2f, x - s * 0.75f, y - s * 0.05f, x - s * 0.5f, y + s * 0.35f);
                p.AddBezier(x - s * 0.5f, y + s * 0.35f, x - s * 0.3f, y + s * 0.6f, x - s * 0.05f, y + s * 0.75f, x, y + s * 0.9f);
                p.AddBezier(x, y + s * 0.9f, x + s * 0.05f, y + s * 0.75f, x + s * 0.3f, y + s * 0.6f, x + s * 0.5f, y + s * 0.35f);
                p.AddBezier(x + s * 0.5f, y + s * 0.35f, x + s * 0.75f, y - s * 0.05f, x + s * 0.1f, y - s * 0.2f, x, y + s * 0.35f);
                using (var b = new SolidBrush(Color.FromArgb(a, 255, 120, 170))) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(a, 255, 255, 255), Math.Max(1f, s * 0.08f))) g.DrawPath(pen, p);
            }
        }
        void Sparkle(Graphics g, float x, float y, float s, int a, bool light)
        {
            var pts = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double ang = Math.PI / 4 * i;
                float r = (i % 2 == 0) ? s : s * 0.28f;
                pts[i] = new PointF(x + (float)Math.Cos(ang) * r, y + (float)Math.Sin(ang) * r);
            }
            using (var b = new SolidBrush(Color.FromArgb(a, light ? Ch.Light : Ch.Accent))) g.FillPolygon(b, pts);
        }
        void DrawFood(Graphics g, int kind, float x, float y, float s, float left)
        {
            // left: сколько осталось (1 = целое, 0 = съедено)
            if (left <= 0.02f) return;
            g.TranslateTransform(x, y);
            g.ScaleTransform(0.55f + 0.45f * left, 0.55f + 0.45f * left);
            using (var line = new Pen(Color.FromArgb(104, 100, 160), Math.Max(1.2f, s * 0.06f)))
            {
                if (kind == 0)
                {                                   // инари-суши
                    var r = new RectangleF(-s * 0.6f, -s * 0.35f, s * 1.2f, s * 0.7f);
                    using (var p = Round(r, s * 0.3f))
                    using (var b = new LinearGradientBrush(r, Color.FromArgb(240, 186, 96), Color.FromArgb(196, 124, 50), 90f))
                    {
                        g.FillPath(b, p); g.DrawPath(line, p);
                    }
                    using (var w = new SolidBrush(Color.FromArgb(250, 250, 255))) g.FillEllipse(w, -s * 0.42f, -s * 0.48f, s * 0.84f, s * 0.3f);
                }
                else if (kind == 1)
                {                                   // рисовый шарик
                    PointF[] tri = { new PointF(0, -s * 0.6f), new PointF(s * 0.6f, s * 0.42f), new PointF(-s * 0.6f, s * 0.42f) };
                    using (var p = new GraphicsPath())
                    {
                        p.AddClosedCurve(tri, 0.35f);
                        using (var w = new SolidBrush(Color.FromArgb(255, 255, 255))) g.FillPath(w, p);
                        g.DrawPath(line, p);
                    }
                    using (var n = new SolidBrush(Color.FromArgb(40, 60, 54))) g.FillRectangle(n, -s * 0.26f, s * 0.05f, s * 0.52f, s * 0.36f);
                }
                else if (kind == 2)
                {                                   // рыбка
                    using (var p = new GraphicsPath())
                    {
                        p.AddEllipse(-s * 0.6f, -s * 0.28f, s * 0.9f, s * 0.56f);
                        p.AddPolygon(new[] { new PointF(s * 0.22f, 0), new PointF(s * 0.65f, -s * 0.3f), new PointF(s * 0.65f, s * 0.3f) });
                        using (var b = new LinearGradientBrush(new RectangleF(-s * 0.6f, -s * 0.3f, s * 1.3f, s * 0.6f), Color.FromArgb(150, 200, 255), Color.FromArgb(255, 160, 110), 0f)) g.FillPath(b, p);
                        g.DrawPath(line, p);
                    }
                    using (var e = new SolidBrush(Color.FromArgb(48, 34, 86))) g.FillEllipse(e, -s * 0.42f, -s * 0.1f, s * 0.12f, s * 0.12f);
                }
                else
                {                                   // ягоды
                    float[][] xy = { new[] { -0.25f, 0.1f }, new[] { 0.22f, 0.12f }, new[] { 0f, -0.25f } };
                    foreach (float[] c in xy)
                    {
                        var r = new RectangleF((c[0] - 0.24f) * s, (c[1] - 0.24f) * s, s * 0.48f, s * 0.48f);
                        using (var b = new LinearGradientBrush(r, Color.FromArgb(255, 110, 150), Color.FromArgb(190, 40, 90), 60f)) g.FillEllipse(b, r);
                        g.DrawEllipse(line, r);
                        using (var w = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.FillEllipse(w, r.X + r.Width * 0.22f, r.Y + r.Height * 0.2f, r.Width * 0.22f, r.Height * 0.22f);
                    }
                    using (var st = new Pen(Color.FromArgb(90, 170, 90), Math.Max(1.5f, s * 0.08f))) g.DrawLine(st, 0, -s * 0.48f, s * 0.12f, -s * 0.7f);
                }
            }
            g.Transform = baseM;
        }

        public void StartMode(int mode, double seconds)
        {
            Mode = mode; ModeUntil = T() + seconds; Sleeping = false;
            MaxSpeed = mode == 4 ? 400 : 320;
            if (mode == 2) StartBall();
        }
        public void WalkTo(float x, float speed) { Mode = 1; TargetX = x; MaxSpeed = speed; ModeUntil = T() + 30; }
        public void StopMode() { if (Mode == 2) HideBall(); Mode = 0; Speed = 0; }
        public float GroundX() { return Main == null ? 0 : Main.Left + W / 2f; }
        public Rectangle Area() { return Screen.FromPoint(new Point((int)GroundX(), Main.Top + H / 2)).WorkingArea; }

        void StartBall()
        {
            float u = Scale;
            int bs = (int)(64 * u);
            if (BallWin == null)
            {
                BallWin = new PetForm { Text = "Мячик", TopMost = true, Bounds = new Rectangle(-2000, -2000, bs, bs) };
                BallWin.MouseDown += (o, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    grab = true; gdx = Cursor.Position.X - BX; gdy = Cursor.Position.Y - BY; gvx = 0; gvy = 0;
                };
                BallWin.MouseUp += (o, e) =>
                {
                    if (!grab) return;
                    grab = false;
                    float lim = 1800 * Scale;
                    BVX = Math.Max(-lim, Math.Min(lim, gvx)); BVY = Math.Max(-lim, Math.Min(lim, gvy));
                };
                BallWin.Show();
            }
            area = Area();
            BallOn = true; grab = false;
            BX = GroundX() + Facing * -140 * u;
            if (BX < area.Left + 40 * u || BX > area.Right - 40 * u) BX = GroundX() + Facing * 140 * u;
            BY = Main.Top + H * 0.2f; BVX = 0; BVY = 0;
            BallWin.Visible = Main.Visible;
        }
        void HideBall()
        {
            BallOn = false; grab = false;
            if (BallWin != null) BallWin.Visible = false;
        }

        // Спрятать или показать мячик вместе с питомцем.
        public void SyncBall() { if (BallWin != null) BallWin.Visible = BallOn && Main.Visible; }

        // Один шаг жизни: движение, мячик, отрисовка обоих окон. Зовётся таймером ~30 раз в секунду.
        public void Animate()
        {
            if (Main == null || Art == null) return;
            double t = T();
            float dt = (float)Math.Min(0.05, Math.Max(0.001, t - lastStep)); lastStep = t;
            float u = Scale;
            float x = GroundX();
            float ground = Main.Top + H - 8 * u;
            area = Area();
            float minX = area.Left + 46 * u, maxX = area.Right - 46 * u;
            Point c = Cursor.Position;

            if (Mode >= 2 && t > ModeUntil) { if (Mode == 2) HideBall(); Mode = 0; Event = "gameover"; }
            float target = x; bool go = false;
            if (Mode == 1) { target = TargetX; go = true; }
            else if (Mode == 2 && BallOn)
            {
                target = BX; go = Math.Abs(BX - x) > 30 * u;
                if (!grab && Math.Abs(BX - x) < 44 * u && BY > ground - 80 * u && t > nextKick)
                {
                    float dir = BX >= x ? 1 : -1;
                    if (BX < minX + 60 * u) dir = 1; else if (BX > maxX - 60 * u) dir = -1;
                    BVX = dir * (float)(420 + rnd.NextDouble() * 520) * u;
                    BVY = -(float)(420 + rnd.NextDouble() * 520) * u;
                    Facing = dir; HopStart = t; HappyUntil = t + 0.8; nextKick = t + 0.6; Kicks++;
                    if (rnd.NextDouble() < 0.35) Spawn(0, 0, -20, 1.2);
                    Event = "kick";
                }
            }
            else if (Mode == 3)
            {
                target = c.X; go = Math.Abs(c.X - x) > 26 * u;
                if (!go && c.Y < ground && c.Y > ground - 260 * u && t > nextJump) { HopStart = t; HappyUntil = t + 0.7; nextJump = t + 0.9; }
            }
            else if (Mode == 4)
            {
                float dxc = x - c.X;
                if (Math.Abs(dxc) < 230 * u && c.Y > ground - 320 * u && c.Y < ground + 40 * u)
                {
                    float dir = dxc >= 0 ? 1 : -1;
                    if ((dir > 0 && x > maxX - 40 * u) || (dir < 0 && x < minX + 40 * u)) dir = -dir;   // прижали к краю: прошмыгнуть мимо
                    target = x + dir * 320 * u; go = true;
                }
            }
            float want = 0;
            if (go)
            {
                float d = target - x;
                float sp = Math.Min(MaxSpeed * u, Math.Abs(d) * 4f + 40 * u);
                want = Math.Sign(d) * sp;
                if (Math.Abs(d) < 5 * u) { want = 0; if (Mode == 1) { Mode = 0; Event = "arrived"; } }
            }
            float acc = 1600 * u * dt;
            if (Speed < want) Speed = Math.Min(want, Speed + acc); else Speed = Math.Max(want, Speed - acc);
            x += Speed * dt;
            if (x < minX) { x = minX; Speed = 0; if (Mode == 1) { Mode = 0; Event = "arrived"; } }
            if (x > maxX) { x = maxX; Speed = 0; if (Mode == 1) { Mode = 0; Event = "arrived"; } }
            if (Math.Abs(Speed) > 12 * u) Facing = Speed > 0 ? 1 : -1;
            bool run = Math.Abs(Speed) > 20 * u;
            runBlend += ((run ? 1f : 0f) - runBlend) * Math.Min(1f, dt * 8f);
            if (run) runPhase += dt * Math.Abs(Speed) / (300 * u) * Math.PI * 2 * 2.6;

            int left = (int)Math.Round(x - W / 2f);
            formLeft = left;
            if (Main.Left != left) Main.Left = left;
            Main.SetBitmapAt(Render(), left, Main.Top);

            // мячик
            if (BallOn && BallWin != null)
            {
                float r = 22 * u;
                if (grab)
                {
                    float nx = c.X - gdx, ny = c.Y - gdy;
                    gvx = gvx * 0.6f + (nx - BX) / dt * 0.4f; gvy = gvy * 0.6f + (ny - BY) / dt * 0.4f;
                    BX = nx; BY = ny;
                }
                else
                {
                    BVY += 1500 * u * dt;
                    BX += BVX * dt; BY += BVY * dt;
                    if (BY > ground - r)
                    {
                        BY = ground - r;
                        if (BVY > 0) BVY = -BVY * 0.55f;
                        if (Math.Abs(BVY) < 70 * u) BVY = 0;
                        BVX *= (float)Math.Pow(0.35, dt);
                    }
                    if (BY < area.Top + r) { BY = area.Top + r; BVY = Math.Abs(BVY) * 0.6f; }
                    if (BX < area.Left + r) { BX = area.Left + r; BVX = Math.Abs(BVX) * 0.7f; }
                    if (BX > area.Right - r) { BX = area.Right - r; BVX = -Math.Abs(BVX) * 0.7f; }
                    bSpin += BVX * dt / r;
                }
                int bs = BallWin.Width;
                var bp = new Point((int)(BX - bs / 2f), (int)(BY - bs / 2f));
                if (BallWin.Location != bp) BallWin.Location = bp;
                BallWin.SetBitmapAt(RenderBall(bs, r), bp.X, bp.Y);
            }
        }

        // Мячик-тэмари в цветах персонажа, с белыми и светлыми нитями.
        Bitmap RenderBall(int bs, float r)
        {
            if (ballFrame == null || ballFrame.Width != bs) { if (ballFrame != null) ballFrame.Dispose(); ballFrame = new Bitmap(bs, bs, PixelFormat.Format32bppPArgb); }
            using (var g = Graphics.FromImage(ballFrame))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float cx = bs / 2f, cy = bs / 2f;
                var br = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                using (var p = new GraphicsPath())
                {
                    p.AddEllipse(br);
                    using (var pb = new PathGradientBrush(p))
                    {
                        pb.CenterPoint = new PointF(cx - r * 0.35f, cy - r * 0.4f);
                        pb.CenterColor = Ch.BallHi;
                        pb.SurroundColors = new[] { Ch.BallLo };
                        g.FillPath(pb, p);
                    }
                    g.SetClip(p);
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform((float)(bSpin * 180 / Math.PI));
                    using (var w = new Pen(Color.FromArgb(235, 255, 248, 236), r * 0.16f))
                    using (var gd = new Pen(Color.FromArgb(235, Ch.Light), r * 0.1f))
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            g.DrawEllipse(w, -r * 0.42f, -r * 1.1f, r * 0.84f, r * 2.2f);
                            g.DrawLine(gd, -r * 1.1f, 0, r * 1.1f, 0);
                            g.RotateTransform(60);
                        }
                    }
                    g.ResetTransform();
                    g.ResetClip();
                    using (var hl = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) g.FillEllipse(hl, cx - r * 0.6f, cy - r * 0.7f, r * 0.5f, r * 0.32f);
                    var lo = Ch.BallLo;
                    using (var o = new Pen(Color.FromArgb(230, lo.R / 2, lo.G / 2, lo.B / 2), 1.6f * Scale)) g.DrawEllipse(o, br);
                }
            }
            return ballFrame;
        }

        public Bitmap Render()
        {
            double t = T();
            double dt = Math.Min(0.1, t - last); last = t;
            if (frame == null || frame.Width != W || frame.Height != H)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
            }
            float k = 0.5f * Scale;
            float u = Scale;                           // единица интерфейса
            float gx = W / 2f, gy = H - 8 * u;
            bool happy = t < HappyUntil, eating = t < EatUntil;
            bool sad = !happy && !eating && (Mood < 25 || Food < 20);
            double hopP = (t - HopStart) / 0.7;
            float hop = (hopP >= 0 && hopP < 1) ? -(float)(Math.Abs(Math.Sin(hopP * Math.PI * 2)) * 10 * u * (1 - hopP * 0.4)) : 0f;
            double br = Sleeping ? 1.1 : 2.0;
            float breathe = (float)Math.Sin(t * br);
            float sy = 1f + breathe * (Sleeping ? 0.022f : 0.013f);
            float rb = runBlend;
            if (rb > 0.01f)
            {
                hop = Math.Min(hop, -(float)(Math.Abs(Math.Sin(runPhase)) * 13 * u * rb));
                sy *= 1f + (float)Math.Cos(runPhase * 2) * 0.05f * rb;
            }

            if (t > nextBlink) { blinkUntil = t + 0.14; nextBlink = t + 2.5 + rnd.NextDouble() * 4; }
            if (Sleeping && t > nextZ) { nextZ = t + 1.3; Spawn(2, 26, -40, 2.6); }

            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                // на бегу наклоняется вперёд
                baseM.Reset();
                baseM.RotateAt(Facing * 9f * rb * (float)(0.8 + 0.2 * Math.Cos(runPhase)), new PointF(gx, gy));
                g.Transform = baseM;

                string st = Stage;
                var stage = Art.Stages[st];
                // тень на «полу»
                float bw = Art.Parts[st + "_body"].Img.Width * k;
                using (var sh = new GraphicsPath())
                {
                    sh.AddEllipse(new RectangleF(gx - bw * 0.75f, gy - 7 * u, bw * 1.5f, 14 * u));
                    using (var pb = new PathGradientBrush(sh))
                    {
                        pb.CenterColor = Color.FromArgb(hop < -2 ? 40 : 70, 20, 16, 40);
                        pb.SurroundColors = new[] { Color.FromArgb(0, 20, 16, 40) };
                        g.FillPath(pb, sh);
                    }
                }
                // хвосты
                float[] ta = stage.Tails;
                double amp = happy ? 10 : sad ? 2.5 : (Sleeping ? 2 : 4.5);
                double sp = happy ? 6.5 : sad ? 1.0 : (Sleeping ? 0.8 : 1.7);
                for (int i = 0; i < ta.Length; i++)
                {
                    float a = ta[i];
                    if (sad || Sleeping) a = Math.Max(-88f, Math.Min(88f, a * 1.18f + (a >= 0 ? 6 : -6)));
                    float ang = a + (float)(amp * Math.Sin(t * sp + i * 0.85));
                    if (rb > 0.01f)
                    {
                        // хвосты развеваются позади
                        float back = ta.Length == 1 ? 72f : 48f + 44f * i / (ta.Length - 1);
                        float runA = -Facing * back + (float)(6 * Math.Sin(runPhase + i * 0.7));
                        ang = ang * (1 - rb) + runA * rb;
                    }
                    g.TranslateTransform(gx, gy + stage.TailBase * k + hop, MatrixOrder.Prepend);
                    g.RotateTransform(ang);
                    DrawPart(g, st + "_tail", k);
                    g.Transform = baseM;
                }
                // тело дышит
                g.TranslateTransform(gx, gy + hop);
                g.ScaleTransform(1f, sy);
                DrawPart(g, st + "_body", k);
                g.Transform = baseM;
                // голова
                float hx = gx, hy = gy + stage.Neck * k * sy + hop + (float)(Math.Sin(t * br + 0.6) * 1.2 * u);
                float tilt = (float)(Math.Sin(t * 0.7) * 2.0);
                if (happy) tilt = (float)(Math.Sin(t * 5) * 6);
                if (sad) tilt = 5f;
                if (Sleeping) tilt = 8f;

                string face = "normal";
                int cx = Cursor.Position.X - (formLeft + (int)gx);
                if (cx < -150 * u) face = "lookl"; else if (cx > 150 * u) face = "lookr";
                if (rb > 0.3f) face = Facing > 0 ? "lookr" : "lookl";
                if (Mode == 2 && BallOn) face = BX < formLeft + gx - 40 * u ? "lookl" : (BX > formLeft + gx + 40 * u ? "lookr" : "normal");
                if (sad) face = "sad";
                if (t < blinkUntil) face = "blink";
                if (t < TalkUntil && Bubble != null) face = ((int)(t / 0.13) % 2 == 0) ? "talk" : "normal";
                if (happy) face = "happy";
                if (eating) face = ((int)(t / 0.22) % 2 == 0) ? "eat" : "happy";
                if (Sleeping) face = "sleep";

                g.TranslateTransform(hx, hy);
                g.RotateTransform(tilt);
                DrawPart(g, st + "_head", k);
                DrawPart(g, st + "_face_" + face, k);
                g.Transform = baseM;
                float R = stage.R * k;
                float headTop = hy - R * 1.9f;

                // еда у мордочки
                if (eating)
                {
                    float left = (float)((EatUntil - t) / 2.4);
                    float bites = (float)Math.Ceiling(left * 4) / 4f;
                    DrawFood(g, FoodKind, hx - R * 0.15f, hy + R * 0.95f, 26 * u, bites);
                }
                g.ResetTransform();
                // частицы: сердечки, искры, «z»
                for (int i = ps.Count - 1; i >= 0; i--)
                {
                    Particle p = ps[i];
                    double age = t - p.Born;
                    if (age > p.Life) { ps.RemoveAt(i); continue; }
                    p.X += p.VX * (float)dt; p.Y += p.VY * (float)dt;
                    int a = (int)(255 * Math.Min(1, (p.Life - age) / 0.5) * Math.Min(1, age / 0.15));
                    float px = hx + p.X * u, py = hy + p.Y * u;
                    if (p.Kind == 0) Heart(g, px, py, 13 * u * p.Size, a);
                    else if (p.Kind == 1) Sparkle(g, px, py, 7 * u * p.Size * (float)(0.7 + 0.3 * Math.Sin(age * 8 + p.Spin)), a, i % 3 == 0);
                    else if (p.Kind == 3) TrickDot(g, p, px, py, u, age, a);
                    else
                    {
                        using (var zf = new Font("Segoe UI", 11 * u * p.Size, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var zb = new SolidBrush(Color.FromArgb(a, 210, 220, 255)))
                            g.DrawString("z", zf, zb, px + (float)Math.Sin(age * 2) * 6 * u, py);
                    }
                }
                // облачко с речью
                BubbleRect = Rectangle.Empty;
                if (Bubble != null && t > BubbleUntil) Bubble = null;
                if (Bubble != null) DrawBubble(g, t, u, hx, headTop);
                else if (t < HoverUntil) DrawInfo(g, u, hx, headTop);
            }
            return frame;
        }

        void DrawBubble(Graphics g, double t, float u, float hx, float headTop)
        {
            using (var f = new Font("Segoe UI", 13.5f * u, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                float maxW = Math.Min(W - 16 * u, 270 * u);
                SizeF ms = g.MeasureString(Bubble, f, (int)(maxW - 22 * u));
                float bwid = ms.Width + 22 * u, bh = ms.Height + 14 * u;
                float bx = Math.Max(4 * u, Math.Min(W - bwid - 4 * u, hx - bwid / 2));
                float by = Math.Max(4 * u, headTop - bh - 14 * u);
                double fade = Math.Min(1, Math.Min((t - bubbleStart) / 0.18, (BubbleUntil - t) / 0.4));
                int al = (int)(255 * Math.Max(0, fade));
                var r = new RectangleF(bx, by, bwid, bh);
                using (var p = Round(r, 11 * u))
                {
                    float tipX = Math.Max(bx + 16 * u, Math.Min(bx + bwid - 16 * u, hx));
                    using (var tail = new GraphicsPath())
                    {
                        tail.AddPolygon(new[] { new PointF(tipX - 8 * u, by + bh - 1), new PointF(tipX + 8 * u, by + bh - 1), new PointF(tipX + 2 * u, by + bh + 10 * u) });
                        using (var rg = new Region(p))
                        {
                            rg.Union(tail);
                            using (var b = new SolidBrush(Color.FromArgb(al * 240 / 255, 252, 251, 255))) g.FillRegion(b, rg);
                        }
                    }
                    using (var pen = new Pen(Color.FromArgb(al * 200 / 255, Ch.Accent), 1.4f * u)) g.DrawPath(pen, p);
                }
                int n = Math.Min(Bubble.Length, (int)((t - bubbleStart) * 55) + 1);
                using (var tb = new SolidBrush(Color.FromArgb(al, 70, 30, 20)))
                    g.DrawString(Bubble.Substring(0, n), f, tb, new RectangleF(bx + 11 * u, by + 7 * u, bwid - 20 * u, bh));
                BubbleRect = Rectangle.Round(r);
            }
        }

        // Подсказка при наведении: имя, возраст и шкалы.
        void DrawInfo(Graphics g, float u, float hx, float headTop)
        {
            float pw = 190 * u, ph = 92 * u;
            float px = Math.Max(4 * u, Math.Min(W - pw - 4 * u, hx - pw / 2));
            float py = Math.Max(4 * u, headTop - ph - 10 * u);
            var r = new RectangleF(px, py, pw, ph);
            using (var p = Round(r, 10 * u))
            {
                using (var b = new SolidBrush(Color.FromArgb(215, 24, 26, 40))) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(120, Ch.Accent), 1f * u)) g.DrawPath(pen, p);
            }
            using (var f1 = new Font("Segoe UI Semibold", 13 * u, GraphicsUnit.Pixel))
            using (var f2 = new Font("Segoe UI", 11 * u, GraphicsUnit.Pixel))
            using (var w = new SolidBrush(Color.FromArgb(245, 245, 255)))
            using (var m = new SolidBrush(Color.FromArgb(175, 205, 210, 235)))
            {
                g.DrawString(InfoTitle, f1, w, new RectangleF(px + 10 * u, py + 6 * u, pw - 16 * u, 18 * u), Ui.Left);
                g.DrawString(InfoSub, f2, m, px + 10 * u, py + 24 * u);
                string[] names = { "Сытость", "Настроение", "Рост" };
                double[] vals = { Food / 100.0, Mood / 100.0, Grow };
                Color[] cols = { Color.FromArgb(255, 196, 92), Color.FromArgb(255, 130, 180), Ch.Accent };
                for (int i = 0; i < 3; i++)
                {
                    float yy = py + 44 * u + i * 15 * u;
                    g.DrawString(names[i], f2, m, px + 10 * u, yy - 2 * u);
                    var bar = new RectangleF(px + 82 * u, yy + 4 * u, pw - 94 * u, 5 * u);
                    using (var bp = Round(bar, 2.5f * u)) using (var bb = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillPath(bb, bp);
                    float fw = (float)(bar.Width * Math.Max(0, Math.Min(1, vals[i])));
                    if (fw > 5 * u)
                    {
                        using (var bp = Round(new RectangleF(bar.X, bar.Y, fw, bar.Height), 2.5f * u))
                        using (var bb = new SolidBrush(cols[i])) g.FillPath(bb, bp);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (BallWin != null) { BallWin.Dispose(); BallWin = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
            if (ballFrame != null) { ballFrame.Dispose(); ballFrame = null; }
            baseM.Dispose();
        }
    }
}
