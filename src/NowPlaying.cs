using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Glasswing
{
    // ---------------------------------------------------------------- сейчас играет

    class NowPlayingModule : Module
    {
        NowPlayingCard card;
        bool hidden;
        public override string Id => "np";
        public override string Name => "Сейчас играет";
        public override string Short => "Трек, обложка, кнопки";
        public override string Description => "Что сейчас играет в музыке или браузере, с обложкой и кнопками «назад», «пауза», «вперёд».";
        public override IconKind Icon => IconKind.Music;
        public override bool Running => card != null && !card.IsDisposed;
        public override bool CanReset => true;

        public bool Hidden => hidden;

        public bool HideIdle
        {
            get { return Settings.GetBool(Id, "HideIdle", false); }
            set { Settings.Set(Id, "HideIdle", value); if (Running) card.SyncVisible(); }
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

        public override void Start()
        {
            if (Running) return;
            card = new NowPlayingCard(this);
            card.PlaceFromSettings();
            var h = card.Handle;   // окно создаём сразу: опрос плеера идёт, даже пока карточка спрятана
            if (!hidden && !HideIdle) card.Show();
        }

        public override void Stop()
        {
            if (!Running) return;
            card.Close();
            card.Dispose();
            card = null;
        }

        public override void SetHidden(bool hide) { hidden = hide; if (Running) card.SyncVisible(); }
        public override void GlassChanged() { if (Running) card.ApplyGlass(); }
        public override bool HasGlass => true;
        public override void ResetPosition() { if (Running) card.ResetPosition(); }

        public override IEnumerable<Row> Rows()
        {
            yield return new Row { Label = "Прятать, когда ничего не играет", On = () => HideIdle, Click = () => HideIdle = !HideIdle };
            yield return new Row { Label = "Поверх всех окон", On = () => TopMost, Click = () => TopMost = !TopMost };
            yield return new Row { Label = "Закрепить на месте", On = () => Locked, Click = () => Locked = !Locked };
        }
    }

    // Что известно о текущем треке. Поток плеера присылает копию, карточка рисует свою.
    class NpInfo
    {
        public bool Has, Playing, CanPrev, CanNext, CanPlay, CanSeek, MediaOk = true;
        public string Title = "", Artist = "", Source = "", Key = "";
        public double Length, Start, MinSeek, MaxSeek, Pos;
        public DateTime PosAt = DateTime.MinValue;
        public Bitmap Cover;
        public NpInfo Copy() { return (NpInfo)MemberwiseClone(); }
    }

    class NowPlayingCard : GlassCard
    {
        static readonly Color ColText = Color.FromArgb(255, 255, 255, 255);
        static readonly Color ColMuted = Color.FromArgb(185, 255, 255, 255);
        static readonly Color ColFaint = Color.FromArgb(90, 255, 255, 255);
        static readonly Color ColHot = Color.FromArgb(40, 255, 255, 255);
        static readonly Color ColTrack = Color.FromArgb(60, 255, 255, 255);
        static readonly Color ColCard = Color.FromArgb(40, 255, 255, 255);
        static readonly Color ColDark = Color.FromArgb(255, 24, 26, 34);
        static readonly StringFormat CenterFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        static readonly StringFormat TimeLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        static readonly StringFormat TimeRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        const string Note = "♪";

        readonly NowPlayingModule np;
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly int cardW, cardH, pad, coverSz, textX, colW, barY, btnY, btnCx;
        readonly string[] btnName = { "prev", "play", "next" };
        readonly int[] btnX, btnR;
        NpMedia media;
        NpInfo info = new NpInfo();

        bool seeking, hoverBar, hoverCard;
        double seekFrac;
        string hotBtn, downBtn;
        DateTime seekAt = DateTime.MinValue;

        public NowPlayingCard(NowPlayingModule m) : base(m)
        {
            np = m;
            cardW = Ui.S(360);
            cardH = Ui.S(114);
            pad = Ui.S(12);
            coverSz = cardH - 2 * pad;
            textX = pad + coverSz + Ui.S(14);
            colW = cardW - textX - pad;
            barY = pad + Ui.S(47);          // середина полоски прогресса
            btnY = cardH - pad - Ui.S(16);
            btnCx = textX + (int)Math.Round(colW / 2.0);
            btnX = new[] { btnCx - Ui.S(44), btnCx, btnCx + Ui.S(44) };
            btnR = new[] { Ui.S(15), Ui.S(16), Ui.S(15) };
            tick.Tick += (s, e) => { if (info.Has && info.Playing && info.Length > 1) Invalidate(); };
        }

        // Появляясь, когда включили музыку, карточка не отнимает фокус у активного окна.
        protected override bool ShowWithoutActivation => true;

        protected override Size CardSize() { return new Size(cardW, cardH); }

        protected override Point DefaultLocation(Rectangle wa)
        {
            return new Point(wa.Right - cardW - Ui.S(40), wa.Top + Ui.S(24));
        }

        public void SyncVisible()
        {
            bool want = !np.Hidden && !(np.HideIdle && !info.Has);
            if (Visible != want) Visible = want;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (media != null) return;
            media = new NpMedia(coverSz, Deliver);
            media.Start();
            tick.Start();
        }

        void Deliver(NpInfo i)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => Apply(i))); }
            catch (InvalidOperationException) { }
        }

        void Apply(NpInfo i)
        {
            if (IsDisposed) return;
            // сразу после перемотки плеер ещё отдаёт старое время: не даём полоске прыгнуть назад
            if ((DateTime.UtcNow - seekAt).TotalSeconds <= 2.5) { i.Pos = info.Pos; i.PosAt = info.PosAt; }
            var old = info.Cover;
            info = i;
            if (old != null && !ReferenceEquals(old, i.Cover)) old.Dispose();
            if (!info.Has) { hotBtn = null; hoverBar = false; }
            SyncVisible();
            Invalidate();
        }

        // ---------------------------------------------------------------- мышь

        bool TestBar(Point pt)
        {
            return info.Has && info.Length > 1 && info.CanSeek &&
                pt.X >= textX - Ui.S(4) && pt.X <= cardW - pad + Ui.S(4) &&
                pt.Y >= barY - Ui.S(8) && pt.Y <= barY + Ui.S(8);
        }

        double BarFrac(Point pt)
        {
            return Math.Max(0.0, Math.Min(1.0, (pt.X - textX) / (double)(cardW - textX - pad)));
        }

        string ButtonHit(Point pt)
        {
            for (int i = 0; i < btnName.Length; i++)
            {
                int dx = pt.X - btnX[i], dy = pt.Y - btnY, r = btnR[i] + Ui.S(3);
                if (dx * dx + dy * dy <= r * r) return btnName[i];
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (seeking) { seekFrac = BarFrac(e.Location); Invalidate(); return; }
            string hot = ButtonHit(e.Location);
            bool bar = TestBar(e.Location);
            if (hot != hotBtn || bar != hoverBar || !hoverCard)
            {
                hotBtn = hot;
                hoverBar = bar;
                hoverCard = true;
                Cursor = hot != null || bar ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (seeking) return;
            hotBtn = null;
            hoverBar = false;
            hoverCard = false;
            Invalidate();
        }

        protected override bool HandleMouseDown(Point p)
        {
            if (TestBar(p))
            {
                seeking = true;
                seekFrac = BarFrac(p);
                Capture = true;
                Invalidate();
                return true;
            }
            string btn = ButtonHit(p);
            if (btn != null) { downBtn = btn; return true; }
            return false;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (seeking)
            {
                seeking = false;
                Capture = false;
                // берём место, запомненное при нажатии и движении: координаты в MouseUp после захвата мыши ненадёжны
                SeekTo(seekFrac);
                return;
            }
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            string btn = ButtonHit(e.Location);
            if (btn != null && btn == downBtn && info.Has)
            {
                media.Command(btn);
                Invalidate();
            }
            downBtn = null;
        }

        // Захват мыши отняли посреди перемотки (Alt+Tab и т. п.): просто отпускаем полоску.
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (seeking && !Capture) { seeking = false; Invalidate(); }
        }

        void SeekTo(double frac)
        {
            if (!info.Has || info.Length <= 0) return;
            // не уходим в самый конец трека: иначе плеер считает его доигранным и включает следующий
            double target = info.Length * Math.Max(0.0, Math.Min(1.0, frac));
            target = Math.Min(target, info.Length - 2);
            double abs = info.Start + target;
            if (info.MaxSeek > info.MinSeek) abs = Math.Max(info.MinSeek, Math.Min(info.MaxSeek, abs));
            info.Pos = abs - info.Start;
            info.PosAt = DateTime.UtcNow;
            seekAt = DateTime.UtcNow;
            media.Seek(abs);
            Invalidate();
        }

        double Position()
        {
            if (info.Length <= 0) return 0;
            double pos = info.Pos;
            if (info.Playing && info.PosAt.Year > 2000) pos += (DateTime.UtcNow - info.PosAt).TotalSeconds;
            return Math.Max(0.0, Math.Min(info.Length, pos));
        }

        static string FormatTime(double sec)
        {
            var t = TimeSpan.FromSeconds(Math.Floor(Math.Max(0, sec)));
            if (t.TotalHours >= 1) return string.Format("{0}:{1:00}:{2:00}", (int)Math.Floor(t.TotalHours), t.Minutes, t.Seconds);
            return string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        // ---------------------------------------------------------------- отрисовка

        static Color Hsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
            double r, g, b;
            switch ((int)Math.Floor(h / 60) % 6)
            {
                case 0: r = c; g = x; b = 0; break;
                case 1: r = x; g = c; b = 0; break;
                case 2: r = 0; g = c; b = x; break;
                case 3: r = 0; g = x; b = c; break;
                case 4: r = x; g = 0; b = c; break;
                default: r = c; g = 0; b = x; break;
            }
            return Color.FromArgb(255, (int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
        }

        static void Triangle(Graphics g, Brush brush, float x, float cy, float size, bool right)
        {
            float h = size, wd = size * 0.9f;
            var pts = right
                ? new[] { new PointF(x, cy - h / 2), new PointF(x + wd, cy), new PointF(x, cy + h / 2) }
                : new[] { new PointF(x + wd, cy - h / 2), new PointF(x, cy), new PointF(x + wd, cy + h / 2) };
            g.FillPolygon(brush, pts);
        }

        protected override void DrawContent(Graphics g, int w, int h)
        {
            // обложка
            var cover = new RectangleF(pad, pad, coverSz, coverSz);
            float radius = Ui.S(10);
            var font = Ui.Font(30, false, "Segoe UI Symbol");
            if (info.Cover != null)
            {
                using (var path = Ui.Round(cover, radius))
                using (var tb = new TextureBrush(info.Cover))
                {
                    tb.TranslateTransform(pad, pad);
                    g.FillPath(tb, path);
                }
            }
            else if (info.Has)
            {
                // плеер не дал обложку: цветная заглушка, свой цвет у каждого трека
                int hue = (int)(Math.Abs((long)info.Key.GetHashCode()) % 360);
                using (var lg = new LinearGradientBrush(cover, Hsv(hue, 0.55, 0.85), Hsv((hue + 50) % 360, 0.7, 0.45), 45f))
                using (var path = Ui.Round(cover, radius))
                    g.FillPath(lg, path);
                using (var sh = new SolidBrush(Ui.Shadow))
                    g.DrawString(Note, font, sh, new RectangleF(cover.X, cover.Y + Ui.S(1.5), cover.Width, cover.Height), CenterFormat);
                using (var nb = new SolidBrush(ColText)) g.DrawString(Note, font, nb, cover, CenterFormat);
            }
            else
            {
                Ui.FillRound(g, cover, radius, ColCard);
                using (var nb = new SolidBrush(ColMuted)) g.DrawString(Note, font, nb, cover, CenterFormat);
            }

            // строки текста
            float cw = w - textX - pad;
            string title, sub;
            if (info.Has)
            {
                title = info.Title.Length > 0 ? info.Title : "Без названия";
                sub = info.Artist;
                if (info.Source.Length > 0) sub = sub.Length > 0 ? sub + "  ·  " + info.Source : info.Source;
            }
            else
            {
                title = "Ничего не играет";
                sub = info.MediaOk ? "Включите музыку в любом плеере" : "Нужна Windows 10 (1809) или новее";
            }
            Ui.Text2(g, title, Ui.Font(14, true), ColText, new RectangleF(textX, pad - Ui.S(2), cw, Ui.S(22)), Ui.Left, true);
            Ui.Text2(g, sub, Ui.Font(12.5), ColMuted, new RectangleF(textX, pad + Ui.S(19), cw, Ui.S(19)), Ui.Left, true);

            // полоска прогресса (если плеер сообщает длину трека); её можно тянуть мышкой
            if (info.Has && info.Length > 1)
            {
                bool big = seeking || (hoverBar && info.CanSeek);
                float barH = big ? Ui.S(5) : Ui.S(3);
                float by = barY - barH / 2;
                Ui.FillRound(g, new RectangleF(textX, by, cw, barH), barH / 2, ColTrack);
                double frac = seeking ? seekFrac : Position() / info.Length;
                if (frac > 0) Ui.FillRound(g, new RectangleF(textX, by, (float)Math.Max(barH, cw * frac), barH), barH / 2, ColText);
                if (big)
                {
                    float kr = Ui.S(6.5);
                    float kx = (float)(textX + cw * frac);
                    using (var ks = new SolidBrush(Ui.Shadow)) g.FillEllipse(ks, kx - kr, barY - kr + Ui.S(1), 2 * kr, 2 * kr);
                    using (var kb = new SolidBrush(ColText)) g.FillEllipse(kb, kx - kr, barY - kr, 2 * kr, 2 * kr);
                }
                // время: сколько прошло слева, длина трека справа
                var timeBox = new RectangleF(textX, btnY - Ui.S(9), cw, Ui.S(18));
                var tf = Ui.Font(10, true);
                using (var mb = new SolidBrush(ColMuted))
                {
                    g.DrawString(FormatTime(frac * info.Length), tf, mb, timeBox, TimeLeft);
                    g.DrawString(FormatTime(info.Length), tf, mb, timeBox, TimeRight);
                }
            }
            DrawButtons(g);
        }

        void DrawButtons(Graphics g)
        {
            for (int i = 0; i < btnName.Length; i++)
            {
                string name = btnName[i];
                bool enabled = info.Has && (name == "prev" ? info.CanPrev : name == "next" ? info.CanNext : info.CanPlay);
                float cx = btnX[i], cy = btnY, r = btnR[i];
                if (name == "play")
                {
                    // белый круг с тёмным значком
                    var fill = enabled ? (hotBtn == "play" ? ColText : Color.FromArgb(235, 255, 255, 255)) : ColFaint;
                    using (var circle = new SolidBrush(fill)) g.FillEllipse(circle, cx - r, cy - r, 2 * r, 2 * r);
                    if (info.Playing)
                    {
                        float bw = Ui.S(3.5), bh = Ui.S(13), gap = Ui.S(3);
                        Ui.FillRound(g, new RectangleF(cx - gap / 2 - bw, cy - bh / 2, bw, bh), Ui.S(1), ColDark);
                        Ui.FillRound(g, new RectangleF(cx + gap / 2, cy - bh / 2, bw, bh), Ui.S(1), ColDark);
                    }
                    else
                    {
                        float sz = Ui.S(13);
                        using (var ink = new SolidBrush(ColDark)) Triangle(g, ink, cx - sz * 0.35f, cy, sz, true);
                    }
                }
                else
                {
                    if (enabled && hotBtn == name)
                        using (var hot = new SolidBrush(ColHot)) g.FillEllipse(hot, cx - r, cy - r, 2 * r, 2 * r);
                    using (var ink = new SolidBrush(enabled ? ColText : ColFaint))
                    {
                        float sz = Ui.S(11), bar = Ui.S(2.5);
                        if (name == "next")
                        {
                            Triangle(g, ink, cx - sz * 0.6f, cy, sz, true);
                            g.FillRectangle(ink, cx + sz * 0.35f, cy - sz / 2, bar, sz);
                        }
                        else
                        {
                            Triangle(g, ink, cx - sz * 0.3f, cy, sz, false);
                            g.FillRectangle(ink, cx - sz * 0.35f - bar, cy - sz / 2, bar, sz);
                        }
                    }
                }
            }
        }

        protected override void AddMenuItems(ContextMenuStrip menu)
        {
            menu.Items.Add(new ToolStripMenuItem("Прятать, когда ничего не играет", null, (s, e) => np.HideIdle = !np.HideIdle) { Checked = np.HideIdle });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (media != null) media.Stop();
                tick.Dispose();
                if (info.Cover != null) { info.Cover.Dispose(); info.Cover = null; }
            }
            base.Dispose(disposing);
        }
    }

    // Плееры сообщают Windows, что играет (это же видно в регуляторе громкости).
    // Читаем через WinRT GlobalSystemMediaTransportControlsSessionManager. Метаданных WinRT при сборке нет,
    // поэтому типы ищем по имени во время работы, а IAsyncOperation ждём через AsTask из System.Runtime.WindowsRuntime.
    // Все вызовы идут в своём потоке, окно ими не тормозится.
    sealed class NpMedia
    {
        static readonly object sync = new object();
        static bool loaded, ok;
        static Type tManager, tSession, tProps, tInfo, tControls, tTimeline, tStreamRef, tStream, tInput, tReader;
        static MethodInfo asTask, asStream;

        readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        readonly Action<NpInfo> publish;
        readonly int coverSz;
        readonly NpInfo w = new NpInfo();
        object manager, session;
        int coverTries;
        DateTime seekAt = DateTime.MinValue, nextPoll = DateTime.MinValue;
        volatile bool stopped;

        public NpMedia(int coverSize, Action<NpInfo> publish)
        {
            coverSz = coverSize;
            this.publish = publish;
        }

        static Type WinRt(string name, string asm)
        {
            return Type.GetType(name + ", " + asm + ", ContentType=WindowsRuntime", false);
        }

        static bool Load()
        {
            lock (sync)
            {
                if (loaded) return ok;
                loaded = true;
                try
                {
                    const string mc = "Windows.Media.Control";
                    tManager = WinRt(mc + ".GlobalSystemMediaTransportControlsSessionManager", mc);
                    tSession = WinRt(mc + ".GlobalSystemMediaTransportControlsSession", mc);
                    tProps = WinRt(mc + ".GlobalSystemMediaTransportControlsSessionMediaProperties", mc);
                    tInfo = WinRt(mc + ".GlobalSystemMediaTransportControlsSessionPlaybackInfo", mc);
                    tControls = WinRt(mc + ".GlobalSystemMediaTransportControlsSessionPlaybackControls", mc);
                    tTimeline = WinRt(mc + ".GlobalSystemMediaTransportControlsSessionTimelineProperties", mc);
                    tStreamRef = WinRt("Windows.Storage.Streams.IRandomAccessStreamReference", "Windows.Storage.Streams");
                    tStream = WinRt("Windows.Storage.Streams.IRandomAccessStream", "Windows.Storage.Streams");
                    tInput = WinRt("Windows.Storage.Streams.IInputStream", "Windows.Storage.Streams");
                    tReader = WinRt("Windows.Storage.Streams.DataReader", "Windows.Storage.Streams");
                    var rt = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                    var ext = rt.GetType("System.WindowsRuntimeSystemExtensions");
                    asTask = ext == null ? null : ext.GetMethods().FirstOrDefault(m => m.Name == "AsTask" && m.IsGenericMethodDefinition &&
                        m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");
                    var sext = rt.GetType("System.IO.WindowsRuntimeStreamExtensions");
                    if (sext != null && tInput != null) asStream = sext.GetMethod("AsStreamForRead", new[] { tInput });
                    ok = tManager != null && tSession != null && tProps != null && tInfo != null && tControls != null && tTimeline != null && asTask != null;
                }
                catch (Exception ex) { Log.Write("np: WinRT недоступен: " + Why(ex)); ok = false; }
                if (!ok) Log.Write("np: эта версия Windows не сообщает, какая музыка играет");
                return ok;
            }
        }

        static string Why(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            if (ex is AggregateException && ex.InnerException != null) ex = ex.InnerException;
            return ex.Message;
        }

        // ---------------------------------------------------------------- отражение поверх WinRT

        static object Await(object op, Type result, int ms)
        {
            if (op == null) return null;
            var task = (Task)asTask.MakeGenericMethod(result).Invoke(null, new[] { op });
            if (!task.Wait(ms)) return null;
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        static object CallAsync(object target, Type t, string name, object[] args, int ms)
        {
            var m = args == null ? t.GetMethod(name, Type.EmptyTypes) : t.GetMethod(name);
            var op = m.Invoke(target, args);
            return Await(op, m.ReturnType.GetGenericArguments()[0], ms);
        }

        static object Call(object target, Type t, string name, params object[] args)
        {
            return t.GetMethod(name).Invoke(target, args);
        }

        static object Prop(object target, Type t, string name)
        {
            return t.GetProperty(name).GetValue(target, null);
        }

        static string Str(object o) { return o as string ?? ""; }
        static double Sec(object o) { return o is TimeSpan ? ((TimeSpan)o).TotalSeconds : 0; }

        // ---------------------------------------------------------------- поток

        public void Start()
        {
            var t = new Thread(Loop) { IsBackground = true, Name = "np media" };
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
        }

        public void Stop()
        {
            stopped = true;
            try { queue.CompleteAdding(); } catch { }
        }

        void Post(Action a)
        {
            if (stopped) return;
            try { queue.Add(a); } catch (InvalidOperationException) { }
        }

        public void Command(string cmd) { Post(() => DoCommand(cmd)); }
        public void Seek(double abs) { Post(() => DoSeek(abs)); }

        void Loop()
        {
            w.MediaOk = Load();
            while (!stopped)
            {
                int wait = (int)Math.Max(0, Math.Min(1000, (nextPoll - DateTime.UtcNow).TotalMilliseconds));
                Action a;
                bool got;
                try { got = queue.TryTake(out a, wait); }
                catch (Exception) { return; }
                if (stopped) return;
                if (got)
                {
                    try { a(); } catch (Exception ex) { Log.Write("np: " + Why(ex)); }
                    continue;
                }
                nextPoll = DateTime.UtcNow.AddMilliseconds(1000);
                try { Poll(); } catch (Exception ex) { Log.Write("np опрос: " + Why(ex)); }
                if (!stopped) publish(w.Copy());
            }
        }

        object Manager()
        {
            if (!w.MediaOk) return null;
            if (manager == null)
            {
                try { manager = CallAsync(null, tManager, "RequestAsync", null, 3000); }
                catch { manager = null; }
            }
            return manager;
        }

        void Poll()
        {
            var mgr = Manager();
            object s = null;
            if (mgr != null) { try { s = Call(mgr, tManager, "GetCurrentSession"); } catch { s = null; } }
            if (s == null)
            {
                session = null;
                if (w.Has) { w.Has = false; w.Key = ""; w.Cover = null; }
                return;
            }
            session = s;
            w.Has = true;
            try
            {
                var p = CallAsync(s, tSession, "TryGetMediaPropertiesAsync", null, 800);
                var pi = Call(s, tSession, "GetPlaybackInfo");
                w.Playing = Convert.ToString(Prop(pi, tInfo, "PlaybackStatus")) == "Playing";
                var c = Prop(pi, tInfo, "Controls");
                Func<string, bool> can = n => Prop(c, tControls, n) as bool? ?? false;
                w.CanPrev = can("IsPreviousEnabled");
                w.CanNext = can("IsNextEnabled");
                w.CanSeek = can("IsPlaybackPositionEnabled");
                w.CanPlay = can("IsPlayPauseToggleEnabled") || can("IsPlayEnabled") || can("IsPauseEnabled");
                string app = Str(Prop(s, tSession, "SourceAppUserModelId"));
                w.Source = SourceName(app);
                var tl = Call(s, tSession, "GetTimelineProperties");
                w.Start = Sec(Prop(tl, tTimeline, "StartTime"));
                w.MinSeek = Sec(Prop(tl, tTimeline, "MinSeekTime"));
                w.MaxSeek = Sec(Prop(tl, tTimeline, "MaxSeekTime"));
                w.Length = Sec(Prop(tl, tTimeline, "EndTime")) - w.Start;
                // сразу после перемотки плеер ещё отдаёт старое время: не даём полоске прыгнуть назад
                if ((DateTime.UtcNow - seekAt).TotalSeconds > 2.5)
                {
                    w.Pos = Sec(Prop(tl, tTimeline, "Position")) - w.Start;
                    var upd = Prop(tl, tTimeline, "LastUpdatedTime");
                    w.PosAt = upd is DateTimeOffset ? ((DateTimeOffset)upd).UtcDateTime : DateTime.MinValue;
                }
                if (p != null)
                {
                    w.Title = Str(Prop(p, tProps, "Title"));
                    string artist = Str(Prop(p, tProps, "Artist"));
                    w.Artist = artist.Length > 0 ? artist : Str(Prop(p, tProps, "AlbumTitle"));
                    string key = w.Title + "|" + w.Artist + "|" + app;
                    if (key != w.Key) { w.Key = key; coverTries = 0; w.Cover = null; }
                    // браузеры иногда отдают обложку позже названия: пробуем ещё несколько раз
                    var thumb = Prop(p, tProps, "Thumbnail");
                    if (w.Cover == null && coverTries < 5 && thumb != null)
                    {
                        coverTries++;
                        w.Cover = ReadCover(thumb);
                    }
                }
            }
            catch { }
        }

        void DoCommand(string cmd)
        {
            var s = session;
            if (s == null) return;
            try
            {
                string name = cmd == "prev" ? "TrySkipPreviousAsync" : cmd == "next" ? "TrySkipNextAsync" : "TryTogglePlayPauseAsync";
                CallAsync(s, tSession, name, null, 1000);
                if (cmd == "play") w.Playing = !w.Playing;
            }
            catch { }
            publish(w.Copy());
            nextPoll = DateTime.UtcNow.AddMilliseconds(350);   // быстрое обновление после нажатия
        }

        void DoSeek(double abs)
        {
            var s = session;
            if (s == null || w.Length <= 0) return;
            try { CallAsync(s, tSession, "TryChangePlaybackPositionAsync", new object[] { (long)Math.Round(abs * 10000000) }, 1000); }
            catch (Exception ex) { Log.Write("np перемотка: ошибка " + Why(ex)); }
            w.Pos = abs - w.Start;
            w.PosAt = DateTime.UtcNow;
            seekAt = DateTime.UtcNow;
            publish(w.Copy());
        }

        static string SourceName(string id)
        {
            string l = id.ToLowerInvariant();
            if (l.Contains("spotify")) return "Spotify";
            if (l.Contains("yandex")) return "Яндекс";
            if (l.Contains("chrome")) return "Chrome";
            if (l.Contains("msedge")) return "Edge";
            if (l.Contains("firefox")) return "Firefox";
            if (l.Contains("opera")) return "Opera";
            if (l.Contains("telegram")) return "Telegram";
            if (l.Contains("vk")) return "VK";
            int mi = l.IndexOf("media", StringComparison.Ordinal);
            if (l.Contains("zunemusic") || (mi >= 0 && l.IndexOf("player", mi + 5, StringComparison.Ordinal) >= 0)) return "Медиаплеер";
            if (l.Contains("aimp")) return "AIMP";
            if (l.Contains("foobar")) return "foobar2000";
            string[] parts = id.Split('!', '\\');
            string n = parts[parts.Length - 1];
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            if (n.Length > 18) n = n.Substring(0, 18);
            return n;
        }

        // Обложка трека, обрезанная в квадрат по центру (у видео обложки широкие)
        Bitmap ReadCover(object reference)
        {
            object stream = null;
            try
            {
                stream = CallAsync(reference, tStreamRef, "OpenReadAsync", null, 1500);
                if (stream == null) return null;
                using (var ms = new MemoryStream())
                {
                    bool done = false;
                    if (asStream != null)
                    {
                        try
                        {
                            using (var net = (Stream)asStream.Invoke(null, new[] { stream })) net.CopyTo(ms);
                            done = true;
                        }
                        catch { ms.SetLength(0); }
                    }
                    if (!done && tStream != null && tReader != null)
                    {
                        // запасной путь: читаем байты через DataReader
                        uint size = (uint)Convert.ToUInt64(Prop(stream, tStream, "Size"));
                        var input = Call(stream, tStream, "GetInputStreamAt", (ulong)0);
                        var reader = tReader.GetConstructor(new[] { tInput }).Invoke(new[] { input });
                        var load = tReader.GetMethod("LoadAsync").Invoke(reader, new object[] { size });
                        Await(load, typeof(uint), 1500);
                        var bytes = new byte[size];
                        tReader.GetMethod("ReadBytes").Invoke(reader, new object[] { bytes });
                        ms.Write(bytes, 0, bytes.Length);
                        var rd = reader as IDisposable;
                        if (rd != null) try { rd.Dispose(); } catch { }
                    }
                    if (ms.Length == 0) return null;
                    ms.Position = 0;
                    using (var src = Image.FromStream(ms))
                    {
                        int side = Math.Min(src.Width, src.Height);
                        var outBmp = new Bitmap(coverSz, coverSz, PixelFormat.Format32bppPArgb);
                        using (var gr = Graphics.FromImage(outBmp))
                        {
                            gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            var srcRect = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
                            gr.DrawImage(src, new Rectangle(0, 0, coverSz, coverSz), srcRect, GraphicsUnit.Pixel);
                        }
                        return outBmp;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("np обложка: " + Why(ex));
                return null;
            }
            finally
            {
                var d = stream as IDisposable;
                if (d != null) try { d.Dispose(); } catch { }
            }
        }
    }
}
