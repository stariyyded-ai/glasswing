using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Glasswing
{
    // Центр управления: слева все виджеты с переключателями, справа настройки выбранного.
    class ControlCenter : Form
    {
        static readonly int BaseW = 1040, BaseH = 700;
        readonly Hits hits = new Hits();
        Module sel;
        string hover;
        bool noGlass;
        CenterTheme T = CenterTheme.Current;
        public bool AllowClose;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        public ControlCenter()
        {
            Text = App.Name;
            Icon = App.AppIcon;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            KeyPreview = true;
            ClientSize = new Size(Ui.S(BaseW), Ui.S(BaseH));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            sel = App.Core.Modules[0];
            SystemEvents.UserPreferenceChanged += PrefChanged;
        }

        // «Как в Windows»: Windows сменила режим приложений, перекрашиваемся
        void PrefChanged(object s, UserPreferenceChangedEventArgs e)
        {
            if (CenterTheme.Choice != CenterTheme.Names[2] || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => { if (CenterTheme.Current != T) ApplyTheme(); })); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            SystemEvents.UserPreferenceChanged -= PrefChanged;   // событие статическое: иначе окно не освободится
            base.Dispose(disposing);
        }

        void SetTheme(string name)
        {
            CenterTheme.Choice = name;
            ApplyTheme();
        }

        // Цвета, тонировка акрила и тёмная/светлая рамка окна по текущей теме.
        void ApplyTheme()
        {
            if (IsDisposed) return;
            var t = CenterTheme.Current;
            T = t;
            if (IsHandleCreated)
            {
                WNative.RoundDark(Handle);
                int dark = t.IsDark ? 1 : 0, border = t.Border;
                try
                {
                    DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                    DwmSetWindowAttribute(Handle, 34, ref border, 4);
                }
                catch { }
                noGlass = !WNative.SetAcrylic(Handle, 4, t.TintA, t.TintR, t.TintG, t.TintB);
            }
            Invalidate();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000 | 0x00080000;   // MINIMIZEBOX | SYSMENU: сворачивается с панели задач
                return cp;
            }
        }

        // Часть виджета (вкладка) открывает своего родителя на этой вкладке.
        public void Select(Module m)
        {
            if (m != null && m.Parent != null)
            {
                int i = Array.IndexOf(m.Parent.Tabs, m);
                if (i >= 0) m.Parent.TabIndex = i;
                m = m.Parent;
            }
            if (m != null) sel = m;
            Invalidate();
        }

        // акрил моргает при переносе окна: на это время простая тонировка (см. Ui.GlassWhileMoving)
        protected override void WndProc(ref Message m)
        {
            if (!noGlass && m.Msg == Ui.WM_ENTERSIZEMOVE) WNative.SetAcrylic(Handle, 2, T.MoveA, T.TintR, T.TintG, T.TintB);
            base.WndProc(ref m);
            if (!noGlass && m.Msg == Ui.WM_EXITSIZEMOVE) WNative.SetAcrylic(Handle, 4, T.TintA, T.TintR, T.TintG, T.TintB);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTheme();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Hide();
        }

        int S(double v) { return Ui.S(v); }

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
                    if (noGlass) g.Clear(T.Fallback);
                    using (var wash = new SolidBrush(T.Wash)) g.FillRectangle(wash, 0, 0, cw, ch);
                    if (WNative.Build < 22000)
                        using (var edge = new Pen(T.Edge, 1)) g.DrawRectangle(edge, 0, 0, cw - 1, ch - 1);
                    try
                    {
                        DrawTitle(g, cw);
                        DrawSidebar(g, ch);
                        DrawDetails(g, cw, ch);
                    }
                    catch (Exception ex) { Log.Write("центр: " + ex.Message); }
                }
                WNative.BlitArgb(e.Graphics, bmp, 0, 0);
            }
        }

        void Line(Graphics g, float x1, float y1, float x2, float y2)
        {
            using (var p = new Pen(T.Line, 1)) g.DrawLine(p, x1, y1, x2, y2);
        }

        void DrawTitle(Graphics g, int cw)
        {
            Ui.Logo(g, new RectangleF(S(16), S(12), S(26), S(26)));
            Ui.Text2(g, App.Name, Ui.Font(15, true), T.Text, new RectangleF(S(50), 0, S(120), S(48)), Ui.Left);
            float nameW = g.MeasureString(App.Name, Ui.Font(15, true)).Width;
            Ui.Text2(g, "Центр управления", Ui.Font(12), T.Faint, new RectangleF(S(50) + nameW + S(6), 0, S(200), S(48)), Ui.Left);
            Line(g, 0, S(48), cw, S(48));

            var min = new Rectangle(cw - S(92), S(4), S(46), S(40));
            var close = new Rectangle(cw - S(46), S(4), S(46), S(40));
            if (hover == "min") Ui.FillRound(g, min, S(6), T.BtnHover);
            if (hover == "close") Ui.FillRound(g, close, S(6), Color.FromArgb(200, 196, 43, 28));
            using (var p = new Pen(T.Glyph, Math.Max(1f, Ui.Scale)))
                g.DrawLine(p, min.X + S(17), min.Y + S(20), min.X + S(29), min.Y + S(20));
            // на красной подложке крестик всегда белый
            using (var p = new Pen(hover == "close" && !T.IsDark ? Color.White : T.Glyph, Math.Max(1f, Ui.Scale)))
            {
                g.DrawLine(p, close.X + S(17), close.Y + S(14), close.X + S(29), close.Y + S(26));
                g.DrawLine(p, close.X + S(29), close.Y + S(14), close.X + S(17), close.Y + S(26));
            }
            hits.Add(min, "min", () => WindowState = FormWindowState.Minimized);
            hits.Add(close, "close", Hide);
        }

        void DrawIcon(Graphics g, IconKind kind, RectangleF r)
        {
            var st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            using (var p = new Pen(T.Icon, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (kind)
                {
                    case IconKind.Clock:
                        g.DrawEllipse(p, 3.5f, 3.5f, 17, 17);
                        g.DrawLines(p, new[] { new PointF(12, 7.5f), new PointF(12, 12), new PointF(15, 14) });
                        break;
                    case IconKind.Monitor:
                        g.DrawLines(p, new[] { new PointF(3, 16), new PointF(7, 11), new PointF(11, 14), new PointF(15, 7), new PointF(21, 16) });
                        break;
                    case IconKind.Music:
                        g.DrawLines(p, new[] { new PointF(9, 17), new PointF(9, 5), new PointF(19, 3), new PointF(19, 15) });
                        g.DrawEllipse(p, 4, 14.5f, 5, 5);
                        g.DrawEllipse(p, 14, 12.5f, 5, 5);
                        break;
                    case IconKind.Fox:
                        g.DrawPolygon(p, new[] { new PointF(4, 4), new PointF(8, 10), new PointF(16, 10), new PointF(20, 4), new PointF(20, 13), new PointF(12, 21), new PointF(4, 13) });
                        using (var b = new SolidBrush(T.Icon)) { g.FillEllipse(b, 8.5f, 12, 2, 2); g.FillEllipse(b, 13.5f, 12, 2, 2); }
                        break;
                    case IconKind.Translate:
                        g.DrawLine(p, 3, 5, 12, 5);
                        g.DrawLine(p, 7.5f, 3, 7.5f, 5);
                        g.DrawBezier(p, 5, 5, 6, 9, 8.5f, 11.5f, 11.5f, 13);
                        g.DrawBezier(p, 10, 5, 9, 9, 6.5f, 11.5f, 3.5f, 13);
                        g.DrawLines(p, new[] { new PointF(13, 21), new PointF(17, 11), new PointF(21, 21) });
                        g.DrawLine(p, 14.5f, 17.5f, 19.5f, 17.5f);
                        break;
                    case IconKind.Games:
                        using (var body = Ui.Round(new RectangleF(2.5f, 7, 19, 11), 5.5f)) g.DrawPath(p, body);
                        g.DrawLine(p, 6, 12.5f, 10, 12.5f);
                        g.DrawLine(p, 8, 10.5f, 8, 14.5f);
                        using (var b = new SolidBrush(T.Icon)) { g.FillEllipse(b, 14.2f, 9.8f, 2.4f, 2.4f); g.FillEllipse(b, 16.6f, 12.6f, 2.4f, 2.4f); }
                        break;
                    case IconKind.Overlay:
                        foreach (var c in new[] { new PointF(3.5f, 3.5f), new PointF(13.5f, 3.5f), new PointF(3.5f, 13.5f), new PointF(13.5f, 13.5f) })
                            using (var sq = Ui.Round(new RectangleF(c.X, c.Y, 7, 7), 2)) g.DrawPath(p, sq);
                        break;
                    case IconKind.Folder:
                        using (var fp = new GraphicsPath())
                        {
                            fp.AddLines(new[] { new PointF(3, 18), new PointF(3, 6.5f), new PointF(4.5f, 5), new PointF(9, 5), new PointF(11, 7),
                                new PointF(19.5f, 7), new PointF(21, 8.5f), new PointF(21, 17.5f), new PointF(19.5f, 19), new PointF(4.5f, 19) });
                            fp.CloseFigure();
                            g.DrawPath(p, fp);
                        }
                        break;
                    case IconKind.Weather:
                        g.DrawArc(p, 5, 5, 7, 7, 128, 218);
                        g.DrawLine(p, 3.2f, 8.5f, 1.8f, 8.5f);
                        g.DrawLine(p, 4.8f, 4.8f, 3.8f, 3.8f);
                        g.DrawLine(p, 8.5f, 3.2f, 8.5f, 1.8f);
                        g.DrawLine(p, 12.2f, 4.8f, 13.2f, 3.8f);
                        using (var c = new GraphicsPath())
                        {
                            c.AddLine(7, 19, 16.5f, 19);
                            c.AddArc(13, 12, 7, 7, 90, -180);
                            c.AddArc(6.5f, 8, 10, 10, 348.7f, -158.2f);
                            c.AddArc(3.5f, 12, 7, 7, 260, -170);
                            c.CloseFigure();
                            g.DrawPath(p, c);
                        }
                        break;
                    case IconKind.Todo:
                        g.DrawLines(p, new[] { new PointF(3.5f, 7.5f), new PointF(5.5f, 9.5f), new PointF(9, 5.5f) });
                        g.DrawLine(p, 12.5f, 7.5f, 20.5f, 7.5f);
                        g.DrawEllipse(p, 4, 14, 5, 5);
                        g.DrawLine(p, 12.5f, 16.5f, 20.5f, 16.5f);
                        break;
                }
            }
            g.Restore(st);
        }

        void DrawSidebar(Graphics g, int ch)
        {
            int sw = S(300);
            Line(g, sw, S(48), sw, ch);
            Ui.Text2(g, "ВИДЖЕТЫ", Ui.Font(11, true), T.Faint, new RectangleF(S(22), S(58), S(200), S(22)), Ui.Left);
            int y = S(84);
            var foot = new Rectangle(S(12), ch - S(122), sw - S(24), S(106));
            // при большом числе виджетов строки становятся ниже, чтобы не залезть на нижний блок
            int step = Math.Min(S(60), (foot.Y - S(8) - y) / Math.Max(1, App.Core.Modules.Count));
            int rh = step - S(4), dy = (rh - S(56)) / 2;
            foreach (var m in App.Core.Modules)
            {
                var mod = m;
                var row = new Rectangle(S(12), y, sw - S(24), rh);
                bool active = mod == sel;
                if (active) Ui.FillRound(g, row, S(10), T.PanelHi);
                else if (hover == "row:" + mod.Id) Ui.FillRound(g, row, S(10), T.Panel);
                bool on = mod.Ported && mod.Enabled;
                var tile = new RectangleF(row.X + S(10), row.Y + S(11) + dy, S(34), S(34));
                Ui.FillRound(g, tile, S(9), on ? T.TileOn : T.TileOff);
                DrawIcon(g, mod.Icon, new RectangleF(tile.X + S(8), tile.Y + S(8), S(18), S(18)));
                float tx = tile.Right + S(12);
                float tw = row.Right - S(64) - tx;
                Ui.Text2(g, mod.Name, Ui.Font(14, true), mod.Ported ? T.Text : T.Muted, new RectangleF(tx, row.Y + S(8) + dy, tw, S(22)), Ui.Left);
                Ui.Text2(g, mod.Ported ? mod.Short : "Скоро переедет", Ui.Font(12), T.Faint, new RectangleF(tx, row.Y + S(29) + dy, tw, S(18)), Ui.Left);
                hits.Add(row, "row:" + mod.Id, () => Select(mod));
                var tg = new Rectangle(row.Right - S(54), row.Y + (rh - S(24)) / 2, S(44), S(24));
                Ui.Toggle(g, tg, on, mod.Ported, T);
                if (mod.Ported) hits.Add(tg, "tg:" + mod.Id, () => { App.Core.SetEnabled(mod, !mod.Enabled); Select(mod); });
                y += step;
            }

            Ui.FillRound(g, foot, S(10), T.Panel);
            var lf = Ui.Font(13, true);
            Ui.Text2(g, "Запускать вместе с Windows", lf, T.Text, new RectangleF(foot.X + S(14), foot.Y + S(8), foot.Width - S(80), S(28)), Ui.Left);
            var at = new Rectangle(foot.Right - S(56), foot.Y + S(10), S(44), S(24));
            Ui.Toggle(g, at, Installer.Autostart, true, T);
            hits.Add(at, "auto", () => { Installer.Autostart = !Installer.Autostart; Invalidate(); });
            Ui.Text2(g, "Обновляться автоматически", lf, T.Text, new RectangleF(foot.X + S(14), foot.Y + S(40), foot.Width - S(80), S(28)), Ui.Left);
            var ut = new Rectangle(foot.Right - S(56), foot.Y + S(42), S(44), S(24));
            Ui.Toggle(g, ut, Updater.Auto, true, T);
            hits.Add(ut, "upd", () => { Updater.Auto = !Updater.Auto; Invalidate(); });
            string ver = "Версия " + App.Version + (Updater.Status.Length > 0 ? " · " + Updater.Status : "");
            var vr = new RectangleF(foot.X + S(14), foot.Y + S(74), foot.Width - S(28), S(22));
            Ui.Text2(g, ver, Ui.Font(12), T.Faint, vr, Ui.Left);
            hits.Add(Rectangle.Round(vr), "ver", () => Updater.Check(true));
        }

        void DrawDetails(Graphics g, int cw, int ch)
        {
            int x = S(332), right = cw - S(32), w = right - x;
            var m = sel;
            bool running = m.Ported && m.Enabled;

            string badge = !m.Ported ? "Скоро" : running ? "Работает" : "Выключен";
            var bf = Ui.Font(12, true);
            int bw = (int)g.MeasureString(badge, bf).Width + S(26);
            var br = new Rectangle(right - bw, S(76), bw, S(28));
            Ui.FillRound(g, br, br.Height / 2f, running ? T.Accent : T.BadgeOff);
            Ui.Text2(g, badge, bf, running ? T.AccentInk : T.Muted, br, Ui.Center);

            Ui.Text2(g, m.Name, Ui.Font(26, true), T.Text, new RectangleF(x, S(68), w - bw - S(16), S(40)), Ui.Left);
            // описание переносим сами: каждая строка меряется и рисуется одним и тем же форматом,
            // поэтому строки не наезжают друг на друга и не обрезаются ни при каком DPI
            var df = Ui.Font(14);
            float dw = Math.Min(w - bw - S(16), S(540));
            int lh = (int)Math.Ceiling(df.GetHeight(g)) + S(3);
            var lines = WrapLines(g, m.Description, df, dw, 3);
            for (int li = 0; li < lines.Count; li++)
                Ui.Text2(g, lines[li], df, T.Muted, new RectangleF(x, S(112) + li * lh, dw, lh), Ui.Left);

            int y = S(112) + lines.Count * lh + S(20);
            var tabs = m.Ported ? m.Tabs : null;
            var cur = m;
            if (tabs != null && tabs.Length > 0)
            {
                m.TabIndex = Math.Max(0, Math.Min(tabs.Length - 1, m.TabIndex));
                cur = tabs[m.TabIndex];
                y = DrawTabs(g, m, tabs, x, y, w) + S(10);
            }
            if (!m.Ported)
            {
                var info = new Rectangle(x, y, w, S(76));
                Ui.FillRound(g, info, S(10), T.Panel);
                Ui.Text2(g, "Пока этот виджет работает отдельной программой, как раньше. Он переедет в " + App.Name +
                    " в следующем обновлении, и тогда его можно будет включать и настраивать здесь.",
                    Ui.Font(13.5), T.Muted, new RectangleF(info.X + S(16), info.Y + S(14), info.Width - S(32), info.Height - S(20)), Ui.Wrap);
                y = info.Bottom + S(16);
            }
            else
            {
                // строки сжимаются, если вместе с кнопкой сброса не помещаются над блоком стекла
                var rows = cur.Rows().ToList();
                if (cur.HasGlass) rows.Add(cur.GlassRow());
                bool reset = cur.CanReset && cur.Running;
                int room = ch - S(118) - S(20) - y - (reset ? S(48) : 0);
                int step = rows.Count == 0 ? S(60) : Math.Max(S(30), Math.Min(S(60), room / rows.Count));
                int rh = step - S(10) * step / S(60);
                int i = 0;
                foreach (var r in rows)
                {
                    var row = r;
                    var rc = new Rectangle(x, y, w, rh);
                    string key = "set:" + i++;
                    Ui.FillRound(g, rc, S(10), hover == key ? T.PanelHi : T.Panel);
                    Ui.Text2(g, row.Label, Ui.Font(14), T.Text, new RectangleF(rc.X + S(16), rc.Y, rc.Width / 2f, rc.Height), Ui.Left);
                    if (row.On != null)
                        Ui.Toggle(g, new Rectangle(rc.Right - S(60), rc.Y + (rh - S(24)) / 2, S(44), S(24)), row.On(), true, T);
                    else
                    {
                        Ui.Text2(g, row.Value(), Ui.Font(13.5, true), T.Accent, new RectangleF(rc.X + rc.Width / 2f, rc.Y, rc.Width / 2f - S(40), rc.Height), Ui.Right);
                        using (var p = new Pen(T.Faint, 1.5f * Ui.Scale))
                        {
                            float ax = rc.Right - S(26), ay = rc.Y + rc.Height / 2f;
                            g.DrawLines(p, new[] { new PointF(ax, ay - S(5)), new PointF(ax + S(5), ay), new PointF(ax, ay + S(5)) });
                        }
                    }
                    hits.Add(rc, key, () => { row.Click(); Invalidate(); });
                    y += step;
                }
                if (reset)
                {
                    y += S(2);
                    var bf2 = Ui.Font(13, true);
                    string label = "Вернуть на место";
                    var b = new Rectangle(x, y, (int)g.MeasureString(label, bf2).Width + S(36), S(40));
                    Ui.FillRound(g, b, b.Height / 2f, hover == "reset" ? T.PanelHi : T.Panel);
                    Ui.StrokeRound(g, b, b.Height / 2f, T.Stroke);
                    Ui.Text2(g, label, bf2, T.Text, b, Ui.Center);
                    hits.Add(b, "reset", () => cur.ResetPosition());
                }
            }

            int gy = ch - S(118);
            Line(g, x, gy, right, gy);
            int tl = DrawThemeSwitch(g, right, gy + S(11));
            Ui.Text2(g, "СТЕКЛО ДЛЯ ВСЕХ ВИДЖЕТОВ", Ui.Font(11, true), T.Faint, new RectangleF(x, gy + S(14), Math.Max(0, tl - S(16) - x), S(22)), Ui.Left);
            int px = x;
            var pf = Ui.Font(13);
            foreach (var name in Ui.GlassNames)
            {
                var n = name;
                bool on = n == App.Glass;
                var pill = new Rectangle(px, gy + S(46), (int)g.MeasureString(n, pf).Width + S(34), S(40));
                Ui.FillRound(g, pill, pill.Height / 2f, on ? T.SelFill : hover == "glass:" + n ? T.Panel : Color.Transparent);
                Ui.StrokeRound(g, pill, pill.Height / 2f, on ? T.Accent : T.Stroke);
                Ui.Text2(g, n, pf, on ? T.Text : T.Muted, pill, Ui.Center);
                hits.Add(pill, "glass:" + n, () => { App.Core.SetGlass(n); Invalidate(); });
                px = pill.Right + S(8);
            }
        }

        // Тема центра управления: подпись и три сегмента справа в строке заголовка блока стекла.
        // Возвращает левый край, чтобы заголовок стекла на него не залез.
        int DrawThemeSwitch(Graphics g, int right, int y)
        {
            var sf = Ui.Font(12);
            int h = S(28), pad = S(3), inner = h - 2 * pad;
            var names = CenterTheme.Names;
            var ws = names.Select(n => (int)Math.Ceiling(g.MeasureString(n, sf).Width) + S(20)).ToArray();
            int total = ws.Sum() + 2 * pad;
            var box = new Rectangle(right - total, y, total, h);
            Ui.FillRound(g, box, h / 2f, T.Panel);
            Ui.StrokeRound(g, box, h / 2f, T.Stroke);
            string cur = CenterTheme.Choice;
            int sx = box.X + pad;
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                bool on = n == cur;
                var seg = new Rectangle(sx, y + pad, ws[i], inner);
                if (on)
                {
                    Ui.FillRound(g, seg, inner / 2f, T.SelFill);
                    Ui.StrokeRound(g, seg, inner / 2f, T.SelStroke);
                }
                else if (hover == "theme:" + n) Ui.FillRound(g, seg, inner / 2f, T.PanelHi);
                Ui.Text2(g, n, sf, on ? T.Text : T.Muted, seg, Ui.Center);
                hits.Add(seg, "theme:" + n, () => SetTheme(n));
                sx += ws[i];
            }
            var lf = Ui.Font(11, true);
            const string label = "ТЕМА ЦЕНТРА";
            int lw = (int)Math.Ceiling(g.MeasureString(label, lf).Width);
            int lx = box.X - S(10) - lw;
            Ui.Text2(g, label, lf, T.Faint, new RectangleF(lx, y, lw + S(4), h), Ui.Left);
            return lx;
        }

        // Переключатель частей виджета: значок, название, подпись и точка «видно на столе».
        int DrawTabs(Graphics g, Module m, Module[] tabs, int x, int y, int w)
        {
            int inset = S(5), gap = S(6), th = S(56);
            var box = new Rectangle(x, y, w, th + 2 * inset);
            Ui.FillRound(g, box, S(14), T.Panel);
            int tw = (w - 2 * inset - gap * (tabs.Length - 1)) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                var t = tabs[i];
                int idx = i;
                var r = new Rectangle(x + inset + i * (tw + gap), y + inset, tw, th);
                bool on = i == m.TabIndex;
                string key = "tab:" + i;
                if (on)
                {
                    Ui.FillRound(g, r, S(10), T.SelTab);
                    Ui.StrokeRound(g, r, S(10), T.SelStroke);
                }
                else if (hover == key) Ui.FillRound(g, r, S(10), T.Panel);
                DrawIcon(g, t.Icon, new RectangleF(r.X + S(14), r.Y + (th - S(20)) / 2f, S(20), S(20)));
                float tx = r.X + S(46), dotX = r.Right - S(22);
                Ui.Text2(g, t.Name, Ui.Font(14, true), T.Text, new RectangleF(tx, r.Y + S(8), dotX - tx - S(6), S(22)), Ui.Left);
                Ui.Text2(g, t.Short, Ui.Font(12), T.Faint, new RectangleF(tx, r.Y + S(29), dotX - tx - S(6), S(18)), Ui.Left);
                bool live = m.Enabled && t.Enabled;
                using (var b = new SolidBrush(live ? T.Accent : T.DotOff))
                    g.FillEllipse(b, dotX, r.Y + (th - S(8)) / 2f, S(8), S(8));
                hits.Add(r, key, () => { m.TabIndex = idx; Invalidate(); });
            }
            return box.Bottom;
        }

        // Перенос по словам с той же мерой, что и у Ui.Left; лишнее уходит в многоточие последней строки.
        static System.Collections.Generic.List<string> WrapLines(Graphics g, string text, Font f, float width, int max)
        {
            var res = new System.Collections.Generic.List<string>();
            foreach (var para in (text ?? "").Split('\n'))
            {
                var words = para.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int i = 0;
                while (i < words.Length)
                {
                    if (res.Count == max - 1) { res.Add(string.Join(" ", words.Skip(i))); return res; }
                    string line = words[i++];
                    while (i < words.Length && g.MeasureString(line + " " + words[i], f).Width <= width) line += " " + words[i++];
                    res.Add(line);
                }
                if (res.Count >= max) break;
            }
            return res;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string k = hits.KeyAt(e.Location);
            Cursor = hits.CursorAt(e.Location);
            if (k != hover) { hover = k; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < S(48) && hits.KeyAt(e.Location) == null) WNative.DragWindow(this);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) hits.ClickAt(e.Location);
        }
    }
}
