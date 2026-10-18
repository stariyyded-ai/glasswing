using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Glasswing
{
    // ---------------------------------------------------------------- оверлей: игровой хаб и папки-виджеты

    // Один пункт в центре управления, внутри две части со своими кнопками на столе.
    // Id остался от игрового хаба, чтобы прежнее «включено» сохранилось.
    class OverlayModule : Module
    {
        readonly Module[] tabs;
        public override string Id => "hub";
        public override string Name => "Оверлей";
        public override string Short => "Игровой хаб и папки";
        public override string Description => "Стеклянные кнопки, которые раскрываются в полку: игры из Steam и Epic или ярлыки из папки-виджета. Выберите, что настроить.";
        public override IconKind Icon => IconKind.Overlay;
        public override Module[] Tabs => tabs;
        public override bool Running => tabs.Any(t => t.Running);

        public OverlayModule()
        {
            tabs = new Module[] { new HubModule(), new FolderModule() };
            foreach (var t in tabs) t.Parent = this;
        }

        public override void Start()
        {
            // обе части были убраны, а оверлей снова включили: показываем обе
            if (tabs.All(t => !t.Enabled)) foreach (var t in tabs) t.Enabled = true;
            foreach (var t in tabs.Where(t => t.Enabled))
            {
                try { t.Start(); }
                catch (Exception ex) { Log.Write(t.Id + " запуск: " + ex.Message); }
            }
        }

        public override void Stop() { foreach (var t in tabs) t.Stop(); }
        public override void SetHidden(bool hidden) { foreach (var t in tabs) t.SetHidden(hidden); }
        public override void GlassChanged() { foreach (var t in tabs) t.GlassChanged(); }
    }

    // ---------------------------------------------------------------- игровой хаб

    class HubModule : Module
    {
        HubCard card;
        bool hidden;
        public override string Id => "hub";
        public override string Name => "Игровой хаб";
        public override string Short => "Игры из Steam и Epic";
        public override string Description => "Кнопка с геймпадом раскрывает полку с обложками игр из Steam, Epic Games и ваших ярлыков, недавние сверху.";
        public override IconKind Icon => IconKind.Games;
        protected override string EnabledKey => "Shown";
        public override bool Running => card != null && !card.IsDisposed;
        public override bool CanReset => true;

        // Ярлыки игр, добавленных вручную.
        public static readonly string MyGamesDir = Path.Combine(Settings.Dir, "Мои игры");
        public static readonly string[] SourceNames = { "Steam и Epic", "Только Steam", "Только Epic", "Без магазинов" };

        public int Sources
        {
            get { return Math.Max(0, Math.Min(SourceNames.Length - 1, Settings.GetInt(Id, "Sources", 0))); }
            set { Settings.Set(Id, "Sources", value); Refresh(); }
        }

        public bool Steam => Sources == 0 || Sources == 1;
        public bool Epic => Sources == 0 || Sources == 2;

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

        public List<string> HiddenKeys()
        {
            object o;
            var list = Settings.Section(Id).TryGetValue("Hidden", out o) ? o as List<object> : null;
            return list == null ? new List<string>() : list.OfType<string>().ToList();
        }

        public void HideKey(string key)
        {
            var list = HiddenKeys();
            if (!list.Contains(key)) list.Add(key);
            Settings.Set(Id, "Hidden", list.Cast<object>().ToList());
        }

        public void RestoreHidden()
        {
            Settings.Set(Id, "Hidden", new List<object>());
            Refresh();
        }

        // Когда игру запускали из хаба: для Epic и своих ярлыков другого источника нет.
        public Dictionary<string, DateTime> Launched()
        {
            var res = new Dictionary<string, DateTime>();
            object o;
            var d = Settings.Section(Id).TryGetValue("Launched", out o) ? o as Dictionary<string, object> : null;
            if (d == null) return res;
            foreach (var kv in d)
            {
                DateTime t;
                var s = kv.Value as string;
                if (s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t))
                    res[kv.Key] = t.Kind == DateTimeKind.Utc ? t.ToLocalTime() : t;
            }
            return res;
        }

        public void MarkLaunched(string key)
        {
            object o;
            var d = Settings.Section(Id).TryGetValue("Launched", out o) ? o as Dictionary<string, object> : null;
            if (d == null) d = new Dictionary<string, object>();
            d[key] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            Settings.Set(Id, "Launched", d);
        }

        public void Refresh() { if (Running) card.Rescan(); }

        public override void Start()
        {
            if (Running) return;
            try { Directory.CreateDirectory(MyGamesDir); } catch { }
            card = new HubCard(this);
            card.PlaceFromSettings();
            card.Show();
            if (hidden) card.SetHidden(true);
        }

        public override void Stop()
        {
            if (!Running) return;
            card.Close();
            card.Dispose();
            card = null;
        }

        public override void SetHidden(bool hide) { hidden = hide; if (Running) card.SetHidden(hide); }
        public override void GlassChanged() { if (Running) card.ApplyGlass(); }
        public override bool HasGlass => true;
        public override void ResetPosition() { if (Running) { card.SetExpanded(false); card.ResetPosition(); } }

        static string Games(int n)
        {
            int a = n % 10, b = n % 100;
            string w = a == 1 && b != 11 ? "игра" : a >= 2 && a <= 4 && (b < 12 || b > 14) ? "игры" : "игр";
            return n + " " + w;
        }

        public override IEnumerable<Row> Rows()
        {
            yield return ShownRow();
            yield return new Row { Label = "Игры из магазинов", Value = () => SourceNames[Sources], Click = () => Sources = (Sources + 1) % SourceNames.Length };
            yield return new Row
            {
                Label = "Обновить список игр",
                Value = () => !Running ? "" : card.Scanning ? "ищу игры…" : "на полке " + Games(card.Count),
                Click = Refresh,
            };
            yield return new Row
            {
                Label = "Вернуть скрытые игры",
                Value = () => { int n = HiddenKeys().Count; return n == 0 ? "скрытых нет" : "скрыто: " + n; },
                Click = RestoreHidden,
            };
            yield return new Row { Label = "Поверх всех окон", On = () => TopMost, Click = () => TopMost = !TopMost };
            yield return new Row { Label = "Закрепить на месте", On = () => Locked, Click = () => Locked = !Locked };
        }
    }

    class HubGame
    {
        public string Key, Name, Source, Launch, Folder, Cover, IconPath, ArtKey;
        public int Minutes;
        public DateTime Last = DateTime.MinValue;
    }

    // Картинка плитки: обложка во всю плитку или крупный значок на стеклянной карточке.
    class HubArt
    {
        public Bitmap Bmp;
        public bool IsCover;
    }

    class HubCard : GlassCard
    {
        static readonly Color ColText = Color.White;
        static readonly Color ColMuted = Color.FromArgb(190, 255, 255, 255);
        static readonly Color ColCard = Color.FromArgb(40, 255, 255, 255);
        static readonly Color ColHotLine = Color.FromArgb(220, 255, 255, 255);
        static readonly Color ColHover = Color.FromArgb(8, 255, 255, 255);
        static readonly StringFormat TileFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit };
        static readonly StringFormat CenterWrap = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        readonly HubModule hub;
        readonly HubScanner scanner;
        readonly ToolTip tip = new ToolTip();
        readonly int btnSz = Ui.S(56), tileW = Ui.S(76), tileH = Ui.S(114), gap = Ui.S(8), pad = Ui.S(12), stripH = Ui.S(36);
        readonly int gridTop;
        readonly Dictionary<string, HubArt> art = new Dictionary<string, HubArt>();
        List<HubGame> items = new List<HubGame>();
        bool expanded, up, hoverBtn, scannedOnce;
        int hot = -1, scroll, asked, got;
        int expW, expH;
        Size shelf;
        Point home;
        HubGame downGame, menuGame;
        DateTime scanned = DateTime.MinValue;

        public HubCard(HubModule m) : base(m)
        {
            hub = m;
            gridTop = stripH + Ui.S(2);
            expW = Settings.GetInt(m.Id, "ShelfW", Ui.S(540));
            expH = Settings.GetInt(m.Id, "ShelfH", Ui.S(310));
            AllowDrop = true;
            scanner = new HubScanner(tileW, tileH, Deliver);
            tip.SetToolTip(this, m.Name);
        }

        public bool Scanning => asked != got;
        public int Count => items.Count;

        static int S(double v) { return Ui.S(v); }
        static float F(double v) { return (float)(v * Ui.Scale); }

        protected override Size CardSize() { return expanded ? shelf : new Size(btnSz, btnSz); }

        protected override Point DefaultLocation(Rectangle wa)
        {
            return new Point(wa.Right - btnSz - S(32), wa.Top + S(160));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (asked == 0) Rescan();
        }

        public void SetHidden(bool hide)
        {
            if (hide) SetExpanded(false);
            Visible = !hide;
        }

        // ---------------------------------------------------------------- список игр

        public void Rescan()
        {
            asked++;
            scanner.Request(new HubQuery
            {
                Id = asked,
                Steam = hub.Steam,
                Epic = hub.Epic,
                Hidden = new HashSet<string>(hub.HiddenKeys()),
                Launched = hub.Launched(),
            });
            Invalidate();
        }

        // Поток поиска ждёт, пока окно примет список: так он точно знает, какие картинки уже загружены.
        void Deliver(HubResult r)
        {
            bool taken = false;
            try
            {
                if (!IsDisposed && IsHandleCreated) Invoke(new Action(() => { taken = true; Apply(r); }));
            }
            catch (Exception) { }
            if (!taken) r.DisposeArt();
        }

        void Apply(HubResult r)
        {
            if (IsDisposed) { r.DisposeArt(); return; }
            try
            {
                foreach (var kv in r.Art)
                {
                    if (art.ContainsKey(kv.Key)) { if (kv.Value.Bmp != null) kv.Value.Bmp.Dispose(); }
                    else art[kv.Key] = kv.Value;
                }
                items = r.Games;
                var used = new HashSet<string>(items.Where(g => g.ArtKey != null).Select(g => g.ArtKey));
                foreach (var k in art.Keys.Where(k => !used.Contains(k)).ToList())
                {
                    if (art[k].Bmp != null) art[k].Bmp.Dispose();
                    art.Remove(k);
                }
                scanner.Known = new HashSet<string>(art.Keys);
                got = r.Id;
                scannedOnce = true;
                scanned = DateTime.Now;
                hot = -1;
                scroll = Math.Min(scroll, MaxScroll());
                if (expanded && MouseButtons == MouseButtons.None) Relayout();   // не мешаем тянуть край полки
                Invalidate();
            }
            catch (Exception ex) { Log.Write("хаб: " + ex.Message); }
        }

        static string Info(HubGame g)
        {
            var parts = new List<string> { g.Name };
            if (g.Minutes >= 60) parts.Add(Math.Round(g.Minutes / 60.0) + " ч");
            else if (g.Minutes > 0) parts.Add(g.Minutes + " мин");
            if (g.Last > new DateTime(2000, 1, 1))
            {
                int days = (DateTime.Today - g.Last.Date).Days;
                parts.Add(days <= 0 ? "сегодня" : days == 1 ? "вчера" : days < 7 ? days + " дн. назад" : g.Last.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            }
            return string.Join("  ·  ", parts);
        }

        void Warn(string text)
        {
            MessageBox.Show(text, Mod.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void Play(HubGame g)
        {
            try
            {
                Process.Start(new ProcessStartInfo(g.Launch) { UseShellExecute = true });
                hub.MarkLaunched(g.Key);
                g.Last = DateTime.Now;
                scanned = DateTime.MinValue;   // при следующем раскрытии игра встанет первой
                SetExpanded(false);
            }
            catch (Exception ex) { Warn("Не удалось запустить «" + g.Name + "»:\n" + ex.Message); }
        }

        void OpenFolder(HubGame g)
        {
            string dir = g.Folder;
            if (dir == null && g.IconPath != null)
            {
                // у своего ярлыка открываем папку, куда он ведёт
                if (g.IconPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                        string target = shell.CreateShortcut(g.IconPath).TargetPath;
                        if (!string.IsNullOrEmpty(target)) dir = Path.GetDirectoryName(target);
                    }
                    catch { }
                }
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Path.GetDirectoryName(g.IconPath);
            }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { Warn("Папка игры не найдена."); return; }
            try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); }
            catch (Exception ex) { Warn(ex.Message); }
        }

        void HideGame(HubGame g)
        {
            if (g.Source == HubLibrary.OwnSource) { try { File.Delete(g.Launch); } catch { } }
            else hub.HideKey(g.Key);
            items = items.Where(x => x != g).ToList();
            hot = -1;
            Rescan();
        }

        void AddDialog()
        {
            using (var dlg = new OpenFileDialog { Filter = "Игры и ярлыки (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url", Title = "Выберите игру" })
                if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(new[] { dlg.FileName });
        }

        void AddFiles(string[] paths)
        {
            try { Directory.CreateDirectory(HubModule.MyGamesDir); } catch { }
            foreach (var p in paths)
            {
                string ext = Path.GetExtension(p).ToLowerInvariant();
                try
                {
                    if (ext == ".exe")
                    {
                        // сам .exe не трогаем, делаем на него ярлык
                        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                        dynamic s = shell.CreateShortcut(Path.Combine(HubModule.MyGamesDir, Path.GetFileNameWithoutExtension(p) + ".lnk"));
                        s.TargetPath = p;
                        s.WorkingDirectory = Path.GetDirectoryName(p);
                        s.Save();
                    }
                    else if (ext == ".lnk" || ext == ".url")
                        File.Copy(p, Path.Combine(HubModule.MyGamesDir, Path.GetFileName(p)), true);
                }
                catch (Exception ex) { Warn("Не удалось добавить " + p + "\n" + ex.Message); }
            }
            Rescan();
        }

        // ---------------------------------------------------------------- геометрия

        int Columns()
        {
            int avail = ClientSize.Width - 2 * pad;
            return Math.Max(1, (int)Math.Floor((avail + gap) / (double)(tileW + gap)));
        }

        Rectangle TileRect(int i)
        {
            int cols = Columns();
            int row = i / cols, col = i % cols;
            int gridW = cols * tileW + (cols - 1) * gap;
            int x0 = (ClientSize.Width - gridW) / 2;
            return new Rectangle(x0 + col * (tileW + gap), gridTop + row * (tileH + gap) - scroll, tileW, tileH);
        }

        int MaxScroll()
        {
            int n = items.Count;
            if (n == 0) return 0;
            int cols = Columns();
            int rows = (n + cols - 1) / cols;
            int contentH = rows * tileH + (rows - 1) * gap;
            int viewH = ClientSize.Height - gridTop - pad;
            return Math.Max(0, contentH - viewH);
        }

        int HitIndex(Point p)
        {
            if (!expanded || p.Y < gridTop) return -1;
            for (int i = 0; i < items.Count; i++) if (TileRect(i).Contains(p)) return i;
            return -1;
        }

        // Край полки, за который её можно растянуть: код для WM_NCLBUTTONDOWN или 0.
        int EdgeHit(Point p) { return expanded ? Shelf.EdgeHit(p, ClientSize, up) : 0; }

        public void SetExpanded(bool on)
        {
            if (expanded == on) return;
            if (on)
            {
                home = Location;
                expanded = true;
                // список обновляется при раскрытии, если с прошлого раза прошла минута
                if ((DateTime.Now - scanned).TotalSeconds > 60 && !Scanning) Rescan();
                Relayout();
                tip.SetToolTip(this, null);
            }
            else
            {
                expanded = false;
                MinimumSize = Size.Empty;
                Bounds = new Rectangle(home, new Size(btnSz, btnSz));
                hot = -1;
                scroll = 0;
                tip.SetToolTip(this, Mod.Name);
            }
            Invalidate();
        }

        // Полка раскрывается от кнопки вниз, а если внизу не хватает места, то вверх.
        void Relayout()
        {
            var wa = Screen.FromPoint(home).WorkingArea;
            int fullW = Math.Min(expW, wa.Width), fullH = Math.Min(expH, wa.Height);
            // полка не выше, чем нужно для всех игр: без пустого места снизу
            int cols = Math.Max(1, (int)Math.Floor((fullW - 2 * pad + gap) / (double)(tileW + gap)));
            int rows = Math.Max(1, (items.Count + cols - 1) / cols);
            fullH = Math.Min(fullH, gridTop + rows * tileH + (rows - 1) * gap + pad);
            int x = Math.Max(wa.Left, Math.Min(home.X, wa.Right - fullW));
            up = home.Y + fullH > wa.Bottom;
            int y = up ? home.Y + btnSz - fullH : home.Y;
            MinimumSize = new Size(tileW + 2 * pad + S(20), gridTop + tileH + pad);
            shelf = new Size(fullW, fullH);
            Bounds = new Rectangle(x, y, fullW, fullH);
        }

        void SaveHome()
        {
            home = new Point(Left, expanded && up ? Bottom - btnSz : Top);
            Settings.Set(Mod.Id, "X", home.X, false);
            Settings.Set(Mod.Id, "Y", home.Y);
        }

        protected override void OnCardMoved() { SaveHome(); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (expanded) shelf = Size;
            scroll = Math.Min(scroll, MaxScroll());
            Invalidate();
        }

        // ---------------------------------------------------------------- мышь

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!expanded)
            {
                if (!hoverBtn) { hoverBtn = true; Invalidate(); }
                Cursor = Cursors.Hand;
                return;
            }
            int edge = Locked ? 0 : EdgeHit(e.Location);
            int h = edge != 0 ? -1 : HitIndex(e.Location);
            Cursor = edge != 0 ? Shelf.EdgeCursor(edge) : h >= 0 || e.Y < stripH ? Cursors.Hand : Cursors.Default;
            if (h != hot) { hot = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot != -1 || hoverBtn) { hot = -1; hoverBtn = false; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Clicks > 1) return;   // двойной щелчок не сворачивает только что раскрытую полку
            base.OnMouseDown(e);
        }

        // Перетаскивание; простой щелчок раскрывает полку или (по строке сверху) сворачивает её.
        protected override bool HandleMouseDown(Point p)
        {
            if (expanded)
            {
                int edge = Locked ? 0 : EdgeHit(p);
                if (edge != 0)
                {
                    WNative.ReleaseCapture();
                    WNative.SendMessage(Handle, 0xA1, (IntPtr)edge, IntPtr.Zero);   // растягивание
                    expW = Width;
                    expH = Height;
                    Settings.Set(Mod.Id, "ShelfW", expW, false);
                    Settings.Set(Mod.Id, "ShelfH", expH);
                    return true;
                }
                int i = HitIndex(p);
                if (i >= 0) { downGame = items[i]; return true; }
            }
            bool inStrip = p.Y < stripH;
            if (!Locked)
            {
                var before = Location;
                WNative.DragWindow(this);
                if (Location != before) { SaveHome(); return true; }
            }
            if (!expanded) SetExpanded(true);
            else if (inStrip) SetExpanded(false);
            return true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && downGame != null)
            {
                var g = downGame;
                downGame = null;
                int i = HitIndex(e.Location);
                if (i >= 0 && items[i] == g) Play(g);
            }
            if (e.Button == MouseButtons.Right)
            {
                int i = HitIndex(e.Location);
                menuGame = i >= 0 ? items[i] : null;
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!expanded) return;
            scroll = Math.Max(0, Math.Min(MaxScroll(), scroll - (int)Math.Round(e.Delta / 120.0 * (tileH + gap) / 2)));
            hot = HitIndex(e.Location);
            Invalidate();
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            if (e.Effect != DragDropEffects.None) SetExpanded(true);
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) AddFiles(files);
        }

        // Пункты игры и полки идут первыми, общие пункты Glasswing ниже.
        protected override void AddMenuItems(ContextMenuStrip menu)
        {
            int at = 0;
            var g = menuGame;
            if (g != null)
            {
                menu.Items.Insert(at++, new ToolStripMenuItem("Играть", null, (s, e) => Play(g)) { Font = new Font(menu.Font, FontStyle.Bold) });
                menu.Items.Insert(at++, new ToolStripMenuItem("Открыть папку игры", null, (s, e) => OpenFolder(g)));
                menu.Items.Insert(at++, new ToolStripMenuItem("Скрыть из хаба", null, (s, e) => HideGame(g)));
                menu.Items.Insert(at++, new ToolStripSeparator());
            }
            menu.Items.Insert(at++, new ToolStripMenuItem("Обновить список игр", null, (s, e) => Rescan()));
            menu.Items.Insert(at++, new ToolStripMenuItem("Добавить игру…", null, (s, e) => AddDialog()));
            menu.Items.Insert(at++, new ToolStripMenuItem("Вернуть скрытые игры", null, (s, e) => hub.RestoreHidden()) { Enabled = hub.HiddenKeys().Count > 0 });
        }

        // ---------------------------------------------------------------- отрисовка

        protected override void DrawContent(Graphics g, int w, int h)
        {
            if (expanded) DrawShelf(g, w, h);
            else DrawButton(g, w, h);
        }

        void DrawButton(Graphics g, int cw, int ch)
        {
            if (hoverBtn) using (var hb = new SolidBrush(ColHover)) g.FillRectangle(hb, 0, 0, cw, ch);
            // значок геймпада
            using (var pen = new Pen(ColText, Math.Max(1.5f, F(1.7))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var white = new SolidBrush(ColText))
            {
                float bw = F(28), bh = F(18);
                var body = new RectangleF((cw - bw) / 2, (ch - bh) / 2, bw, bh);
                using (var path = Ui.Round(body, bh / 2)) g.DrawPath(pen, path);
                float cx = body.X + bh / 2 + F(1), cy = body.Y + bh / 2, a = F(3.5);
                g.DrawLine(pen, cx - a, cy, cx + a, cy);
                g.DrawLine(pen, cx, cy - a, cx, cy + a);
                float dot = F(3.5);
                g.FillEllipse(white, body.Right - bh / 2 - F(4), cy - dot / 2 - F(2), dot, dot);
                g.FillEllipse(white, body.Right - bh / 2 + F(0.5), cy - dot / 2 + F(2), dot, dot);
            }
            Shelf.DrawBadge(g, cw, items.Count);   // число игр
        }

        void DrawShelf(Graphics g, int cw, int ch)
        {
            // строка сверху: название наведённой игры, часы, когда играл
            string text;
            Font font;
            Color col;
            if (hot >= 0 && hot < items.Count) { text = Info(items[hot]); font = Ui.Font(12); col = ColText; }
            else { text = !scannedOnce ? "Ищу игры…" : "Игры  ·  " + items.Count; font = Ui.Font(13, true); col = ColMuted; }
            Shelf.DrawStrip(g, text, font, col, cw, pad, stripH);

            var grid = new Rectangle(0, gridTop, cw, ch - gridTop);
            g.SetClip(grid);
            if (items.Count == 0)
            {
                var box = new RectangleF(pad, gridTop + S(2), cw - 2 * pad, ch - gridTop - pad - S(2));
                string empty = !scannedOnce ? "Ищу игры…" : "Игры не найдены.\nПеретащите сюда ярлык или .exe игры";
                Ui.Text2(g, empty, Ui.Font(12.5), ColMuted, box, CenterWrap);
            }
            float radius = S(8);
            for (int i = 0; i < items.Count; i++)
            {
                var rc = TileRect(i);
                if (rc.Bottom < gridTop || rc.Top > ch) continue;
                var game = items[i];
                HubArt a;
                if (game.ArtKey == null || !art.TryGetValue(game.ArtKey, out a)) a = null;
                bool isHot = i == hot;
                RectangleF r = rc;
                if (isHot) r = new RectangleF(rc.X - S(2), rc.Y - S(3), rc.Width + S(4), rc.Height + S(4));
                using (var path = Ui.Round(r, radius))
                {
                    if (a != null && a.IsCover && a.Bmp != null)
                    {
                        g.SetClip(path, CombineMode.Intersect);
                        g.DrawImage(a.Bmp, r);
                        g.SetClip(grid);
                    }
                    else
                    {
                        // нет обложки: стеклянная карточка с крупным значком и названием
                        using (var cb = new SolidBrush(ColCard)) g.FillPath(cb, path);
                        if (a != null && a.Bmp != null)
                        {
                            float isz = S(40);
                            g.DrawImage(a.Bmp, r.X + (r.Width - isz) / 2, r.Y + S(18), isz, isz);
                        }
                        var tr = new RectangleF(r.X + S(5), r.Y + S(64), r.Width - S(10), r.Height - S(70));
                        Ui.Text2(g, game.Name, Ui.Font(11, true), ColText, tr, TileFormat, true);
                    }
                    if (isHot) using (var hp = new Pen(ColHotLine, Math.Max(1.5f, F(2)))) g.DrawPath(hp, path);
                }
            }
            Shelf.DrawScroll(g, cw, ch, gridTop, pad, scroll, MaxScroll());
            g.ResetClip();
            Shelf.DrawGrip(g, cw, ch, up, ColMuted);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                scanner.Stop();
                tip.Dispose();
                foreach (var a in art.Values) if (a.Bmp != null) a.Bmp.Dispose();
                art.Clear();
            }
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------- поиск игр в своём потоке

    class HubQuery
    {
        public int Id;
        public bool Steam, Epic;
        public HashSet<string> Hidden;
        public Dictionary<string, DateTime> Launched;
    }

    class HubResult
    {
        public int Id;
        public List<HubGame> Games = new List<HubGame>();
        public Dictionary<string, HubArt> Art = new Dictionary<string, HubArt>();

        public void DisposeArt()
        {
            foreach (var a in Art.Values) if (a.Bmp != null) a.Bmp.Dispose();
            Art.Clear();
        }
    }

    // Чтение библиотек и обложек идёт не в окне: на больших библиотеках это заметные секунды.
    sealed class HubScanner
    {
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Action<HubResult> publish;
        readonly int tileW, tileH;
        HubQuery next;
        Thread thread;
        volatile HashSet<string> known = new HashSet<string>();
        volatile bool stopped;

        public HubScanner(int tileW, int tileH, Action<HubResult> publish)
        {
            this.tileW = tileW;
            this.tileH = tileH;
            this.publish = publish;
        }

        // Картинки, которые окно уже держит: их не грузим заново.
        public HashSet<string> Known { set { known = value; } }

        public void Request(HubQuery q)
        {
            if (stopped) return;
            Interlocked.Exchange(ref next, q);
            if (thread == null)
            {
                // значки берём у оболочки Windows, ей нужен STA-поток
                thread = new Thread(Loop) { IsBackground = true, Name = "hub scan" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
            wake.Set();
        }

        public void Stop()
        {
            stopped = true;
            wake.Set();
        }

        void Loop()
        {
            while (true)
            {
                wake.WaitOne();
                if (stopped) return;
                var q = Interlocked.Exchange(ref next, null);
                if (q == null) continue;
                HubResult r;
                try { r = Scan(q); }
                catch (Exception ex) { Log.Write("хаб поиск: " + ex.Message); r = new HubResult { Id = q.Id }; }
                if (stopped) { r.DisposeArt(); return; }
                publish(r);
            }
        }

        HubResult Scan(HubQuery q)
        {
            var all = new List<HubGame>();
            if (q.Steam) try { all.AddRange(HubLibrary.Steam()); } catch (Exception ex) { Log.Write("хаб Steam: " + ex.Message); }
            if (q.Epic) try { all.AddRange(HubLibrary.Epic()); } catch (Exception ex) { Log.Write("хаб Epic: " + ex.Message); }
            try { all.AddRange(HubLibrary.Own()); } catch (Exception ex) { Log.Write("хаб ярлыки: " + ex.Message); }
            foreach (var g in all)
            {
                DateTime l;
                if (q.Launched.TryGetValue(g.Key, out l) && l > g.Last) g.Last = l;
            }
            var r = new HubResult { Id = q.Id };
            r.Games = all.Where(g => !q.Hidden.Contains(g.Key))
                .OrderByDescending(g => g.Last).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var have = known;
            foreach (var g in r.Games)
            {
                if (stopped) break;
                g.ArtKey = HubLibrary.ArtKey(g);
                if (g.ArtKey == null || have.Contains(g.ArtKey) || r.Art.ContainsKey(g.ArtKey)) continue;
                r.Art[g.ArtKey] = HubLibrary.LoadArt(g, tileW, tileH);
            }
            return r;
        }
    }

    // Где лежат игры: Steam (реестр и файлы .vdf/.acf), Epic (манифесты .item) и папка своих ярлыков.
    static class HubLibrary
    {
        public const string OwnSource = "Ярлык";
        static readonly Regex SkipSteam = new Regex(@"Steamworks Common Redistributables|Steam Linux Runtime|^Proton|Steam VR|SteamVR|Wallpaper Engine|Dedicated Server|Soundtrack", RegexOptions.IgnoreCase);
        static readonly Regex VdfToken = new Regex(@"""((?:[^""\\]|\\.)*)""|\{|\}");

        // Минимальный разбор формата Valve KeyValues во вложенные словари (ключи без учёта регистра)
        static Dictionary<string, object> ReadVdf(string path)
        {
            var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return root;
            string text = File.ReadAllText(path);
            var stack = new Stack<Dictionary<string, object>>();
            var cur = root;
            string key = null;
            foreach (Match m in VdfToken.Matches(text))
            {
                string v = m.Value;
                if (v == "{")
                {
                    var nd = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    if (key != null) cur[key] = nd;
                    stack.Push(cur);
                    cur = nd;
                    key = null;
                }
                else if (v == "}")
                {
                    if (stack.Count > 0) cur = stack.Pop();
                    key = null;
                }
                else
                {
                    string s = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (key == null) key = s;
                    else { cur[key] = s; key = null; }
                }
            }
            return root;
        }

        static object Node(object h, params string[] keys)
        {
            foreach (var k in keys)
            {
                var d = h as Dictionary<string, object>;
                if (d == null || !d.TryGetValue(k, out h)) return null;
            }
            return h;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object o;
            return d != null && d.TryGetValue(key, out o) && o is string ? (string)o : "";
        }

        static DateTime Unix(string sec)
        {
            long n;
            if (long.TryParse(sec, out n) && n > 0) return DateTimeOffset.FromUnixTimeSeconds(n).LocalDateTime;
            return DateTime.MinValue;
        }

        static string SteamCover(string steam, string id)
        {
            string lc = Path.Combine(steam, "appcache", "librarycache");
            foreach (var p in new[] { Path.Combine(lc, id + "_library_600x900.jpg"), Path.Combine(lc, id, "library_600x900.jpg") })
                if (File.Exists(p)) return p;
            string d = Path.Combine(lc, id);
            if (Directory.Exists(d))
            {
                foreach (var pattern in new[] { "library_600x900*", "library_capsule*", "header*" })
                {
                    try
                    {
                        var f = Directory.EnumerateFiles(d, pattern, SearchOption.AllDirectories).FirstOrDefault();
                        if (f != null) return f;
                    }
                    catch { }
                }
            }
            string h = Path.Combine(lc, id + "_header.jpg");
            return File.Exists(h) ? h : null;
        }

        public static List<HubGame> Steam()
        {
            var res = new List<HubGame>();
            string steam = null;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (k != null) steam = k.GetValue("SteamPath") as string;
            if (string.IsNullOrEmpty(steam)) return res;
            steam = steam.Replace('/', '\\');

            var libs = new List<string> { steam };
            var lf = Node(ReadVdf(Path.Combine(steam, "steamapps", "libraryfolders.vdf")), "libraryfolders") as Dictionary<string, object>;
            if (lf != null)
            {
                foreach (var kv in lf)
                {
                    var p = Node(kv.Value, "path") as string;
                    if (p == null && kv.Value is string && kv.Key.All(char.IsDigit)) p = (string)kv.Value;   // старый формат файла
                    if (!string.IsNullOrEmpty(p)) libs.Add(p);
                }
            }

            // часы в игре и дата последнего запуска — из настроек пользователей Steam
            var play = new Dictionary<string, Tuple<int, DateTime>>();
            string ud = Path.Combine(steam, "userdata");
            if (Directory.Exists(ud))
            {
                foreach (var u in Directory.GetDirectories(ud))
                {
                    Dictionary<string, object> apps;
                    try { apps = Node(ReadVdf(Path.Combine(u, "config", "localconfig.vdf")), "UserLocalConfigStore", "Software", "Valve", "Steam", "apps") as Dictionary<string, object>; }
                    catch { continue; }
                    if (apps == null) continue;
                    foreach (var kv in apps)
                    {
                        var a = kv.Value as Dictionary<string, object>;
                        if (a == null) continue;
                        int min;
                        int.TryParse(Str(a, "Playtime"), out min);
                        var last = Unix(Str(a, "LastPlayed"));
                        Tuple<int, DateTime> old;
                        if (play.TryGetValue(kv.Key, out old)) play[kv.Key] = Tuple.Create(Math.Max(min, old.Item1), old.Item2 > last ? old.Item2 : last);
                        else play[kv.Key] = Tuple.Create(min, last);
                    }
                }
            }

            var seen = new HashSet<string>();
            foreach (var lib in libs)
            {
                string sa;
                string[] acfs;
                try
                {
                    sa = Path.Combine(lib, "steamapps");
                    if (!Directory.Exists(sa)) continue;
                    acfs = Directory.GetFiles(sa, "appmanifest_*.acf");
                }
                catch { continue; }
                foreach (var acf in acfs)
                {
                    Dictionary<string, object> st;
                    try { st = Node(ReadVdf(acf), "AppState") as Dictionary<string, object>; }
                    catch { continue; }
                    if (st == null) continue;
                    string id = Str(st, "appid"), name = Str(st, "name");
                    if (id.Length == 0 || name.Length == 0 || seen.Contains(id) || SkipSteam.IsMatch(name) || id == "228980") continue;
                    seen.Add(id);
                    Tuple<int, DateTime> p;
                    play.TryGetValue(id, out p);
                    var last = Unix(Str(st, "LastPlayed"));
                    if (p != null && p.Item2 > last) last = p.Item2;
                    string folder = null;
                    try { folder = Path.Combine(sa, "common", Str(st, "installdir")); } catch { }
                    res.Add(new HubGame
                    {
                        Key = "steam:" + id, Name = name, Source = "Steam",
                        Launch = "steam://rungameid/" + id,
                        Folder = folder,
                        Minutes = p != null ? p.Item1 : 0, Last = last,
                        Cover = SteamCover(steam, id),
                    });
                }
            }
            return res;
        }

        public static List<HubGame> Epic()
        {
            var res = new List<HubGame>();
            string man = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(man)) return res;
            foreach (var f in Directory.GetFiles(man, "*.item"))
            {
                try
                {
                    var j = Json.Parse(File.ReadAllText(f, Encoding.UTF8).TrimStart('﻿')) as Dictionary<string, object>;
                    if (j == null) continue;
                    object o;
                    if (j.TryGetValue("bIsApplication", out o) && o is bool && !(bool)o) continue;
                    var cats = j.TryGetValue("AppCategories", out o) ? o as List<object> : null;
                    if (cats != null && cats.Count > 0 && !cats.OfType<string>().Any(c => string.Equals(c, "games", StringComparison.OrdinalIgnoreCase))) continue;
                    string name = Str(j, "DisplayName");
                    if (name.Length == 0) continue;
                    string loc = Str(j, "InstallLocation"), exe = Str(j, "LaunchExecutable");
                    string exePath = loc.Length > 0 && exe.Length > 0 ? Path.Combine(loc, exe) : null;
                    res.Add(new HubGame
                    {
                        Key = "epic:" + Str(j, "AppName"), Name = name, Source = "Epic",
                        Launch = "com.epicgames.launcher://apps/" + Str(j, "CatalogNamespace") + "%3A" + Str(j, "CatalogItemId") + "%3A" + Str(j, "AppName") + "?action=launch&silent=true",
                        Folder = loc.Length > 0 ? loc : null,
                        IconPath = exePath != null && File.Exists(exePath) ? exePath : null,
                    });
                }
                catch { }
            }
            return res;
        }

        public static List<HubGame> Own()
        {
            var res = new List<HubGame>();
            string dir = HubModule.MyGamesDir;
            if (!Directory.Exists(dir)) return res;
            foreach (var f in Directory.GetFiles(dir))
            {
                var attr = File.GetAttributes(f);
                if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;   // desktop.ini и т. п.
                string name = HubShell.DisplayName(f);
                if (string.IsNullOrEmpty(name)) name = Path.GetFileNameWithoutExtension(f);
                res.Add(new HubGame { Key = "file:" + Path.GetFileName(f), Name = name, Source = OwnSource, Launch = f, IconPath = f });
            }
            return res;
        }

        // Ключ картинки: обложка меняется вместе с датой файла.
        public static string ArtKey(HubGame g)
        {
            if (g.Cover != null)
            {
                try { return "c:" + g.Cover + "|" + File.GetLastWriteTimeUtc(g.Cover).Ticks; } catch { }
            }
            return g.IconPath != null ? "i:" + g.IconPath : null;
        }

        // Обложка, уменьшенная до размера плитки (файл не блокируется), иначе крупный значок.
        public static HubArt LoadArt(HubGame g, int tileW, int tileH)
        {
            if (g.Cover != null)
            {
                try
                {
                    using (var ms = new MemoryStream(File.ReadAllBytes(g.Cover)))
                    using (var src = Image.FromStream(ms))
                    {
                        var bmp = new Bitmap(tileW, tileH, PixelFormat.Format32bppPArgb);
                        using (var gr = Graphics.FromImage(bmp))
                        {
                            gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            // заполняем плитку, обрезая лишнее (как object-fit: cover)
                            double k = Math.Max(tileW / (double)src.Width, tileH / (double)src.Height);
                            double dw = src.Width * k, dh = src.Height * k;
                            gr.DrawImage(src, (float)((tileW - dw) / 2), (float)((tileH - dh) / 2), (float)dw, (float)dh);
                        }
                        return new HubArt { Bmp = bmp, IsCover = true };
                    }
                }
                catch { }
            }
            return new HubArt { Bmp = g.IconPath != null ? HubShell.BigIcon(g.IconPath) : null };
        }
    }

    // Значки и имена файлов, как их показывает Проводник.
    static class HubShell
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImageList
        {
            [PreserveSig] int Add(IntPtr bmp, IntPtr mask, ref int index);
            [PreserveSig] int ReplaceIcon(int i, IntPtr icon, ref int index);
            [PreserveSig] int SetOverlayImage(int image, int overlay);
            [PreserveSig] int Replace(int i, IntPtr bmp, IntPtr mask);
            [PreserveSig] int AddMasked(IntPtr bmp, int mask, ref int index);
            [PreserveSig] int Draw(IntPtr p);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, ref IntPtr icon);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("shell32.dll", EntryPoint = "#727")] static extern int SHGetImageList(int list, ref Guid riid, out IImageList ppv);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        static Bitmap FromHandle(IntPtr h)
        {
            try { using (var ic = Icon.FromHandle(h)) return ic.ToBitmap(); }
            finally { DestroyIcon(h); }
        }

        static Bitmap ShellIcon(string path)
        {
            var i = new SHFILEINFO();
            SHGetFileInfo(path, 0, ref i, (uint)Marshal.SizeOf(i), 0x100u);
            return i.hIcon == IntPtr.Zero ? null : FromHandle(i.hIcon);
        }

        // Крупный значок 48x48 из системного списка, без стрелки ярлыка.
        public static Bitmap BigIcon(string path)
        {
            try
            {
                var i = new SHFILEINFO();
                SHGetFileInfo(path, 0, ref i, (uint)Marshal.SizeOf(i), 0x4000u);
                var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                IImageList il;
                if (SHGetImageList(2, ref iid, out il) != 0 || il == null) return ShellIcon(path);
                IntPtr h = IntPtr.Zero;
                il.GetIcon(i.iIcon, 1, ref h);
                return h == IntPtr.Zero ? ShellIcon(path) : FromHandle(h);
            }
            catch { try { return ShellIcon(path); } catch { return null; } }
        }

        public static string DisplayName(string path)
        {
            var i = new SHFILEINFO();
            SHGetFileInfo(path, 0, ref i, (uint)Marshal.SizeOf(i), 0x200u);
            return i.szDisplayName;
        }
    }
}
