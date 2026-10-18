using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Glasswing
{
    // Строка настройки в центре управления: переключатель или значение, которое меняется по щелчку.
    class Row
    {
        public string Label;
        public Func<bool> On;          // есть: рисуем переключатель
        public Func<string> Value;     // иначе: текст справа
        public Action Click;
    }

    enum IconKind { Clock, Monitor, Music, Fox, Translate, Games }

    // Виджет внутри Glasswing.
    abstract class Module
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Short { get; }
        public abstract string Description { get; }
        public abstract IconKind Icon { get; }

        // Ещё не переехал: в центре управления виден, но включается прежней версией.
        public virtual bool Ported => true;

        public bool Enabled
        {
            get { return Settings.GetBool(Id, "Enabled", true); }
            set { Settings.Set(Id, "Enabled", value); }
        }

        public abstract bool Running { get; }
        public abstract void Start();
        public abstract void Stop();
        public virtual void SetHidden(bool hidden) { }
        public virtual void GlassChanged() { }
        public virtual bool CanReset => false;
        public virtual void ResetPosition() { }
        public virtual IEnumerable<Row> Rows() { yield break; }
    }

    // ---------------------------------------------------------------- часы

    class ClockModule : Module
    {
        ClockCard card;
        public override string Id => "clock";
        public override string Name => "Часы";
        public override string Short => "Время и дата на столе";
        public override string Description => "Крупные стеклянные часы с датой. Сами отодвигают значки рабочего стола, чтобы те не прятались под виджетом.";
        public override IconKind Icon => IconKind.Clock;
        public override bool Running => card != null && !card.IsDisposed;
        public override bool CanReset => true;

        public bool Seconds
        {
            get { return Settings.GetBool(Id, "Seconds", false); }
            set { Settings.Set(Id, "Seconds", value); if (Running) card.Refit(); }
        }

        public bool KeepIcons
        {
            get { return Settings.GetBool(Id, "KeepIcons", true); }
            set { Settings.Set(Id, "KeepIcons", value); if (Running) card.ClearIcons(); }
        }

        public static readonly string[] DateStyles = { "День недели и число", "Только число", "Без даты" };

        public int DateStyle
        {
            get { return Math.Max(0, Math.Min(2, Settings.GetInt(Id, "DateStyle", 0))); }
            set { Settings.Set(Id, "DateStyle", value); if (Running) card.Refit(); }
        }

        public override void Start()
        {
            if (Running) return;
            card = new ClockCard(this);
            card.PlaceFromSettings();
            card.Show();
        }

        public override void Stop()
        {
            if (!Running) return;
            card.Close();
            card.Dispose();
            card = null;
        }

        public override void SetHidden(bool hidden) { if (Running) card.Visible = !hidden; }
        public override void GlassChanged() { if (Running) card.ApplyGlass(); }
        public override void ResetPosition() { if (Running) card.ResetPosition(); }

        public override IEnumerable<Row> Rows()
        {
            yield return new Row { Label = "Показывать секунды", On = () => Seconds, Click = () => Seconds = !Seconds };
            yield return new Row { Label = "Не пускать значки под часы", On = () => KeepIcons, Click = () => KeepIcons = !KeepIcons };
            yield return new Row { Label = "Дата", Value = () => DateStyles[DateStyle], Click = () => DateStyle = (DateStyle + 1) % DateStyles.Length };
        }
    }

    class ClockCard : GlassCard
    {
        static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        readonly ClockModule clock;
        readonly Timer tick = new Timer { Interval = 250 };
        readonly Timer icons = new Timer { Interval = 2000 };
        string shown = "";

        public ClockCard(ClockModule m) : base(m)
        {
            clock = m;
            tick.Tick += (s, e) =>
            {
                string key = TimeText() + "|" + DateText();
                if (key != shown) { shown = key; Invalidate(); }
            };
            icons.Tick += (s, e) => ClearIcons();
        }

        string TimeText() { return DateTime.Now.ToString(clock.Seconds ? "HH:mm:ss" : "HH:mm"); }

        string DateText()
        {
            var now = DateTime.Now;
            switch (clock.DateStyle)
            {
                case 1: return now.ToString("d MMMM yyyy", Ru);
                case 2: return "";
                default:
                    string d = now.ToString("dddd, d MMMM", Ru);
                    return char.ToUpper(d[0], Ru) + d.Substring(1);
            }
        }

        protected override Size CardSize()
        {
            int w = clock.Seconds ? Ui.S(350) : Ui.S(280);
            return new Size(w, clock.DateStyle == 2 ? Ui.S(100) : Ui.S(124));
        }

        protected override Point DefaultLocation(Rectangle wa)
        {
            return new Point(wa.Right - CardSize().Width - Ui.S(420), wa.Top + Ui.S(24));
        }

        public void Refit()
        {
            FitSize();
            shown = "";
            Invalidate();
            ClearIcons();
        }

        protected override void DrawContent(Graphics g, int w, int h)
        {
            bool date = clock.DateStyle != 2;
            Ui.Text2(g, TimeText(), Ui.Font(66, false, "Segoe UI Light"), Color.White,
                new RectangleF(0, Ui.S(4), w, date ? Ui.S(84) : h - Ui.S(8)), Ui.Center, true);
            if (date)
                Ui.Text2(g, DateText(), Ui.Font(15), Color.FromArgb(185, 255, 255, 255),
                    new RectangleF(0, Ui.S(86), w, Ui.S(24)), Ui.Center, true);
        }

        protected override void AddMenuItems(ContextMenuStrip menu)
        {
            menu.Items.Add(new ToolStripMenuItem("Показывать секунды", null, (s, e) => clock.Seconds = !clock.Seconds) { Checked = clock.Seconds });
            menu.Items.Add(new ToolStripMenuItem("Не пускать значки под часы", null, (s, e) => clock.KeepIcons = !clock.KeepIcons) { Checked = clock.KeepIcons });
        }

        protected override void OnCardMoved() { ClearIcons(); }

        // Значки рабочего стола, оказавшиеся под карточкой, переезжают в свободную клетку рядом.
        public void ClearIcons()
        {
            if (!clock.KeepIcons || !Visible) return;
            try { DeskIcons.Clear(Bounds, Screen.FromRectangle(Bounds).WorkingArea); }
            catch (Exception ex) { Log.Write("значки: " + ex.Message); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            tick.Start();
            icons.Start();
            ClearIcons();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tick.Dispose(); icons.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------- ещё не переехавшие виджеты

    class LegacyModule : Module
    {
        readonly string id, name, shrt, desc;
        readonly IconKind icon;

        public LegacyModule(string id, string name, string shrt, string desc, IconKind icon)
        {
            this.id = id; this.name = name; this.shrt = shrt; this.desc = desc; this.icon = icon;
        }

        public override string Id => id;
        public override string Name => name;
        public override string Short => shrt;
        public override string Description => desc;
        public override IconKind Icon => icon;
        public override bool Ported => false;
        public override bool Running => false;
        public override void Start() { }
        public override void Stop() { }
    }
}
