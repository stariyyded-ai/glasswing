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
            if (houseWindow == null) houseWindow = new PetHouseWindow { Text = "Домик Уголька", Bounds = bounds };
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
            using (var shadow = new SolidBrush(Color.FromArgb(75, 20, 12, 34)))
            using (var wood = new SolidBrush(Color.FromArgb(247, 77, 42, 48)))
            using (var gold = new SolidBrush(Color.FromArgb(255, 196, 108)))
            using (var beam = new Pen(Color.FromArgb(230, 192, 117, 98), 2))
            {
                g.FillEllipse(shadow, 7, 109, 146, 14);
                g.FillRectangle(wood, 20, 106, 118, 10);
                g.DrawLine(beam, 22, 107, 136, 107);
                if (stage < 2) return;
                using (var wall = new LinearGradientBrush(new Rectangle(25, 46, 108, 62),
                    Color.FromArgb(246, 123, 67, 104), Color.FromArgb(251, 69, 39, 68), 90f))
                    g.FillRectangle(wall, 25, 46, 108, 62);
                g.DrawRectangle(beam, 25, 46, 108, 62);
                if (stage < 3) return;
                g.FillRectangle(wood, 106, 18, 15, 34);
                using (var roof = new SolidBrush(Color.FromArgb(250, 82, 49, 110)))
                    g.FillPolygon(roof, new[] { new Point(14, 53), new Point(79, 11), new Point(144, 53) });
                g.DrawLines(beam, new[] { new Point(14, 53), new Point(79, 11), new Point(144, 53) });
                g.FillEllipse(gold, 71, 33, 16, 16);
                g.FillRectangle(wood, 64, 72, 30, 36);
                g.FillEllipse(wood, 64, 60, 30, 25);
                g.FillEllipse(gold, 86, 87, 4, 4);
                g.FillRectangle(gold, 35, 64, 17, 20);
                g.FillRectangle(gold, 106, 64, 17, 20);
            }
        }

        float PortalScale(double t)
        {
            if (portalOpened > 0) return Math.Max(0.06f, 1 - (float)((t - portalOpened) / PortalDuration));
            return Math.Min(1, 0.06f + (float)((t - portalArrival) / PortalDuration));
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
