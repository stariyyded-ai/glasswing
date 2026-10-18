using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace Glasswing
{
    // Основа любого виджета на рабочем столе: стеклянная карточка без рамки,
    // которую можно перетащить, закрепить и вывести поверх окон. Позиция запоминается.
    abstract class GlassCard : Form
    {
        protected readonly Module Mod;
        bool noGlass;
        readonly Timer toolTimer = new Timer { Interval = 1000 };

        protected GlassCard(Module owner)
        {
            Mod = owner;
            Text = App.Name + ": " + owner.Name;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;              // на стекле чёрный = прозрачно
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            TopMost = Settings.GetBool(owner.Id, "TopMost", false);
            toolTimer.Tick += (s, e) => { if (IsHandleCreated) WNative.EnsureToolWindow(Handle); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;               // TOOLWINDOW: нет в Alt+Tab
                return cp;
            }
        }

        public bool Locked
        {
            get { return Settings.GetBool(Mod.Id, "Locked", false); }
            set { Settings.Set(Mod.Id, "Locked", value); }
        }

        // Размер карточки и место по умолчанию задаёт сам виджет.
        protected abstract Size CardSize();
        protected abstract Point DefaultLocation(Rectangle workArea);
        protected abstract void DrawContent(Graphics g, int w, int h);
        protected virtual void AddMenuItems(ContextMenuStrip menu) { }
        protected virtual void OnCardMoved() { }
        // Кнопки на самой карточке: true, если нажатие забрал виджет и окно тащить не надо.
        protected virtual bool HandleMouseDown(Point p) { return false; }

        public void PlaceFromSettings()
        {
            var size = CardSize();
            var wa = Screen.PrimaryScreen.WorkingArea;
            var pos = DefaultLocation(wa);
            if (Settings.Has(Mod.Id, "X"))
            {
                var saved = new Point(Settings.GetInt(Mod.Id, "X", pos.X), Settings.GetInt(Mod.Id, "Y", pos.Y));
                if (Screen.AllScreens.Any(s => s.WorkingArea.Contains(saved))) pos = saved;
            }
            Bounds = new Rectangle(pos, size);
        }

        public void ResetPosition()
        {
            var size = CardSize();
            Bounds = new Rectangle(DefaultLocation(Screen.PrimaryScreen.WorkingArea), size);
            SavePosition();
            OnCardMoved();
        }

        protected void FitSize()
        {
            var sz = CardSize();
            if (Size != sz) Bounds = new Rectangle(Location, sz);
        }

        void SavePosition()
        {
            Settings.Set(Mod.Id, "X", Left, false);
            Settings.Set(Mod.Id, "Y", Top);
        }

        protected override void WndProc(ref Message m)
        {
            if (!noGlass && m.Msg == Ui.WM_ENTERSIZEMOVE) Ui.GlassWhileMoving(Handle, Mod.Glass);
            base.WndProc(ref m);
            if (!noGlass && m.Msg == Ui.WM_EXITSIZEMOVE) ApplyGlass();
        }

        public void ApplyGlass()
        {
            if (!IsHandleCreated) return;
            noGlass = !Ui.ApplyGlass(Handle, Mod.Glass);
            if (noGlass) Opacity = 0.92;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WNative.EnsureToolWindow(Handle);
            WNative.RoundDark(Handle);
            ApplyGlass();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            WNative.EnsureToolWindow(Handle);
            FitSize();   // Windows может растянуть окно при первом показе
            toolTimer.Start();
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
                    try { DrawContent(g, cw, ch); }
                    catch (Exception ex) { Log.Write(Mod.Id + " paint: " + ex.Message); }
                }
                WNative.BlitArgb(e.Graphics, bmp, 0, 0);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || HandleMouseDown(e.Location) || Locked) return;
            var before = Location;
            WNative.DragWindow(this);
            if (Location != before) { SavePosition(); OnCardMoved(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) BuildMenu().Show(this, e.Location);
        }

        ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 9.5f) };
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            var own = new ToolStripMenuItem("Стекло этого виджета");
            own.DropDownItems.Add(new ToolStripMenuItem(Module.SameGlass + " (" + App.Glass + ")", null, (s, e) => Mod.OwnGlass = "") { Checked = Mod.OwnGlass.Length == 0 });
            own.DropDownItems.Add(new ToolStripSeparator());
            foreach (var name in Ui.GlassNames)
            {
                var n = name;
                own.DropDownItems.Add(new ToolStripMenuItem(n, null, (s, e) => Mod.OwnGlass = n) { Checked = n == Mod.OwnGlass });
            }
            menu.Items.Add(own);
            var glass = new ToolStripMenuItem("Стекло для всех виджетов");
            foreach (var name in Ui.GlassNames)
            {
                var n = name;
                glass.DropDownItems.Add(new ToolStripMenuItem(n, null, (s, e) => App.Core.SetGlass(n)) { Checked = n == App.Glass });
            }
            menu.Items.Add(glass);
            AddMenuItems(menu);
            menu.Items.Add(new ToolStripMenuItem("Поверх всех окон", null, (s, e) =>
            {
                TopMost = !TopMost;
                Settings.Set(Mod.Id, "TopMost", TopMost);
            }) { Checked = TopMost });
            menu.Items.Add(new ToolStripMenuItem("Закрепить на месте", null, (s, e) => Locked = !Locked) { Checked = Locked });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Центр управления " + App.Name, null, (s, e) => App.Core.ShowCenter(Mod)));
            menu.Items.Add(new ToolStripMenuItem("Убрать «" + Mod.Name + "»", null, (s, e) => App.Core.SetEnabled(Mod, false)));
            return menu;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) toolTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
