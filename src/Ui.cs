using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Glasswing
{
    // Общий вид: масштаб под DPI, цвета, шрифты, виды стекла.
    static class Ui
    {
        public static float Scale = 1f;

        public static int S(double v) { return (int)Math.Round(v * Scale); }

        public static void Init()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
        }

        public static readonly Color Text = Color.FromArgb(255, 244, 246, 250);
        public static readonly Color Muted = Color.FromArgb(255, 184, 192, 204);
        public static readonly Color Faint = Color.FromArgb(255, 143, 152, 168);
        public static readonly Color Accent = Color.FromArgb(255, 110, 215, 200);
        public static readonly Color AccentInk = Color.FromArgb(255, 13, 26, 31);
        public static readonly Color Panel = Color.FromArgb(13, 255, 255, 255);
        public static readonly Color PanelHi = Color.FromArgb(26, 255, 255, 255);
        public static readonly Color Line = Color.FromArgb(20, 255, 255, 255);
        public static readonly Color Wash = Color.FromArgb(24, 255, 255, 255);
        public static readonly Color Edge = Color.FromArgb(70, 255, 255, 255);
        public static readonly Color Shadow = Color.FromArgb(140, 0, 0, 0);
        public static readonly Color Fallback = Color.FromArgb(255, 24, 27, 36);

        // Виды стекла: (режим Windows, плотность тонировки 0..255).
        public static readonly string[] GlassNames = { "Матовое", "Полупрозрачное", "Лёгкое размытие", "Чистое стекло" };
        static readonly int[][] GlassLevels = { new[] { 4, 110 }, new[] { 4, 20 }, new[] { 3, 0 }, new[] { 2, 40 } };

        public static bool ApplyGlass(IntPtr h, string name)
        {
            int i = Array.IndexOf(GlassNames, name);
            if (i < 0) i = 2;
            return WNative.SetAcrylic(h, GlassLevels[i][0], GlassLevels[i][1], 20, 22, 32);
        }

        // Акрил (state 4) и размытие (3) моргают, пока окно тащат; простая тонировка (2) нет.
        // На время переноса ставим тонировку чуть плотнее, после отпускания возвращаем стекло.
        public static void GlassWhileMoving(IntPtr h, string name)
        {
            int i = Array.IndexOf(GlassNames, name);
            if (i < 0) i = 2;
            if (GlassLevels[i][0] != 2) WNative.SetAcrylic(h, 2, Math.Min(230, GlassLevels[i][1] + 80), 20, 22, 32);
        }

        public const int WM_ENTERSIZEMOVE = 0x231, WM_EXITSIZEMOVE = 0x232;

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        // Размер в пикселях при 96 dpi; масштаб учитывается сам.
        public static Font Font(double px, bool semibold = false, string face = null)
        {
            if (face == null) face = semibold ? "Segoe UI Semibold" : "Segoe UI";
            string key = face + "|" + px;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font(face, (float)(px * Scale), FontStyle.Regular, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }

        public static GraphicsPath Round(RectangleF rc, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(2, Math.Min(radius * 2, Math.Min(rc.Width, rc.Height)));
            path.AddArc(rc.X, rc.Y, d, d, 180, 90);
            path.AddArc(rc.Right - d, rc.Y, d, d, 270, 90);
            path.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
            path.AddArc(rc.X, rc.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, RectangleF rc, float radius, Color c)
        {
            using (var p = Round(rc, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF rc, float radius, Color c, float width = 1)
        {
            using (var p = Round(rc, radius))
            using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public static readonly StringFormat Left = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        public static readonly StringFormat Right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        public static readonly StringFormat Center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
        public static readonly StringFormat Wrap = new StringFormat { LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord };

        public static void Text2(Graphics g, string s, Font f, Color c, RectangleF box, StringFormat fmt, bool shadow = false)
        {
            if (shadow)
                using (var sb = new SolidBrush(Shadow)) g.DrawString(s, f, sb, new RectangleF(box.X, box.Y + 1, box.Width, box.Height), fmt);
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, box, fmt);
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }

        // Переключатель-«таблетка», как в центре управления.
        public static void Toggle(Graphics g, Rectangle r, bool on, bool enabled = true)
        {
            var rc = new RectangleF(r.X, r.Y, r.Width, r.Height);
            Color fill = !enabled ? Color.FromArgb(10, 255, 255, 255) : on ? Accent : Color.FromArgb(16, 255, 255, 255);
            FillRound(g, rc, r.Height / 2f, fill);
            if (!on) StrokeRound(g, rc, r.Height / 2f, Color.FromArgb(enabled ? 72 : 30, 255, 255, 255));
            float k = r.Height - S(8);
            float x = on ? r.Right - S(4) - k : r.X + S(4);
            using (var b = new SolidBrush(on ? AccentInk : Color.FromArgb(enabled ? 255 : 90, 214, 219, 228)))
                g.FillEllipse(b, x, r.Y + S(4), k, k);
        }

        // Стеклянная бабочка: логотип Glasswing.
        public static void Logo(Graphics g, RectangleF r)
        {
            var st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 64f, r.Height / 64f);
            using (var wingL = new GraphicsPath())
            using (var wingR = new GraphicsPath())
            {
                wingL.AddBezier(31, 30, 18, 6, 2, 8, 6, 26);
                wingL.AddBezier(6, 26, 8, 36, 20, 36, 31, 32);
                wingL.CloseFigure();
                wingL.AddBezier(31, 34, 20, 36, 10, 44, 14, 54);
                wingL.AddBezier(14, 54, 20, 60, 28, 50, 31, 38);
                wingL.CloseFigure();
                using (var m = new Matrix(-1, 0, 0, 1, 64, 0)) { wingR.AddPath(wingL, false); wingR.Transform(m); }
                using (var br = new LinearGradientBrush(new RectangleF(0, 4, 64, 56), Color.FromArgb(235, 127, 224, 210), Color.FromArgb(235, 75, 143, 232), 60f))
                {
                    g.FillPath(br, wingL);
                    g.FillPath(br, wingR);
                }
                using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.6f))
                {
                    g.DrawPath(pen, wingL);
                    g.DrawPath(pen, wingR);
                }
            }
            using (var body = new SolidBrush(Color.FromArgb(255, 20, 30, 44)))
                g.FillEllipse(body, 29.5f, 22, 5, 30);
            g.Restore(st);
        }
    }

    // Список кликабельных мест на нарисованном окне.
    class Hits
    {
        class Hit { public Rectangle R; public string Key; public Action Click; public Cursor Cursor; }
        readonly List<Hit> list = new List<Hit>();

        public void Clear() { list.Clear(); }

        public void Add(Rectangle r, string key, Action click, Cursor cursor = null)
        {
            list.Add(new Hit { R = r, Key = key, Click = click, Cursor = cursor ?? Cursors.Hand });
        }

        public string KeyAt(Point p)
        {
            for (int i = list.Count - 1; i >= 0; i--) if (list[i].R.Contains(p)) return list[i].Key;
            return null;
        }

        public Cursor CursorAt(Point p)
        {
            for (int i = list.Count - 1; i >= 0; i--) if (list[i].R.Contains(p)) return list[i].Cursor;
            return Cursors.Default;
        }

        public bool ClickAt(Point p)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!list[i].R.Contains(p)) continue;
                if (list[i].Click != null) list[i].Click();
                return true;
            }
            return false;
        }
    }
}
