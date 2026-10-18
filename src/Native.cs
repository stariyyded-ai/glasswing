using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Glasswing
{
    // Всё, что берём у Windows напрямую: стекло, скруглённые углы, тёмные меню.
    static class WNative
    {
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);

        [StructLayout(LayoutKind.Sequential)] struct ACCENTPOLICY { public int State; public int Flags; public int Color; public int Anim; }
        [StructLayout(LayoutKind.Sequential)] struct WCADATA { public int Attr; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WCADATA d);

        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);

        public static int Build;

        static WNative()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    Build = int.Parse((string)k.GetValue("CurrentBuild", "0"));
            }
            catch { Build = 0; }
        }

        // Размытое "акриловое" стекло под окном (Windows 10/11). alpha 0..255 = плотность тонировки.
        public static bool SetAcrylic(IntPtr h, int state, int alpha, int r, int g, int b)
        {
            try
            {
                var a = new ACCENTPOLICY();
                a.State = state; a.Flags = 2;
                a.Color = unchecked((int)(((uint)alpha << 24) | ((uint)b << 16) | ((uint)g << 8) | (uint)r));
                int size = Marshal.SizeOf(a);
                IntPtr p = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(a, p, false);
                    var d = new WCADATA { Attr = 19, Data = p, Size = size };
                    return SetWindowCompositionAttribute(h, ref d) != 0;
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { return false; }
        }

        // Скруглённые углы, тёмная рамка (Windows 11).
        public static void RoundDark(IntPtr h)
        {
            int round = 2, dark = 1, border = 140 + (144 << 8) + (160 << 16);
            try
            {
                DwmSetWindowAttribute(h, 33, ref round, 4);
                DwmSetWindowAttribute(h, 20, ref dark, 4);
                DwmSetWindowAttribute(h, 34, ref border, 4);
            }
            catch { }
        }

        // Копирует картинку вместе с прозрачностью: прозрачные места остаются стеклом, а не чёрными.
        public static void BlitArgb(Graphics g, Bitmap bmp, int x, int y)
        {
            IntPtr hdc = g.GetHdc();
            try
            {
                IntPtr mem = CreateCompatibleDC(hdc);
                IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0));
                IntPtr old = SelectObject(mem, hb);
                BitBlt(hdc, x, y, bmp.Width, bmp.Height, mem, 0, 0, 0x00CC0020);
                SelectObject(mem, old);
                DeleteObject(hb);
                DeleteDC(mem);
            }
            finally { g.ReleaseHdc(hdc); }
        }

        public static void PreferDark() { try { SetPreferredAppMode(2); } catch { } }

        // Окно-инструмент: нет на панели задач и в Alt+Tab.
        public static void EnsureToolWindow(IntPtr h)
        {
            int ex = GetWindowLong(h, -20);
            int want = (ex | 0x80) & ~0x40000;
            if (want != ex)
            {
                SetWindowLong(h, -20, want);
                SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x20);
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam; public IntPtr lParam; public int time; public int x, y; }
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, int min, int max);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetCapture();
        [DllImport("dwmapi.dll")] static extern int DwmFlush();

        // Перетаскивание окна за любое место. Двигаем сами, а не системным циклом переноса
        // (WM_NCLBUTTONDOWN + HTCAPTION): с ним стеклянные окна моргают, пока их тащат.
        // Как и системный цикл, возвращается после отпускания кнопки и само отпускание съедает.
        public static void DragWindow(Form f)
        {
            IntPtr h = f.Handle;
            Point start = Cursor.Position, origin = f.Location, last = origin;
            SetCapture(h);
            SendMessage(h, Ui.WM_ENTERSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
            try
            {
                MSG m;
                while (true)
                {
                    int r = GetMessage(out m, IntPtr.Zero, 0, 0);
                    if (r == 0) { PostQuitMessage((int)m.wParam); break; }
                    if (r < 0) break;
                    if (m.message == 0x202) break;                                   // WM_LBUTTONUP
                    if (m.message == 0x100 && (int)m.wParam == 0x1B)                 // Esc: вернуть на место
                    {
                        SetWindowPos(h, IntPtr.Zero, origin.X, origin.Y, 0, 0, 0x1 | 0x4 | 0x10);
                        break;
                    }
                    if (m.message == 0x200 && m.hwnd == h)                           // WM_MOUSEMOVE
                    {
                        var p = Cursor.Position;
                        var to = new Point(origin.X + p.X - start.X, origin.Y + p.Y - start.Y);
                        if (to != last)
                        {
                            SetWindowPos(h, IntPtr.Zero, to.X, to.Y, 0, 0, 0x1 | 0x4 | 0x10);
                            last = to;
                            try { DwmFlush(); } catch { }   // не чаще одного сдвига за кадр экрана
                        }
                        continue;
                    }
                    TranslateMessage(ref m);
                    DispatchMessage(ref m);
                    if (GetCapture() != h || (Control.MouseButtons & MouseButtons.Left) == 0) break;
                }
            }
            finally
            {
                if (GetCapture() == h) ReleaseCapture();
                if (!f.IsDisposed) SendMessage(h, Ui.WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
            }
        }

        public static void Activate(Form f)
        {
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Show();
            ShowWindow(f.Handle, 5);
            SetForegroundWindow(f.Handle);
            f.Activate();
        }
    }

    // Убирает значки рабочего стола из-под карточки: двигает их в ближайшую свободную клетку сетки.
    static class DeskIcons
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern int MapWindowPoints(IntPtr from, IntPtr to, ref PT p, int n);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr hp, IntPtr addr, IntPtr size, uint type, uint prot);
        [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr hp, IntPtr addr, IntPtr size, uint type);
        [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr hp, IntPtr addr, byte[] buf, IntPtr size, out IntPtr done);
        [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr hp, IntPtr addr, byte[] buf, IntPtr size, out IntPtr done);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        struct PT { public int X, Y; }

        const int LVM_GETITEMCOUNT = 0x1004, LVM_GETITEMRECT = 0x100E, LVM_GETITEMPOSITION = 0x1010,
                  LVM_SETITEMPOSITION32 = 0x1031, LVM_GETITEMSPACING = 0x1033, LVS_AUTOARRANGE = 0x0100;

        static IntPtr FindListView()
        {
            IntPtr def = IntPtr.Zero;
            IntPtr prog = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            if (prog != IntPtr.Zero) def = FindWindowEx(prog, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr ww = IntPtr.Zero;
            while (def == IntPtr.Zero)
            {
                ww = FindWindowEx(IntPtr.Zero, ww, "WorkerW", null);
                if (ww == IntPtr.Zero) break;
                def = FindWindowEx(ww, IntPtr.Zero, "SHELLDLL_DefView", null);
            }
            return def == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(def, IntPtr.Zero, "SysListView32", null);
        }

        // Возвращает число сдвинутых значков; -1, если включено «Упорядочить значки автоматически».
        public static int Clear(Rectangle keep, Rectangle area)
        {
            IntPtr lv = FindListView();
            if (lv == IntPtr.Zero || !IsWindowVisible(lv)) return 0;
            if ((GetWindowLong(lv, -16) & LVS_AUTOARRANGE) != 0) return -1;
            int n = (int)SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (n <= 0) return 0;
            uint pid;
            GetWindowThreadProcessId(lv, out pid);
            IntPtr hp = OpenProcess(0x0008 | 0x0010 | 0x0020, false, pid);
            if (hp == IntPtr.Zero) return 0;
            IntPtr mem = VirtualAllocEx(hp, IntPtr.Zero, (IntPtr)64, 0x3000, 0x04);
            if (mem == IntPtr.Zero) { CloseHandle(hp); return 0; }
            int moved = 0;
            try
            {
                var org = new PT();
                MapWindowPoints(lv, IntPtr.Zero, ref org, 1);
                var rc = new Rectangle[n];
                var pos = new Point[n];
                var buf = new byte[16];
                var zero = new byte[16];
                IntPtr done;
                for (int i = 0; i < n; i++)
                {
                    SendMessage(lv, LVM_GETITEMPOSITION, (IntPtr)i, mem);
                    ReadProcessMemory(hp, mem, buf, (IntPtr)8, out done);
                    pos[i] = new Point(BitConverter.ToInt32(buf, 0), BitConverter.ToInt32(buf, 4));
                    WriteProcessMemory(hp, mem, zero, (IntPtr)16, out done);   // left = LVIR_BOUNDS
                    SendMessage(lv, LVM_GETITEMRECT, (IntPtr)i, mem);
                    ReadProcessMemory(hp, mem, buf, (IntPtr)16, out done);
                    rc[i] = Rectangle.FromLTRB(BitConverter.ToInt32(buf, 0) + org.X, BitConverter.ToInt32(buf, 4) + org.Y,
                                               BitConverter.ToInt32(buf, 8) + org.X, BitConverter.ToInt32(buf, 12) + org.Y);
                }
                int sp = (int)SendMessage(lv, LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero);
                int cx = sp & 0xFFFF, cy = (sp >> 16) & 0xFFFF;
                if (cx < 16 || cy < 16) return 0;
                Rectangle guard = Rectangle.Inflate(keep, 6, 6);

                // смещение сетки берём у значков, которые уже стоят на этом мониторе
                int offX = 0, offY = 0;
                for (int i = 0; i < n; i++)
                {
                    if (area.Contains(rc[i].Location) && !rc[i].IntersectsWith(guard))
                    {
                        offX = ((rc[i].X - area.X) % cx + cx) % cx;
                        offY = ((rc[i].Y - area.Y) % cy + cy) % cy;
                        break;
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    if (!rc[i].IntersectsWith(guard)) continue;
                    Size sz = rc[i].Size;
                    bool placed = false;
                    for (int x = area.X + offX; !placed && x + sz.Width <= area.Right; x += cx)
                    {
                        for (int y = area.Y + offY; y + sz.Height <= area.Bottom; y += cy)
                        {
                            var cand = new Rectangle(x, y, sz.Width, sz.Height);
                            if (cand.IntersectsWith(guard)) continue;
                            bool busy = false;
                            for (int j = 0; j < n && !busy; j++) if (j != i && cand.IntersectsWith(rc[j])) busy = true;
                            if (busy) continue;
                            int nx = pos[i].X + (x - rc[i].X), ny = pos[i].Y + (y - rc[i].Y);
                            var p = new byte[8];
                            BitConverter.GetBytes(nx).CopyTo(p, 0);
                            BitConverter.GetBytes(ny).CopyTo(p, 4);
                            WriteProcessMemory(hp, mem, p, (IntPtr)8, out done);
                            SendMessage(lv, LVM_SETITEMPOSITION32, (IntPtr)i, mem);
                            rc[i] = cand;
                            moved++;
                            placed = true;
                            break;
                        }
                    }
                }
            }
            finally
            {
                VirtualFreeEx(hp, mem, IntPtr.Zero, 0x8000);
                CloseHandle(hp);
            }
            return moved;
        }
    }

    class DarkColors : ProfessionalColorTable
    {
        static readonly Color Bg = Color.FromArgb(32, 32, 36), Hi = Color.FromArgb(56, 56, 64), Edge = Color.FromArgb(70, 70, 80);
        public override Color ToolStripDropDownBackground => Bg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color MenuBorder => Edge;
        public override Color MenuItemBorder => Hi;
        public override Color MenuItemSelected => Hi;
        public override Color MenuItemSelectedGradientBegin => Hi;
        public override Color MenuItemSelectedGradientEnd => Hi;
        public override Color MenuItemPressedGradientBegin => Hi;
        public override Color MenuItemPressedGradientEnd => Hi;
        public override Color SeparatorDark => Edge;
        public override Color SeparatorLight => Edge;
        public override Color CheckBackground => Hi;
        public override Color CheckSelectedBackground => Hi;
        public override Color CheckPressedBackground => Hi;
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Color.FromArgb(235, 235, 240) : Color.FromArgb(120, 120, 130);
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(200, 200, 210);
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Color.FromArgb(120, 190, 255), Math.Max(2f, r.Height / 8f)))
            {
                e.Graphics.DrawLines(p, new[] {
                    new Point(r.Left + r.Width / 5, r.Top + r.Height / 2),
                    new Point(r.Left + r.Width * 2 / 5, r.Bottom - r.Height / 4),
                    new Point(r.Right - r.Width / 5, r.Top + r.Height / 4) });
            }
        }
    }
}
