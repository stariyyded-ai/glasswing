using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Glasswing
{
    // The house is a decoration on the desktop. Mouse clicks pass through it.
    sealed class PetHouseWindow : PetForm
    {
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        public void PlaceBehind(PetForm pet)
        {
            if (!pet.TopMost) SetWindowPos(Handle, pet.Handle, 0, 0, 0, 0, 0x13);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000020; // NOACTIVATE | TRANSPARENT
                return cp;
            }
        }
    }

    partial class PetBody
    {
        const double PortalDuration = 0.42;
        double portalOpened, portalArrival = -10;
        PetHouseWindow houseWindow;
        Bitmap houseFrame;
        Rectangle houseBounds;
        double buildStarted = -1;
        int houseStage;
        bool homePerched;
        public Func<Rectangle?> WeatherBounds;

        public bool HasHouse => houseStage == 3;

        public Screen HomeScreen()
        {
            return Screen.AllScreens.Where(screen => !screen.Primary)
                .OrderBy(screen => screen.DeviceName, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        Rectangle HouseBounds(Screen home)
        {
            var area = home.WorkingArea;
            var weather = WeatherBounds?.Invoke();
            bool aboveWeather = weather.HasValue && Screen.FromRectangle(weather.Value).DeviceName == home.DeviceName;
            int width = (int)(158 * Scale), height = (int)(124 * Scale);
            int x = aboveWeather ? weather.Value.Left + (weather.Value.Width - width) / 2 : area.Left + (int)(65 * Scale);
            int bottom = aboveWeather ? weather.Value.Top - (int)(14 * Scale) : area.Bottom - (int)(270 * Scale);
            return new Rectangle(
                Math.Max(area.Left, Math.Min(area.Right - width, x)),
                Math.Max(area.Top, Math.Min(area.Bottom - height, bottom - height)),
                width, height);
        }

        public bool AtHome()
        {
            var home = HomeScreen();
            if (home == null || Area() != home.WorkingArea) return false;
            var bounds = HouseBounds(home);
            return Math.Abs(GroundX() - (bounds.Left + bounds.Width / 2f)) < 18 * Scale;
        }

        public bool WalkHome()
        {
            var home = HomeScreen();
            if (home == null) return false;
            var bounds = HouseBounds(home);
            WalkTo(bounds.Left + bounds.Width / 2f, 250, home);
            return true;
        }

        public void RestoreHouse(bool built) { houseStage = built ? 3 : 0; }

        public void SyncHouse()
        {
            if (houseWindow != null && !Main.Visible) houseWindow.Hide();
        }

        int HouseTop(int top, float x)
        {
            if (Mode == 0 && AtHome())
            {
                homePerched = true;
                return HouseBounds(HomeScreen()).Bottom - H + (int)(8 * Scale);
            }
            if (homePerched && Math.Abs(x - GroundX()) > 0.25f && Mode != 0)
            {
                homePerched = false;
                return area.Bottom - H;
            }
            return top;
        }

        void UpdateHouse(double t)
        {
            var home = HomeScreen();
            if (home == null || !Main.Visible)
            {
                houseWindow?.Hide();
                return;
            }

            if (houseStage < 3 && Mode == 0 && !Sleeping && AtHome())
            {
                if (buildStarted < 0) buildStarted = t;
                int stage = Math.Min(3, 1 + (int)((t - buildStarted) / 0.8));
                if (stage > houseStage)
                {
                    houseStage = stage;
                    Spawn(1, -24, -6, 0.8);
                    if (HasHouse) Event = "homebuilt";
                }
            }
            else buildStarted = -1;
            if (houseStage == 0) return;

            var bounds = HouseBounds(home);
            if (houseWindow == null) houseWindow = new PetHouseWindow { Text = "Шалаш Уголька", Bounds = bounds };
            if (houseWindow.Visible && houseBounds == bounds && houseWindow.Tag is int
                && (int)houseWindow.Tag == houseStage) return;
            if (houseFrame == null || houseFrame.Size != bounds.Size)
            {
                houseFrame?.Dispose();
                houseFrame = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            }
            using (var g = Graphics.FromImage(houseFrame))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(Scale, Scale);
                DrawHouse(g, houseStage);
            }
            houseWindow.Tag = houseStage;
            houseBounds = bounds;
            if (!houseWindow.Visible) houseWindow.Show();
            houseWindow.SetBitmapAt(houseFrame, bounds.X, bounds.Y);
            houseWindow.PlaceBehind(Main);
        }

        static void DrawHouse(Graphics g, int stage)
        {
            using (var shadow = new SolidBrush(Color.FromArgb(95, 20, 12, 34)))
            using (var bed = new SolidBrush(Color.FromArgb(242, 147, 89, 116)))
            using (var stitching = new Pen(Color.FromArgb(220, 239, 163, 159), 1.5f))
            using (var branch = new Pen(Color.FromArgb(255, 139, 91, 66), 4.5f))
            {
                branch.StartCap = branch.EndCap = LineCap.Round;
                g.FillEllipse(shadow, 5, 108, 148, 15);
                g.FillEllipse(bed, 23, 101, 112, 15);
                g.DrawArc(stitching, 27, 102, 104, 10, 10, 160);
                if (stage < 2) return;
                if (stage == 3)
                {
                    using (var leaves = new LinearGradientBrush(new Rectangle(9, 17, 140, 93),
                        Color.FromArgb(255, 118, 125, 70), Color.FromArgb(255, 46, 63, 54), 76f))
                    using (var opening = new LinearGradientBrush(new Rectangle(35, 30, 88, 81),
                        Color.FromArgb(255, 37, 27, 41), Color.FromArgb(255, 94, 54, 49), 90f))
                    {
                        g.FillPolygon(leaves, new[] { new Point(7, 109), new Point(27, 66), new Point(54, 40),
                            new Point(78, 16), new Point(105, 38), new Point(126, 69), new Point(150, 109) });
                        g.FillPolygon(opening, new[] { new Point(34, 110), new Point(79, 33), new Point(124, 110) });
                    }
                    using (var twig = new Pen(Color.FromArgb(150, 178, 142, 88), 1.5f))
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            g.DrawLine(twig, 15 + 11 * i, 102, 52 + 5 * i, 46 - 5 * i);
                            g.DrawLine(twig, 143 - 10 * i, 103, 106 - 5 * i, 45 - 5 * i);
                        }
                    }
                    g.FillEllipse(bed, 40, 101, 79, 13);
                    g.DrawArc(stitching, 43, 102, 73, 8, 10, 160);
                    using (var glow = new SolidBrush(Color.FromArgb(255, 237, 166, 88)))
                        g.FillEllipse(glow, 112, 87, 9, 12);
                }
                g.DrawLine(branch, 25, 110, 88, 9);
                g.DrawLine(branch, 135, 110, 69, 10);
                g.DrawLine(stitching, 72, 19, 84, 23);
                g.DrawLine(stitching, 72, 23, 84, 27);
            }
        }

        float PortalScale(double t)
        {
            bool leaving = portalOpened > 0;
            float progress = Math.Max(0, Math.Min(1,
                (float)((t - (leaving ? portalOpened : portalArrival)) / PortalDuration)));
            float eased = progress * progress * (3 - 2 * progress);
            return leaving ? 1 - .94f * eased : .06f + .94f * eased;
        }

        void DrawPortal(Graphics g, double t, float gx, float gy, float u)
        {
            double age = t - (portalOpened > 0 ? portalOpened : portalArrival);
            if (age < 0 || age >= PortalDuration) return;
            float phase = (float)(age / PortalDuration);
            float opening = (float)Math.Sin(Math.PI * phase);
            var oval = new RectangleF(gx - 37 * u * opening, gy - 112 * u, 74 * u * opening, 112 * u);
            if (oval.Width < u) return;
            using (var halo = new Pen(Color.FromArgb(105, 163, 100, 255), 11 * u))
            using (var rim = new Pen(Color.FromArgb(231, 222, 172, 255), 3 * u))
            using (var spark = new Pen(Color.FromArgb(248, 255, 202, 128), 2 * u))
            {
                g.DrawEllipse(halo, oval);
                g.DrawEllipse(rim, oval);
                g.DrawArc(spark, oval, -140 + phase * 300, 66);
                g.DrawArc(spark, oval, 40 + phase * 300, 42);
            }
        }
    }
}
