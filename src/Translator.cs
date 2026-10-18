using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Glasswing
{
    // ---------------------------------------------------------------- клавиатура и буфер обмена

    static class KeyTools
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();

        public static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        static void CtrlPlus(byte key)
        {
            keybd_event(0x11, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 2, UIntPtr.Zero);
            keybd_event(0x11, 0, 2, UIntPtr.Zero);
        }

        public static string ClipText()
        {
            for (int i = 0; i < 6; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch { Thread.Sleep(30); }
            }
            return null;
        }

        public static void SetClip(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            for (int i = 0; i < 6; i++)
            {
                try { Clipboard.SetText(s); return; }
                catch { Thread.Sleep(30); }
            }
        }

        // Копирует выделенный текст в активной программе (нажимает за пользователя Ctrl+C),
        // а потом возвращает в буфер то, что там было. null = ничего не выделено.
        public static string CopySelection()
        {
            int t0 = Environment.TickCount;
            while (Environment.TickCount - t0 < 1200 && (Down(0x10) || Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C)))
                Thread.Sleep(10);
            string old = ClipText();
            uint seq = GetClipboardSequenceNumber();
            CtrlPlus(0x43);
            string got = null;
            t0 = Environment.TickCount;
            while (Environment.TickCount - t0 < 700)
            {
                Thread.Sleep(15);
                if (GetClipboardSequenceNumber() != seq)
                {
                    Thread.Sleep(40);
                    got = ClipText();
                    break;
                }
            }
            if (got != null && old != null && old != got) SetClip(old);
            return got;
        }

        // Вставляет текст вместо выделенного в активной программе.
        public static void Paste(string s)
        {
            SetClip(s);
            Thread.Sleep(40);
            CtrlPlus(0x56);
        }
    }

    // Следит за клавишами: двойное Ctrl+C (1), горячая клавиша (2), Esc (3).
    // Перехват клавиатуры живёт в отдельном потоке, чтобы никогда не тормозить набор текста.
    class KeyWatch : NativeWindow
    {
        delegate IntPtr LLProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LLProc fn, IntPtr mod, uint tid);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint tid, int msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        const int WM_TRIGGER = 0x8000 + 77;

        public event Action<int> Triggered;
        public volatile bool DoubleCopy = true;
        public volatile bool EscArmed;
        public volatile bool HookOk;
        readonly LLProc proc;
        IntPtr hook = IntPtr.Zero;
        readonly IntPtr me;
        uint hookThread;
        int lastCopy;
        bool cDown, hotOn;

        public KeyWatch()
        {
            var cp = new CreateParams { Parent = new IntPtr(-3) };   // окно только для сообщений
            CreateHandle(cp);
            me = Handle;
            proc = Hook;
            var ready = new ManualResetEvent(false);
            var t = new Thread(() =>
            {
                hookThread = GetCurrentThreadId();
                hook = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0);
                HookOk = hook != IntPtr.Zero;
                ready.Set();
                Application.Run();
                if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            ready.WaitOne(3000);
        }

        public bool SetHotKey(uint mods, uint vk)
        {
            if (hotOn) { UnregisterHotKey(Handle, 1); hotOn = false; }
            if (vk == 0) return true;
            hotOn = RegisterHotKey(Handle, 1, mods | 0x4000, vk);
            return hotOn;
        }

        public void Close()
        {
            if (hotOn) UnregisterHotKey(Handle, 1);
            hotOn = false;
            if (hookThread != 0) PostThreadMessage(hookThread, 0x0012, IntPtr.Zero, IntPtr.Zero);   // WM_QUIT
            DestroyHandle();
        }

        IntPtr Hook(int code, IntPtr w, IntPtr l)
        {
            try
            {
                if (code >= 0)
                {
                    int vk = Marshal.ReadInt32(l);
                    int flags = Marshal.ReadInt32(l, 8);
                    int msg = w.ToInt32();
                    bool down = msg == 0x100 || msg == 0x104;
                    bool up = msg == 0x101 || msg == 0x105;
                    if ((flags & 0x10) == 0)      // нажатия, которые сделала программа (в том числе мы), пропускаем
                    {
                        if (vk == 0x43)
                        {
                            if (down && !cDown)
                            {
                                cDown = true;
                                if (KeyTools.Down(0x11) && !KeyTools.Down(0x12) && !KeyTools.Down(0x10) && !KeyTools.Down(0x5B))
                                {
                                    int now = Environment.TickCount;
                                    if (lastCopy != 0 && now - lastCopy < 500)
                                    {
                                        lastCopy = 0;
                                        if (DoubleCopy) PostMessage(me, WM_TRIGGER, new IntPtr(1), IntPtr.Zero);
                                    }
                                    else lastCopy = now;
                                }
                                else lastCopy = 0;
                            }
                            else if (up) cDown = false;
                        }
                        else if (down && vk != 0x11 && vk != 0xA2 && vk != 0xA3)
                        {
                            lastCopy = 0;
                            if (vk == 0x1B && EscArmed) PostMessage(me, WM_TRIGGER, new IntPtr(3), IntPtr.Zero);
                        }
                    }
                }
            }
            catch { }
            return CallNextHookEx(hook, code, w, l);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 || m.Msg == WM_TRIGGER)
            {
                int kind = m.Msg == 0x0312 ? 2 : m.WParam.ToInt32();
                try { Triggered?.Invoke(kind); }
                catch (Exception ex) { Log.Write("клавиши: " + ex.Message); }
                return;
            }
            base.WndProc(ref m);
        }
    }

    // ---------------------------------------------------------------- перевод

    class TrResult
    {
        public int Seq;
        public string Src = "", Tl = "", Text = "", Dict = "", Error = "";
    }

    // Перевод через Google Translate, если он не ответил, через MyMemory.
    static class Translator
    {
        static string Http(string url, string post)
        {
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                if (post == null) return wc.DownloadString(url);
                wc.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded;charset=UTF-8";
                return wc.UploadString(url, "POST", post);
            }
        }

        static string Escape(string s)
        {
            // Uri.EscapeDataString в старых .NET не любит длинные строки, режем кусками
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i += 2000) sb.Append(Uri.EscapeDataString(s.Substring(i, Math.Min(2000, s.Length - i))));
            return sb.ToString();
        }

        public static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return a.Split('-')[0].ToLowerInvariant() == b.Split('-')[0].ToLowerInvariant();
        }

        static bool MostlyCyrillic(string t)
        {
            int cy = 0, la = 0;
            foreach (char c in t)
            {
                if (c >= 0x400 && c <= 0x4FF) cy++;
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) la++;
            }
            return cy > la;
        }

        static TrResult Google(string text, string tl, string hl)
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=" + tl + "&hl=" + hl + "&dt=t&dt=bd&ie=UTF-8&oe=UTF-8";
            var root = Json.Parse(Http(url, "q=" + Escape(text))) as List<object>;
            var r = new TrResult { Tl = tl };
            var sb = new StringBuilder();
            var segs = root != null && root.Count > 0 ? root[0] as List<object> : null;
            if (segs != null)
                foreach (var o in segs)
                {
                    var seg = o as List<object>;
                    if (seg != null && seg.Count > 0 && seg[0] is string) sb.Append((string)seg[0]);
                }
            r.Text = sb.ToString().Trim();
            if (root != null && root.Count > 2 && root[2] is string) r.Src = (string)root[2];
            // словарь: для одного слова Google даёт варианты по частям речи
            var dict = root != null && root.Count > 1 ? root[1] as List<object> : null;
            if (dict != null)
            {
                var db = new StringBuilder();
                int n = 0;
                foreach (var o in dict)
                {
                    var part = o as List<object>;
                    if (part == null || part.Count < 2) continue;
                    var terms = part[1] as List<object>;
                    if (terms == null) continue;
                    var words = new List<string>();
                    foreach (var w in terms)
                    {
                        var s = w as string;
                        if (s != null && !words.Contains(s) && s != r.Text) words.Add(s);
                        if (words.Count >= 5) break;
                    }
                    if (words.Count == 0) continue;
                    if (db.Length > 0) db.Append('\n');
                    db.Append((part[0] as string ?? "") + ": " + string.Join(", ", words));
                    if (++n >= 3) break;
                }
                r.Dict = db.ToString();
            }
            if (r.Text.Length == 0) throw new Exception("пустой ответ");
            return r;
        }

        static TrResult MyMemory(string text, string src, string tl)
        {
            if (text.Length > 480) text = text.Substring(0, 480);
            string url = "https://api.mymemory.translated.net/get?q=" + Escape(text) + "&langpair=" + src + "|" + tl;
            var root = Json.Parse(Http(url, null)) as Dictionary<string, object>;
            object data, tt;
            if (root == null || !root.TryGetValue("responseData", out data)) throw new Exception("пустой ответ");
            var d = data as Dictionary<string, object>;
            if (d == null || !d.TryGetValue("translatedText", out tt) || !(tt is string)) throw new Exception("пустой ответ");
            return new TrResult { Src = src, Tl = tl, Text = WebUtility.HtmlDecode((string)tt) };
        }

        // my: родной язык, second: на какой переводить текст, который уже на родном.
        // force: если не пусто, переводим ровно на этот язык.
        static TrResult Run(string text, string my, string second, string force)
        {
            try
            {
                if (!string.IsNullOrEmpty(force)) return Google(text, force, my);
                var r = Google(text, my, my);
                if (Same(r.Src, my)) r = Google(text, second, my);
                return r;
            }
            catch (Exception ge)
            {
                try
                {
                    string src = MostlyCyrillic(text) ? "ru" : "en";
                    string tl = !string.IsNullOrEmpty(force) ? force : (Same(src, my) ? second : my);
                    return MyMemory(text, src, tl);
                }
                catch (Exception me)
                {
                    return new TrResult { Error = ge.Message + " / " + me.Message };
                }
            }
        }

        // Переводит в фоне и возвращает результат в поток окна.
        public static void Begin(Control ui, int seq, string text, string my, string second, string force, Action<TrResult> done)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
            var t = new Thread(() =>
            {
                var r = Run(text, my, second, force);
                r.Seq = seq;
                try { if (!ui.IsDisposed) ui.BeginInvoke(done, r); } catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    // ---------------------------------------------------------------- модуль

    class TranslatorModule : Module
    {
        public static readonly string[][] Languages =
        {
            new[] { "ru", "Русский" }, new[] { "en", "Английский" }, new[] { "uk", "Украинский" }, new[] { "be", "Белорусский" },
            new[] { "kk", "Казахский" }, new[] { "de", "Немецкий" }, new[] { "fr", "Французский" }, new[] { "es", "Испанский" },
            new[] { "it", "Итальянский" }, new[] { "pt", "Португальский" }, new[] { "pl", "Польский" }, new[] { "tr", "Турецкий" },
            new[] { "ja", "Японский" }, new[] { "ko", "Корейский" }, new[] { "zh-CN", "Китайский" }, new[] { "ar", "Арабский" },
        };
        static readonly string[] HotKeyNames = { "Ctrl + Alt + T", "Ctrl + Shift + Y", "Alt + Q", "Не использовать" };
        static readonly uint[][] HotKeyCodes = { new uint[] { 3, 0x54 }, new uint[] { 6, 0x59 }, new uint[] { 1, 0x51 }, new uint[] { 0, 0 } };

        KeyWatch watch;
        TranslateCard card;
        readonly System.Windows.Forms.Timer afterCopy = new System.Windows.Forms.Timer { Interval = 160 };

        public TranslatorModule()
        {
            // двойное Ctrl+C: ждём, пока второе копирование долетит до буфера, и берём текст оттуда
            afterCopy.Tick += (s, e) =>
            {
                afterCopy.Stop();
                string txt = KeyTools.ClipText();
                if (!string.IsNullOrWhiteSpace(txt)) Open(txt, true);
            };
        }

        public override string Id => "tr";
        public override string Name => "Переводчик";
        public override string Short => "Ctrl+C два раза";
        public override string Description => "Выдели текст в любой программе, нажми Ctrl+C два раза, и рядом появится стеклянная карточка с переводом.";
        public override IconKind Icon => IconKind.Translate;
        public override bool Running => watch != null;

        public string My
        {
            get { return Settings.GetString(Id, "My", "ru"); }
            set { Settings.Set(Id, "My", value); }
        }

        public string Second
        {
            get { return Settings.GetString(Id, "Second", "en"); }
            set { Settings.Set(Id, "Second", value); }
        }

        public bool DoubleCopy
        {
            get { return Settings.GetBool(Id, "DoubleCopy", true); }
            set { Settings.Set(Id, "DoubleCopy", value); if (watch != null) watch.DoubleCopy = value; }
        }

        public string HotKey
        {
            get
            {
                string h = Settings.GetString(Id, "HotKey", HotKeyNames[0]);
                return Array.IndexOf(HotKeyNames, h) >= 0 ? h : HotKeyNames[0];
            }
            set { Settings.Set(Id, "HotKey", value); ApplyHotKey(true); }
        }

        public static string LangName(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            foreach (var l in Languages) if (Translator.Same(l[0], code)) return l[1];
            return code.ToUpperInvariant();
        }

        static string NextLang(string code)
        {
            int i = Array.FindIndex(Languages, l => l[0] == code);
            return Languages[(i + 1) % Languages.Length][0];
        }

        void ApplyHotKey(bool warn)
        {
            if (watch == null) return;
            var hk = HotKeyCodes[Array.IndexOf(HotKeyNames, HotKey)];
            if (!watch.SetHotKey(hk[0], hk[1]) && warn)
                MessageBox.Show("Сочетание " + HotKey + " уже занято другой программой. Выбери другое.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public override void Start()
        {
            if (watch != null) return;
            watch = new KeyWatch { DoubleCopy = DoubleCopy };
            watch.Triggered += kind =>
            {
                if (kind == 1) { afterCopy.Stop(); afterCopy.Start(); }
                else if (kind == 2) Open(KeyTools.CopySelection(), true);
                else if (kind == 3) CloseCard();
            };
            if (!watch.HookOk) Log.Write("переводчик: не получилось следить за Ctrl+C");
            ApplyHotKey(false);
        }

        public override void Stop()
        {
            CloseCard();
            if (watch == null) return;
            watch.Close();
            watch = null;
        }

        public override void GlassChanged() { if (card != null && !card.IsDisposed) card.ApplyGlass(); }

        // Щелчок по «Показать пример» и пункт в трее: переводим то, что уже скопировано.
        public void TranslateClipboard() { Open(KeyTools.ClipText(), false); }

        void Open(string text, bool selection)
        {
            CloseCard();
            card = new TranslateCard(this, text, selection);
            card.FormClosed += (s, e) => { if (watch != null) watch.EscArmed = false; };
            card.Show();
            if (watch != null) watch.EscArmed = true;
        }

        void CloseCard()
        {
            if (card != null && !card.IsDisposed) card.Close();
            card = null;
            if (watch != null) watch.EscArmed = false;
        }

        public override IEnumerable<Row> Rows()
        {
            yield return new Row { Label = "Переводить по Ctrl+C два раза", On = () => DoubleCopy, Click = () => DoubleCopy = !DoubleCopy };
            yield return new Row { Label = "Горячая клавиша", Value = () => HotKey, Click = () => HotKey = HotKeyNames[(Array.IndexOf(HotKeyNames, HotKey) + 1) % HotKeyNames.Length] };
            yield return new Row { Label = "Переводить на", Value = () => LangName(My), Click = () => My = NextLang(My) };
            yield return new Row { Label = "А если текст уже на нём, то на", Value = () => LangName(Second), Click = () => Second = NextLang(Second) };
            yield return new Row { Label = "Перевести скопированное", Value = () => "Открыть", Click = TranslateClipboard };
        }
    }

    // ---------------------------------------------------------------- карточка с переводом

    class TranslateCard : Form
    {
        class Btn { public string Id, Text, Lang; public Rectangle R; }

        static int seqCounter;
        static readonly Graphics measure = Graphics.FromImage(new Bitmap(4, 4));

        readonly TranslatorModule mod;
        readonly string original;
        readonly bool selection;
        readonly Point anchor;
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 30 };
        string state = "loading", message = "";
        TrResult result;
        DateTime opened = DateTime.Now, copiedAt = DateTime.MinValue, lastDots = DateTime.MinValue;
        string hover = "";
        bool copied, moved, noGlass;
        int seq;
        Point target;
        Point? dragMouse;
        Point dragForm;
        bool dragged, scrollDrag;
        double scroll;

        // раскладка
        int cardW, pad, height;
        RectangleF headR, mainR, dictR, origR;
        Rectangle closeR, track;
        Font mainFont, origFont;
        string mainText;
        float mainFull;
        bool scrollable;
        readonly List<Btn> buttons = new List<Btn>();

        public TranslateCard(TranslatorModule m, string text, bool selection)
        {
            mod = m;
            this.selection = selection;
            text = (text ?? "").Trim();
            if (text.Length > 5000) text = text.Substring(0, 5000);
            original = text;
            anchor = Cursor.Position;
            cardW = Ui.S(440);
            pad = Ui.S(18);
            Text = App.Name + ": перевод";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            if (original.Length == 0)
            {
                state = "error";
                message = "Сначала выдели текст, а потом нажми клавиши ещё раз.";
            }
            DoLayout();
            Place();
            Bounds = new Rectangle(target.X, target.Y + Ui.S(10), cardW, height);
            tick.Tick += OnTick;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08 | 0x80 | 0x08000000;   // TOPMOST | TOOLWINDOW | NOACTIVATE
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WNative.RoundDark(Handle);
            ApplyGlass();
        }

        public void ApplyGlass()
        {
            if (!IsHandleCreated) return;
            noGlass = !Ui.ApplyGlass(Handle, App.Glass == "Чистое стекло" || App.Glass == "Лёгкое размытие" ? "Матовое" : App.Glass);
            Invalidate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            tick.Start();
            if (state == "loading") StartTranslate("");
        }

        // Шрифт под текст: для китайского, японского и корейского нужен свой, иначе будут квадратики.
        static Font TextFont(string text, double size)
        {
            string face = "Segoe UI";
            foreach (char c in text)
            {
                if ((c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF)) { face = "Malgun Gothic"; break; }
                if (c >= 0x3040 && c <= 0x30FF) { face = "Yu Gothic UI"; break; }
                if (c >= 0x4E00 && c <= 0x9FFF) face = "Microsoft YaHei UI";
            }
            return Ui.Font(size, false, face);
        }

        static float Measure(string text, Font f, float width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return (float)Math.Ceiling(measure.MeasureString(text, f, (int)width, Ui.Wrap).Height);
        }

        void DoLayout()
        {
            var wa = Screen.FromPoint(anchor).WorkingArea;
            int iw = cardW - pad * 2;
            float y = pad;
            headR = new RectangleF(pad, y, iw - Ui.S(24), Ui.S(18));
            y += Ui.S(26);
            mainText = state == "done" ? result.Text : state == "error" ? message : "Перевожу";
            mainFont = TextFont(mainText, state == "done" ? 17 : 15);
            // длинный перевод не растягивает карточку на весь экран, а прокручивается
            float cap = (int)(wa.Height * 0.45);
            float tw = iw;
            mainFull = Measure(mainText, mainFont, tw);
            scrollable = mainFull > cap;
            if (scrollable)
            {
                tw = iw - Ui.S(16);
                mainFull = Measure(mainText, mainFont, tw);
            }
            float mh = Math.Min(mainFull, cap);
            mainR = new RectangleF(pad, y, tw, mh);
            track = new Rectangle(pad + iw - Ui.S(6), (int)y, Ui.S(6), (int)mh);
            scroll = Math.Max(0, Math.Min(scroll, mainFull - mh));
            y += mh;
            dictR = RectangleF.Empty;
            if (state == "done" && !string.IsNullOrEmpty(result.Dict))
            {
                y += Ui.S(8);
                float dh = Math.Min(Ui.S(80), Measure(result.Dict, Ui.Font(13), iw));
                dictR = new RectangleF(pad, y, iw, dh);
                y += dh;
            }
            origR = RectangleF.Empty;
            if (original.Length > 0 && state != "error")
            {
                y += Ui.S(12);
                origFont = TextFont(original, 12.5);
                float oh = Math.Min(Ui.S(36), Measure(original, origFont, iw));
                origR = new RectangleF(pad, y, iw, oh);
                y += oh;
            }
            buttons.Clear();
            if (state == "done")
            {
                y += Ui.S(14);
                int x = pad;
                var list = new List<Btn> { new Btn { Id = "copy", Text = copied ? "Скопировано ✓" : "Копировать" } };
                if (selection) list.Add(new Btn { Id = "paste", Text = "Вставить вместо" });
                string other = Translator.Same(result.Tl, mod.My) ? mod.Second : mod.My;
                list.Add(new Btn { Id = "swap", Text = "→ " + TranslatorModule.LangName(other), Lang = other });
                foreach (var b in list)
                {
                    int bw = (int)Math.Ceiling(measure.MeasureString(b.Text, Ui.Font(12, true)).Width) + Ui.S(22);
                    b.R = new Rectangle(x, (int)y, bw, Ui.S(30));
                    x += bw + Ui.S(8);
                    buttons.Add(b);
                }
                y += Ui.S(30);
            }
            height = (int)(y + pad);
            closeR = new Rectangle(cardW - Ui.S(38), Ui.S(8), Ui.S(30), Ui.S(28));
        }

        void Place()
        {
            if (moved) return;    // карточку перетащили: остаётся там, куда положили
            var wa = Screen.FromPoint(anchor).WorkingArea;
            int x = anchor.X - Ui.S(30), y = anchor.Y + Ui.S(22);
            if (y + height > wa.Bottom - Ui.S(8)) y = anchor.Y - Ui.S(16) - height;
            x = Math.Max(wa.Left + Ui.S(8), Math.Min(x, wa.Right - cardW - Ui.S(8)));
            y = Math.Max(wa.Top + Ui.S(8), y);
            target = new Point(x, y);
        }

        void Relayout()
        {
            if (IsDisposed) return;
            DoLayout();
            Place();
            if (Height != height) Bounds = new Rectangle(target.X, target.Y, cardW, height);
            Invalidate();
        }

        void StartTranslate(string force)
        {
            seq = Interlocked.Increment(ref seqCounter);
            state = "loading";
            copied = false;
            scroll = 0;
            Relayout();
            Translator.Begin(this, seq, original, mod.My, mod.Second, force, OnResult);
        }

        void OnResult(TrResult r)
        {
            if (IsDisposed || r.Seq != seq) return;
            if (!string.IsNullOrEmpty(r.Error))
            {
                Log.Write("перевод: " + r.Error);
                state = "error";
                message = "Не получилось перевести. Проверь интернет и попробуй ещё раз.";
            }
            else { state = "done"; result = r; }
            Relayout();
        }

        void OnTick(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            double age = (now - opened).TotalMilliseconds;
            // плавно въезжаем на место
            int ny = age < 180 ? (int)(target.Y + Ui.S(10) * Math.Pow(1 - age / 180.0, 3)) : target.Y;
            if (Top != ny || Left != target.X) Location = new Point(target.X, ny);
            if (state == "loading" && (now - lastDots).TotalMilliseconds >= 350) { lastDots = now; Invalidate(); }
            if (copied && (now - copiedAt).TotalSeconds >= 1.5) { copied = false; Relayout(); }
            // щелчок где-то ещё закрывает карточку
            if (age > 350 && dragMouse == null && !scrollDrag && (KeyTools.Down(0x01) || KeyTools.Down(0x02)))
                if (!Bounds.Contains(Cursor.Position)) Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int cw = ClientSize.Width, ch = ClientSize.Height;
            if (cw <= 0 || ch <= 0) return;
            using (var bmp = new Bitmap(cw, ch, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    Ui.Prepare(g);
                    if (noGlass) g.Clear(Ui.Fallback);
                    using (var wash = new SolidBrush(Ui.Wash)) g.FillRectangle(wash, 0, 0, cw, ch);
                    if (WNative.Build < 22000)
                        using (var edge = new Pen(Ui.Edge, 1)) g.DrawRectangle(edge, 0, 0, cw - 1, ch - 1);
                    try { Draw(g); } catch (Exception ex) { Log.Write("перевод paint: " + ex.Message); }
                }
                WNative.BlitArgb(e.Graphics, bmp, 0, 0);
            }
        }

        void Draw(Graphics g)
        {
            string head = state == "done" ? TranslatorModule.LangName(result.Src) + "  →  " + TranslatorModule.LangName(result.Tl) : "Перевод";
            Ui.Text2(g, head.ToUpperInvariant(), Ui.Font(10.5, true), Ui.Accent, headR, Ui.Left);
            using (var xp = new Pen(hover == "close" ? Ui.Text : Ui.Faint, 1.6f * Ui.Scale))
            {
                float cx = closeR.X + closeR.Width / 2f, cy = closeR.Y + closeR.Height / 2f, d = Ui.S(5);
                g.DrawLine(xp, cx - d, cy - d, cx + d, cy + d);
                g.DrawLine(xp, cx - d, cy + d, cx + d, cy - d);
            }
            string main = mainText;
            if (state == "loading") main += new string('.', 1 + (int)((DateTime.Now - opened).TotalMilliseconds / 350) % 3);
            var st = g.Save();
            g.SetClip(mainR);
            float top = mainR.Y - (float)scroll;
            Ui.Text2(g, main, mainFont, state == "done" ? Ui.Text : Ui.Muted, new RectangleF(mainR.X, top, mainR.Width, mainFull + Ui.S(4)), Ui.Wrap, true);
            g.Restore(st);
            if (scrollable)
            {
                Ui.FillRound(g, track, track.Width / 2f, Color.FromArgb(30, 255, 255, 255));
                var th = Thumb();
                Ui.FillRound(g, new RectangleF(track.X, th.Y, track.Width, th.Height), track.Width / 2f,
                    hover == "scroll" || scrollDrag ? Ui.Text : Ui.Faint);
            }
            if (!dictR.IsEmpty) Ui.Text2(g, result.Dict, Ui.Font(13), Ui.Muted, dictR, Ui.Wrap);
            if (!origR.IsEmpty)
            {
                using (var lp = new Pen(Color.FromArgb(40, 255, 255, 255), 1)) g.DrawLine(lp, origR.X, origR.Y - Ui.S(6), origR.Right, origR.Y - Ui.S(6));
                Ui.Text2(g, original, origFont, Ui.Faint, origR, Ui.Wrap);
            }
            foreach (var b in buttons)
            {
                Ui.FillRound(g, b.R, b.R.Height / 2f, Color.FromArgb(hover == b.Id ? 70 : 34, 255, 255, 255));
                Ui.Text2(g, b.Text, Ui.Font(12, true), b.Id == "copy" && copied ? Ui.Accent : Ui.Text, b.R, Ui.Center);
            }
        }

        RectangleF Thumb()
        {
            float mh = mainR.Height;
            float h = Math.Max(Ui.S(28), mh * mh / Math.Max(1, mainFull));
            float range = Math.Max(1, mainFull - mh);
            return new RectangleF(track.X, track.Y + (mh - h) * (float)(scroll / range), track.Width, h);
        }

        bool OnTrack(Point p)
        {
            return scrollable && new Rectangle(track.X - Ui.S(8), track.Y, track.Width + Ui.S(16), track.Height).Contains(p);
        }

        void SetScroll(double v)
        {
            v = Math.Max(0, Math.Min(v, mainFull - mainR.Height));
            if (v != scroll) { scroll = v; Invalidate(); }
        }

        void ScrollToMouse(int my)
        {
            var th = Thumb();
            double k = (my - track.Y - th.Height / 2) / Math.Max(1, mainR.Height - th.Height);
            SetScroll(Math.Max(0, Math.Min(1, k)) * (mainFull - mainR.Height));
        }

        string HitKey(Point p)
        {
            if (closeR.Contains(p)) return "close";
            if (OnTrack(p)) return "scroll";
            foreach (var b in buttons) if (b.R.Contains(p)) return b.Id;
            return "";
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragged = false;
            if (OnTrack(e.Location)) { scrollDrag = true; ScrollToMouse(e.Y); return; }
            if (HitKey(e.Location) != "") return;
            // тянем за любое место карточки, кроме кнопок
            dragMouse = Cursor.Position;
            dragForm = Location;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (scrollDrag) { ScrollToMouse(e.Y); return; }
            if (dragMouse != null)
            {
                var m = Cursor.Position;
                int dx = m.X - dragMouse.Value.X, dy = m.Y - dragMouse.Value.Y;
                if (!dragged && Math.Abs(dx) + Math.Abs(dy) < Ui.S(4)) return;
                dragged = true;
                moved = true;
                opened = DateTime.Now.AddSeconds(-1);    // без анимации въезда
                target = new Point(dragForm.X + dx, dragForm.Y + dy);
                Location = target;
                return;
            }
            string k = HitKey(e.Location);
            Cursor = k != "" ? Cursors.Hand : Cursors.Default;
            if (k != hover) { hover = k; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != "") { hover = ""; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (scrollable) SetScroll(scroll - e.Delta / 120.0 * Ui.S(60));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            bool wasDrag = dragged || scrollDrag;
            scrollDrag = false;
            dragMouse = null;
            dragged = false;
            Cursor = Cursors.Default;
            if (wasDrag) return;
            if (closeR.Contains(e.Location)) { Close(); return; }
            foreach (var b in buttons)
            {
                if (!b.R.Contains(e.Location)) continue;
                switch (b.Id)
                {
                    case "copy": KeyTools.SetClip(result.Text); copied = true; copiedAt = DateTime.Now; Relayout(); break;
                    case "paste": string txt = result.Text; Close(); KeyTools.Paste(txt); break;
                    case "swap": StartTranslate(b.Lang); break;
                }
                return;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tick.Dispose();
            base.Dispose(disposing);
        }
    }
}
