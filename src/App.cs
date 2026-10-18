using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Glasswing
{
    // Сердце приложения: список виджетов, значок в трее, общее стекло.
    class App
    {
        public const string Name = "Glasswing";
        public const string Version = "1.4.3";
        public const string ShowEventName = @"Local\Glasswing_Show";
        public const string ExitEventName = @"Local\Glasswing_Exit";

        public static App Core;
        public static Icon AppIcon;

        public static string Glass
        {
            get { return Settings.GetString("app", "Glass", "Лёгкое размытие"); }
        }

        public readonly List<Module> Modules = new List<Module>();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Control invoker = new Control();
        readonly System.Windows.Forms.Timer unhide = new System.Windows.Forms.Timer { Interval = 60 * 60 * 1000 };
        ControlCenter center;
        bool hidden;

        public App()
        {
            Modules.Add(new ClockModule());
            Modules.Add(new SysMonitorModule());
            Modules.Add(new NowPlayingModule());
            Modules.Add(new OverlayModule());
            Modules.Add(new WeatherModule());
            Modules.Add(new TodoModule());
            Modules.Add(new TranslatorModule());
            Modules.Add(new PetModule());
            invoker.CreateControl();
            var h = invoker.Handle;   // создаём окно для BeginInvoke из других потоков
            unhide.Tick += (s, e) => SetHiddenAll(false);
        }

        public void Start(bool firstRun)
        {
            tray.Icon = new Icon(AppIcon, SystemInformation.SmallIconSize);
            tray.Text = Name;
            tray.Visible = true;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowCenter(null); };
            tray.ContextMenuStrip = new ContextMenuStrip { Font = new Font("Segoe UI", 9.5f) };
            tray.ContextMenuStrip.Opening += (s, e) => BuildTrayMenu(tray.ContextMenuStrip);

            foreach (var m in Modules.Where(m => m.Ported && m.Enabled))
            {
                try { m.Start(); }
                catch (Exception ex) { Log.Write(m.Id + " запуск: " + ex.Message); }
            }

            ListenForSignals();
            Updater.Start(invoker);

            if (firstRun)
            {
                Settings.Set("app", "FirstRunDone", true);
                ShowCenter(null);
                tray.ShowBalloonTip(8000, Name, "Glasswing живёт здесь, в трее. Щелчок по значку открывает центр управления.", ToolTipIcon.Info);
            }
        }

        void BuildTrayMenu(ContextMenuStrip menu)
        {
            menu.Items.Clear();
            var open = new ToolStripMenuItem("Открыть центр управления", null, (s, e) => ShowCenter(null));
            open.Font = new Font("Segoe UI Semibold", 9.5f);
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());
            foreach (var m in Modules)
            {
                var mod = m;
                if (mod.Ported)
                    menu.Items.Add(new ToolStripMenuItem(mod.Name, null, (s, e) => SetEnabled(mod, !mod.Enabled)) { Checked = mod.Enabled });
                else
                    menu.Items.Add(new ToolStripMenuItem(mod.Name + " (скоро)") { Enabled = false });
            }
            menu.Items.Add(new ToolStripSeparator());
            var glass = new ToolStripMenuItem("Стекло");
            foreach (var name in Ui.GlassNames)
            {
                var n = name;
                glass.DropDownItems.Add(new ToolStripMenuItem(n, null, (s, e) => SetGlass(n)) { Checked = n == Glass });
            }
            menu.Items.Add(glass);
            menu.Items.Add(new ToolStripMenuItem(hidden ? "Показать виджеты" : "Спрятать всё на 1 час", null, (s, e) => SetHiddenAll(!hidden)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Проверить обновления", null, (s, e) => Updater.Check(true)));
            menu.Items.Add(new ToolStripMenuItem("Что нового в версии " + Version, null, (s, e) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/" + Updater.Repo + "/releases/tag/v" + Version) { UseShellExecute = true }); } catch { } }));
            menu.Items.Add(new ToolStripMenuItem("Закрыть " + Name, null, (s, e) => Exit()));
        }

        public void SetEnabled(Module m, bool on)
        {
            if (!m.Ported) return;
            m.Enabled = on;
            try
            {
                if (on) { m.Start(); if (hidden) m.SetHidden(true); }
                else m.Stop();
            }
            catch (Exception ex) { Log.Write(m.Id + ": " + ex.Message); }
            if (center != null && !center.IsDisposed) center.Invalidate();
        }

        public void SetGlass(string name)
        {
            // «для всех» значит для всех: своё стекло у виджетов сбрасывается
            foreach (var m in Modules.Concat(Modules.Where(x => x.Tabs != null).SelectMany(x => x.Tabs)))
                if (m.OwnGlass.Length > 0) Settings.Set(m.Id, "Glass", "", false);
            Settings.Set("app", "Glass", name);
            foreach (var m in Modules) m.GlassChanged();
            if (center != null && !center.IsDisposed) center.Invalidate();
        }

        void SetHiddenAll(bool hide)
        {
            hidden = hide;
            foreach (var m in Modules) m.SetHidden(hide);
            if (hide) { unhide.Stop(); unhide.Start(); } else unhide.Stop();
        }

        // Всплывающее сообщение у значка в трее: например, напоминание питомца.
        public void Notify(string title, string text)
        {
            try { tray.ShowBalloonTip(8000, title, text, ToolTipIcon.Info); } catch { }
        }

        public void ShowCenter(Module m)
        {
            if (center == null || center.IsDisposed) center = new ControlCenter();
            center.Select(m);
            WNative.Activate(center);
        }

        public void Exit()
        {
            tray.Visible = false;
            foreach (var m in Modules)
            {
                try { m.Stop(); } catch { }
            }
            if (center != null && !center.IsDisposed) { center.AllowClose = true; center.Close(); }
            Application.Exit();
        }

        // Второй запуск просит показать центр управления, а установщик новой версии просит закрыться.
        void ListenForSignals()
        {
            var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            var t = new Thread(() =>
            {
                while (true)
                {
                    int i = WaitHandle.WaitAny(new WaitHandle[] { show, exit });
                    try
                    {
                        if (i == 0) invoker.BeginInvoke(new Action(() => ShowCenter(null)));
                        else { invoker.BeginInvoke(new Action(Exit)); return; }
                    }
                    catch { return; }
                }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
