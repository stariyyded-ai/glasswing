using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Glasswing
{
    // ---------------------------------------------------------------- папка-виджет

    // Одна папка-виджет: название, своя папка с перенесёнными значками, место и размер полки.
    class FolderInfo
    {
        public string Id, Title, Dir;
        public bool HasPos;
        public int X, Y, ShelfW, ShelfH;
    }

    // Список папок-виджетов: %APPDATA%\Glasswing\folders.json. Новые папки лежат в «Папки\<id>»,
    // перенесённые со старого виджета остаются там, где были (%USERPROFILE%\DesktopWidgets\<id>).
    static class FolderStore
    {
        public static readonly string FilePath = Path.Combine(Settings.Dir, "folders.json");
        public static readonly string Root = Path.Combine(Settings.Dir, "Папки");

        public static List<FolderInfo> Load()
        {
            var res = new List<FolderInfo>();
            try
            {
                if (!File.Exists(FilePath)) return res;
                var list = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8).TrimStart('﻿')) as List<object>;
                if (list == null) return res;
                foreach (var d in list.OfType<Dictionary<string, object>>())
                {
                    var f = new FolderInfo { Id = Str(d, "Id"), Title = Str(d, "Title"), Dir = Str(d, "Dir") };
                    if (string.IsNullOrEmpty(f.Id) || string.IsNullOrEmpty(f.Dir)) continue;
                    if (string.IsNullOrEmpty(f.Title)) f.Title = "Мои ярлыки";
                    f.HasPos = d.ContainsKey("X") && d["X"] is double && d.ContainsKey("Y") && d["Y"] is double;
                    f.X = Int(d, "X");
                    f.Y = Int(d, "Y");
                    f.ShelfW = Int(d, "ShelfW");
                    f.ShelfH = Int(d, "ShelfH");
                    res.Add(f);
                }
            }
            catch (Exception ex) { Log.Write("папки: " + ex.Message); }
            return res;
        }

        public static void Save(List<FolderInfo> list)
        {
            try
            {
                var arr = new List<object>();
                foreach (var f in list)
                {
                    var d = new Dictionary<string, object> { { "Id", f.Id }, { "Title", f.Title }, { "Dir", f.Dir } };
                    if (f.HasPos) { d["X"] = f.X; d["Y"] = f.Y; }
                    if (f.ShelfW > 0 && f.ShelfH > 0) { d["ShelfW"] = f.ShelfW; d["ShelfH"] = f.ShelfH; }
                    arr.Add(d);
                }
                Directory.CreateDirectory(Settings.Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json.Write(arr), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("папки, сохранение: " + ex.Message); }
        }

        public static FolderInfo New(string id, string title)
        {
            return new FolderInfo { Id = id, Title = title, Dir = Path.Combine(Root, id) };
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object o;
            return d.TryGetValue(k, out o) && o is string ? (string)o : null;
        }

        static int Int(Dictionary<string, object> d, string k)
        {
            object o;
            return d.TryGetValue(k, out o) && o is double ? (int)Math.Round((double)o) : 0;
        }
    }

    // Перенос файлов туда и обратно. Ничего не удаляется: только перемещается, а при совпадении имён
    // рядом появляется «имя (2)».
    static class FolderFiles
    {
        public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        // Что видно на полке: как в Проводнике, без скрытых и системных файлов; папки первыми.
        public static List<FileSystemInfo> Entries(string dir)
        {
            try
            {
                var di = new DirectoryInfo(dir);
                if (!di.Exists) return new List<FileSystemInfo>();
                return di.EnumerateFileSystemInfos()
                    .Where(e => (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .OrderBy(e => e is FileInfo ? 1 : 0).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch { return new List<FileSystemInfo>(); }
        }

        public static int Count(string dir) { return Entries(dir).Count; }

        public static string Norm(string p)
        {
            try { return Path.GetFullPath(p).TrimEnd('\\', '/'); } catch { return (p ?? "").TrimEnd('\\', '/'); }
        }

        public static bool Same(string a, string b) { return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase); }

        // inner лежит внутри outer или совпадает с ней
        public static bool Inside(string inner, string outer)
        {
            return (Norm(inner) + "\\").StartsWith(Norm(outer) + "\\", StringComparison.OrdinalIgnoreCase);
        }

        static string FreePath(string dir, string name, bool isDir)
        {
            string p = Path.Combine(dir, name);
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
            string stem = isDir ? name : Path.GetFileNameWithoutExtension(name);
            string ext = isDir ? "" : Path.GetExtension(name);
            for (int i = 2; ; i++)
            {
                p = Path.Combine(dir, stem + " (" + i + ")" + ext);
                if (!File.Exists(p) && !Directory.Exists(p)) return p;
            }
        }

        public static string Move(string src, string dstDir)
        {
            src = Norm(src);
            Directory.CreateDirectory(dstDir);
            bool isDir = Directory.Exists(src);
            string dst = FreePath(dstDir, Path.GetFileName(src), isDir);
            if (!isDir) { File.Move(src, dst); return dst; }
            try { Directory.Move(src, dst); }
            catch (IOException)
            {
                // другой диск: копируем целиком и только потом убираем исходник
                if (!string.Equals(Path.GetPathRoot(src), Path.GetPathRoot(dst), StringComparison.OrdinalIgnoreCase))
                {
                    CopyDir(src, dst);
                    Directory.Delete(src, true);
                }
                else throw;
            }
            return dst;
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            new DirectoryInfo(dst).Attributes = new DirectoryInfo(src).Attributes;
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }

    class FolderModule : Module
    {
        readonly List<FolderCard> cards = new List<FolderCard>();
        List<FolderInfo> data;
        bool hidden;
        public override string Id => "folder";
        public override string Name => "Папка-виджет";
        public override string Short => "Ярлыки и папки со стола";
        public override string Description => "Кнопка с сеткой раскрывает полку с папками и ярлыками, которые вы перетащили на неё с рабочего стола.";
        public override IconKind Icon => IconKind.Folder;
        protected override string EnabledKey => "Shown";
        public override bool Running => cards.Any(c => !c.IsDisposed);
        public override bool CanReset => true;

        public List<FolderInfo> Data
        {
            get { if (data == null) data = FolderStore.Load(); return data; }
        }

        public bool TopMost
        {
            get { return Settings.GetBool(Id, "TopMost", false); }
            set { Settings.Set(Id, "TopMost", value); foreach (var c in Live()) c.TopMost = value; }
        }

        public bool Locked
        {
            get { return Settings.GetBool(Id, "Locked", false); }
            set { Settings.Set(Id, "Locked", value); }
        }

        IEnumerable<FolderCard> Live() { return cards.Where(c => !c.IsDisposed).ToList(); }

        public void Save() { FolderStore.Save(Data); }

        public override void Start()
        {
            if (Running) return;
            if (Data.Count == 0) { Data.Add(FolderStore.New("main", "Мои ярлыки")); Save(); }
            foreach (var f in Data) Open(f);
        }

        void Open(FolderInfo f)
        {
            var card = new FolderCard(this, f);
            cards.Add(card);
            card.Place(Data.IndexOf(f));
            card.Show();
            if (hidden) card.SetHidden(true);
        }

        public override void Stop()
        {
            foreach (var c in Live()) { c.Close(); c.Dispose(); }
            cards.Clear();
        }

        public override void SetHidden(bool hide) { hidden = hide; foreach (var c in Live()) c.SetHidden(hide); }
        public override void GlassChanged() { foreach (var c in Live()) c.ApplyGlass(); }
        public override bool HasGlass => true;

        public override void ResetPosition()
        {
            foreach (var c in Live()) c.ResetHome(Data.IndexOf(c.Info));
            Save();
        }

        // Место по умолчанию: левее кнопки игрового хаба, следующие папки ниже.
        public static Point DefaultHome(Rectangle wa, int index, int btn)
        {
            int x = wa.Right - btn - Ui.S(32) - btn - Ui.S(16);
            int y = wa.Top + Ui.S(160) + Ui.S(72) * Math.Max(0, index);
            if (y + btn > wa.Bottom) y = wa.Top + Ui.S(40) + Ui.S(72) * (index % 4);
            return new Point(x, y);
        }

        public FolderCard CardOf(FolderInfo f) { return Live().FirstOrDefault(c => c.Info == f); }

        public int Total()
        {
            int n = 0;
            foreach (var f in Data)
            {
                var c = CardOf(f);
                n += c != null ? c.Count : FolderFiles.Count(f.Dir);
            }
            return n;
        }

        // Все папки виджетов: в них нельзя переносить их самих и то, что их содержит.
        public bool IsProtected(string path)
        {
            if (FolderFiles.Inside(Settings.Dir, path)) return true;
            return Data.Any(f => FolderFiles.Inside(f.Dir, path));
        }

        // Показываем часть на столе, если её убрали (или выключен весь оверлей).
        void ShowPart()
        {
            if (!Parent.Enabled)
            {
                foreach (var t in Parent.Tabs) t.Enabled = t == this;
                App.Core.SetEnabled(Parent, true);
            }
            else if (!Enabled) App.Core.SetEnabled(this, true);
        }

        public void AddWidget()
        {
            string id = "w" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            while (Data.Any(f => f.Id == id)) id += "x";
            int n = Data.Count + 1;
            var info = FolderStore.New(id, Data.Count == 0 ? "Мои ярлыки" : "Папка " + n);
            Data.Add(info);
            Save();
            if (Running) Open(info);
            else ShowPart();
            var card = CardOf(info);
            if (card != null) card.SetExpanded(true);
        }

        public void Rename(FolderInfo f, IWin32Window owner)
        {
            string t = FolderDialog.Ask(owner, "Название виджета:", f.Title);
            if (string.IsNullOrWhiteSpace(t)) return;
            f.Title = t.Trim();
            Save();
            var c = CardOf(f);
            if (c != null) c.TitleChanged();
        }

        public void OpenDir(FolderInfo f)
        {
            try
            {
                Directory.CreateDirectory(f.Dir);
                Process.Start(new ProcessStartInfo(f.Dir) { UseShellExecute = true });
            }
            catch (Exception ex) { Warn(ex.Message); }
        }

        public static void Warn(string text)
        {
            MessageBox.Show(text, "Папка-виджет", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Возвращает значки на рабочий стол; список ошибок показываем одним окном.
        public void ToDesktop(FolderInfo f, IEnumerable<string> paths)
        {
            var errors = new List<string>();
            foreach (var p in paths)
            {
                try { FolderFiles.Move(p, FolderFiles.Desktop); }
                catch (Exception ex) { errors.Add(p + "\n  " + ex.Message); }
            }
            var c = CardOf(f);
            if (c != null) c.UpdateItems();
            if (errors.Count > 0) Warn("Не удалось вернуть:\n\n" + string.Join("\n", errors));
        }

        public void Delete(FolderInfo f)
        {
            if (MessageBox.Show("Удалить виджет «" + f.Title + "»?\nВсё его содержимое вернётся на рабочий стол.", "Папка-виджет",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ToDesktop(f, FolderFiles.Entries(f.Dir).Select(e => e.FullName).ToList());
            if (FolderFiles.Count(f.Dir) > 0) return;
            // пустую папку убираем; если в ней остались скрытые файлы, папка остаётся на диске
            try { if (Directory.Exists(f.Dir) && !Directory.EnumerateFileSystemEntries(f.Dir).Any()) Directory.Delete(f.Dir, false); } catch { }
            var c = CardOf(f);
            Data.Remove(f);
            Save();
            if (c != null) { cards.Remove(c); c.Close(); c.Dispose(); }
            if (Data.Count == 0)
            {
                App.Core.SetEnabled(this, false);
                if (Parent.Tabs.All(t => !t.Enabled)) App.Core.SetEnabled(Parent, false);
            }
        }

        public void ReturnAll()
        {
            int n = Total();
            if (n == 0) return;
            if (MessageBox.Show("Вернуть на рабочий стол все значки из папок-виджетов (" + Icons(n) + ")?", "Папка-виджет",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var f in Data.ToList()) ToDesktop(f, FolderFiles.Entries(f.Dir).Select(e => e.FullName).ToList());
        }

        static string Icons(int n)
        {
            int a = n % 10, b = n % 100;
            string w = a == 1 && b != 11 ? "значок" : a >= 2 && a <= 4 && (b < 12 || b > 14) ? "значка" : "значков";
            return n + " " + w;
        }

        // Меню из центра управления: что сделать с каждой папкой.
        void FoldersMenu()
        {
            var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 9.5f) };
            menu.Closed += (s, e) => menu.BeginInvoke(new Action(menu.Dispose));
            foreach (var info in Data)
            {
                var f = info;
                var item = new ToolStripMenuItem(f.Title);
                item.DropDownItems.Add(new ToolStripMenuItem("Открыть папку виджета", null, (s, e) => OpenDir(f)));
                item.DropDownItems.Add(new ToolStripMenuItem("Переименовать…", null, (s, e) => Rename(f, null)));
                item.DropDownItems.Add(new ToolStripMenuItem("Удалить виджет…", null, (s, e) => Delete(f)));
                menu.Items.Add(item);
            }
            if (Data.Count > 0) menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Новый виджет", null, (s, e) => AddWidget()));
            menu.Show(Cursor.Position);
        }

        public override IEnumerable<Row> Rows()
        {
            yield return ShownRow();
            yield return new Row
            {
                Label = "Папки на столе",
                Value = () => Data.Count == 0 ? "нет" : string.Join(", ", Data.Select(f => "«" + f.Title + "»")),
                Click = FoldersMenu,
            };
            yield return new Row { Label = "Добавить ещё одну папку", Value = () => "+ Новая", Click = AddWidget };
            yield return new Row
            {
                Label = "Вернуть всё на рабочий стол",
                Value = () => { int n = Total(); return n == 0 ? "папки пусты" : Icons(n); },
                Click = ReturnAll,
            };
            yield return new Row { Label = "Поверх всех окон", On = () => TopMost, Click = () => TopMost = !TopMost };
            yield return new Row { Label = "Закрепить на месте", On = () => Locked, Click = () => Locked = !Locked };
        }
    }

    class FolderItem
    {
        public string Path, Name;
        public Bitmap Bmp;
    }

    class FolderCard : GlassCard
    {
        static readonly Color ColText = Color.White;
        static readonly Color ColMuted = Color.FromArgb(190, 255, 255, 255);
        static readonly Color ColTileHot = Color.FromArgb(46, 255, 255, 255);
        static readonly Color ColHover = Color.FromArgb(8, 255, 255, 255);
        static readonly StringFormat CenterWrap = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        public readonly FolderInfo Info;
        readonly FolderModule folder;
        readonly ToolTip tip = new ToolTip();
        readonly GlassHint hint;
        readonly GlassInput search;
        readonly Timer watch = new Timer { Interval = 1000 };
        // плитка: значок сверху, под ним название в две строки
        readonly int btnSz = Ui.S(56), iconPx = Ui.S(40), tileW = Ui.S(76), tileH = Ui.S(86), gap = Ui.S(6), pad = Ui.S(12), stripH = Ui.S(34);
        readonly int gridTop;
        readonly List<FolderItem> items = new List<FolderItem>();
        List<FolderItem> shown = new List<FolderItem>();   // то, что подходит под поиск
        string sig = "";
        bool expanded, up, hoverBtn, dragOut, hotClear;
        int hot = -1, scroll, downIdx = -1, menuIdx = -1;
        Point downPt;
        Size shelf;
        Point home;

        public FolderCard(FolderModule m, FolderInfo info) : base(m)
        {
            folder = m;
            Info = info;
            Text = info.Title;
            hint = new GlassHint(this);
            search = new GlassInput(this, 60);
            search.Box.TextChanged += (s, e) => Refilter(true);
            search.Submit += () =>
            {
                if (shown.Count == 0) return;
                var it = shown[0];
                SetExpanded(false);
                Open(it);
            };
            // Esc: сначала стираем поиск, потом сворачиваем полку
            search.Cancel += () => { if (search.Text.Length > 0) search.Box.Text = ""; else SetExpanded(false); };
            gridTop = stripH + Ui.S(4);
            AllowDrop = true;
            tip.SetToolTip(this, info.Title);
            watch.Tick += (s, e) =>
            {
                if (Sig() != sig) UpdateItems();
                bool top = folder.TopMost;   // «поверх окон» из меню одной папки касается всех
                if (TopMost != top) TopMost = top;
            };
            UpdateItems();
        }

        public int Count => items.Count;

        static int S(double v) { return Ui.S(v); }
        static float F(double v) { return (float)(v * Ui.Scale); }

        int ExpW => Info.ShelfW > 0 ? Info.ShelfW : S(380);
        int ExpH => Info.ShelfH > 0 ? Info.ShelfH : S(232);

        protected override Size CardSize() { return expanded ? shelf : new Size(btnSz, btnSz); }

        protected override Point DefaultLocation(Rectangle wa) { return FolderModule.DefaultHome(wa, folder.Data.IndexOf(Info), btnSz); }

        public void Place(int index)
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            var pos = FolderModule.DefaultHome(wa, index, btnSz);
            if (Info.HasPos)
            {
                var saved = new Point(Info.X, Info.Y);
                if (Screen.AllScreens.Any(s => s.WorkingArea.Contains(saved))) pos = saved;
            }
            home = pos;
            Bounds = new Rectangle(pos, new Size(btnSz, btnSz));
        }

        public void ResetHome(int index)
        {
            SetExpanded(false);
            home = FolderModule.DefaultHome(Screen.PrimaryScreen.WorkingArea, index, btnSz);
            Bounds = new Rectangle(home, new Size(btnSz, btnSz));
            Info.HasPos = true;
            Info.X = home.X;
            Info.Y = home.Y;
        }

        public void SetHidden(bool hide)
        {
            if (hide) SetExpanded(false);
            Visible = !hide;
        }

        public void TitleChanged()
        {
            Text = Info.Title;
            if (!expanded) tip.SetToolTip(this, Info.Title);
            Invalidate();
        }

        // ---------------------------------------------------------------- содержимое

        string Sig() { return string.Join("|", FolderFiles.Entries(Info.Dir).Select(e => e.Name)); }

        public void UpdateItems()
        {
            try { Directory.CreateDirectory(Info.Dir); } catch (Exception ex) { Log.Write("папка " + Info.Dir + ": " + ex.Message); }
            var entries = FolderFiles.Entries(Info.Dir);
            foreach (var it in items) if (it.Bmp != null) it.Bmp.Dispose();
            items.Clear();
            foreach (var e in entries)
            {
                string name = null;
                try { name = HubShell.DisplayName(e.FullName); } catch { }
                if (string.IsNullOrEmpty(name)) name = e.Name;
                items.Add(new FolderItem { Path = e.FullName, Name = name, Bmp = HubShell.BigIcon(e.FullName) });
            }
            sig = string.Join("|", entries.Select(e => e.Name));
            hot = -1;
            Refilter(false);
        }

        void Refilter(bool toTop)
        {
            shown = Shelf.Filter(items, x => x.Name, search == null ? "" : search.Text);
            hot = -1;
            downIdx = menuIdx = -1;
            if (toTop) scroll = 0;
            scroll = Math.Min(scroll, MaxScroll());
            if (hint != null) hint.Set(null, null);
            Invalidate();
        }

        // Клавиатуру полка берёт, только пока раскрыта.
        void FocusSearch()
        {
            if (!expanded) return;
            if (!search.Box.Focused) search.Begin(search.Text, false);
            Invalidate();
        }

        void Open(FolderItem it)
        {
            try { Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true }); }
            catch (Exception ex) { FolderModule.Warn(ex.Message); }
        }

        // Перенос внутрь; пропускаем диски, то, что уже здесь, и папки самих виджетов.
        void MoveIn(string[] paths)
        {
            var errors = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    if (string.IsNullOrEmpty(p) || Path.GetPathRoot(p).TrimEnd('\\') == p.TrimEnd('\\')) continue;
                    if (!File.Exists(p) && !Directory.Exists(p)) continue;
                    if (FolderFiles.Same(Path.GetDirectoryName(FolderFiles.Norm(p)), Info.Dir)) continue;
                    if (folder.IsProtected(p)) continue;
                    FolderFiles.Move(p, Info.Dir);
                }
                catch (Exception ex) { errors.Add(p + "\n  " + ex.Message); }
            }
            UpdateItems();
            // окно с ошибкой показываем уже после того, как Проводник закончит перетаскивание
            if (errors.Count > 0) BeginInvoke(new Action(() => FolderModule.Warn("Не удалось перенести:\n\n" + string.Join("\n", errors))));
        }

        bool Accepts(IDataObject data)
        {
            if (dragOut || data == null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
            var paths = data.GetData(DataFormats.FileDrop) as string[];
            return paths != null && paths.Any(p => !FolderFiles.Same(Path.GetDirectoryName(FolderFiles.Norm(p)) ?? "", Info.Dir));
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = Accepts(e.Data) ? DragDropEffects.Move : DragDropEffects.None;
            if (e.Effect != DragDropEffects.None) SetExpanded(true);
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            e.Effect = Accepts(e.Data) ? DragDropEffects.Move : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (Accepts(e.Data)) MoveIn(e.Data.GetData(DataFormats.FileDrop) as string[] ?? new string[0]);
            // перенос уже сделан: Проводнику отвечаем «ничего не делать», чтобы он не трогал исходники
            e.Effect = DragDropEffects.None;
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
            int n = shown.Count;
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
            for (int i = 0; i < shown.Count; i++) if (TileRect(i).Contains(p)) return i;
            return -1;
        }

        public void SetExpanded(bool on)
        {
            if (expanded == on) return;
            if (on)
            {
                home = Location;
                expanded = true;
                var wa = Screen.FromPoint(home).WorkingArea;
                int fullW = Math.Min(ExpW, wa.Width), fullH = Math.Min(ExpH, wa.Height);
                int x = Math.Max(wa.Left, Math.Min(home.X, wa.Right - fullW));
                up = home.Y + fullH > wa.Bottom;
                int y = up ? home.Y + btnSz - fullH : home.Y;
                MinimumSize = new Size(S(140), gridTop + tileH + pad);
                shelf = new Size(fullW, fullH);
                Bounds = new Rectangle(x, y, fullW, fullH);
                tip.SetToolTip(this, null);
            }
            else
            {
                expanded = false;
                MinimumSize = Size.Empty;
                Bounds = new Rectangle(home, new Size(btnSz, btnSz));
                hot = -1;
                scroll = 0;
                hotClear = false;
                hint.Hide();
                search.End();   // поиск сбрасывается, клавиатура отпускается
                tip.SetToolTip(this, Info.Title);
            }
            Invalidate();
        }

        void SaveHome()
        {
            home = new Point(Left, expanded && up ? Bottom - btnSz : Top);
            Info.HasPos = true;
            Info.X = home.X;
            Info.Y = home.Y;
            folder.Save();
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
            // значок вытащили за полку: отдаём его Проводнику как обычный файл
            if (downIdx >= 0 && e.Button == MouseButtons.Left)
            {
                var ds = SystemInformation.DragSize;
                if (Math.Abs(e.X - downPt.X) > ds.Width || Math.Abs(e.Y - downPt.Y) > ds.Height)
                {
                    string path = downIdx < shown.Count ? shown[downIdx].Path : null;
                    downIdx = -1;
                    if (path == null) return;
                    hint.Hide();
                    dragOut = true;
                    try { DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link); }
                    catch (Exception ex) { Log.Write("папка, перетаскивание: " + ex.Message); }
                    finally { dragOut = false; }
                    UpdateItems();
                    return;
                }
            }
            int edge = Locked ? 0 : Shelf.EdgeHit(e.Location, ClientSize, up);
            int h = edge != 0 ? -1 : HitIndex(e.Location);
            var sr = Shelf.SearchRect(ClientSize.Width, pad, stripH);
            bool inSearch = edge == 0 && sr.Contains(e.Location);
            bool hc = inSearch && search.Text.Length > 0 && Shelf.ClearRect(sr).Contains(e.Location);
            Cursor = edge != 0 ? Shelf.EdgeCursor(edge) : hc ? Cursors.Hand : inSearch ? Cursors.IBeam : h >= 0 || e.Y < stripH ? Cursors.Hand : Cursors.Default;
            if (h != hot || hc != hotClear) { hot = h; hotClear = hc; Invalidate(); }
            // полное название значка рядом с мышью
            hint.Set(h >= 0 ? "i:" + h + ":" + shown[h].Path : null, h >= 0 ? shown[h].Name : null);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hint.Set(null, null);
            if (hot != -1 || hoverBtn || hotClear) { hot = -1; hoverBtn = false; hotClear = false; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Clicks > 1) return;   // двойной щелчок не сворачивает только что раскрытую полку
            base.OnMouseDown(e);
        }

        protected override bool HandleMouseDown(Point p)
        {
            hint.Hide();
            if (expanded)
            {
                int edge = Locked ? 0 : Shelf.EdgeHit(p, ClientSize, up);
                if (edge != 0)
                {
                    WNative.ReleaseCapture();
                    WNative.SendMessage(Handle, 0xA1, (IntPtr)edge, IntPtr.Zero);   // растягивание
                    Info.ShelfW = Width;
                    Info.ShelfH = Height;
                    if (up) SaveHome(); else folder.Save();
                    return true;
                }
                // поле поиска: щелчок ставит туда курсор, «×» стирает запрос
                var sr = Shelf.SearchRect(ClientSize.Width, pad, stripH);
                if (sr.Contains(p))
                {
                    if (search.Text.Length > 0 && Shelf.ClearRect(sr).Contains(p)) search.Box.Text = "";
                    FocusSearch();
                    return true;
                }
                int i = HitIndex(p);
                if (i >= 0) { downIdx = i; downPt = p; return true; }
            }
            bool inStrip = p.Y < stripH;
            if (!Locked)
            {
                var before = Location;
                WNative.DragWindow(this);
                if (Location != before) { SaveHome(); return true; }
            }
            if (!expanded) { SetExpanded(true); FocusSearch(); }
            else if (inStrip) SetExpanded(false);
            return true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && downIdx >= 0)
            {
                int was = downIdx;
                downIdx = -1;
                if (was < shown.Count && HitIndex(e.Location) == was) Open(shown[was]);
            }
            if (e.Button == MouseButtons.Right) menuIdx = HitIndex(e.Location);
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!expanded) return;
            scroll = Math.Max(0, Math.Min(MaxScroll(), scroll - (int)Math.Round(e.Delta / 120.0 * (tileH + gap))));
            hot = HitIndex(e.Location);
            hint.Set(null, null);
            Invalidate();
        }

        // Пункты значка и папки идут первыми, общие пункты Glasswing ниже.
        protected override void AddMenuItems(ContextMenuStrip menu)
        {
            int at = 0;
            var it = menuIdx >= 0 && menuIdx < shown.Count ? shown[menuIdx] : null;
            if (it != null)
            {
                menu.Items.Insert(at++, new ToolStripMenuItem("Открыть", null, (s, e) => Open(it)) { Font = new Font(menu.Font, FontStyle.Bold) });
                menu.Items.Insert(at++, new ToolStripMenuItem("Вернуть на рабочий стол", null, (s, e) => folder.ToDesktop(Info, new[] { it.Path })));
                menu.Items.Insert(at++, new ToolStripSeparator());
            }
            menu.Items.Insert(at++, new ToolStripMenuItem("Открыть папку виджета", null, (s, e) => folder.OpenDir(Info)));
            menu.Items.Insert(at++, new ToolStripMenuItem("Переименовать виджет…", null, (s, e) => folder.Rename(Info, this)));
            menu.Items.Insert(at++, new ToolStripMenuItem("Новый виджет", null, (s, e) => folder.AddWidget()));
            menu.Items.Insert(at++, new ToolStripMenuItem("Удалить этот виджет…", null, (s, e) => folder.Delete(Info)));
            menu.Items.Insert(at++, new ToolStripSeparator());
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
            // значок «сетка» из четырёх скруглённых квадратов
            float size = F(22), cell = (size - F(4)) / 2, x0 = (cw - size) / 2, y0 = (ch - size) / 2;
            using (var pen = new Pen(ColText, Math.Max(1.5f, F(1.7))))
                for (int r = 0; r < 2; r++)
                    for (int c = 0; c < 2; c++)
                        using (var path = Ui.Round(new RectangleF(x0 + c * (cell + F(4)), y0 + r * (cell + F(4)), cell, cell), F(2.5)))
                            g.DrawPath(pen, path);
            Shelf.DrawBadge(g, cw, items.Count);
        }

        void DrawShelf(Graphics g, int cw, int ch)
        {
            // строка сверху: название наведённого значка, иначе название виджета
            bool isHot = hot >= 0 && hot < shown.Count;
            string text = isHot ? shown[hot].Name : search.Text.Length > 0 ? "Найдено: " + shown.Count : Info.Title + "  ·  " + items.Count;
            var sr = Shelf.SearchRect(cw, pad, stripH);
            Shelf.DrawStrip(g, text, Ui.Font(13, true), isHot ? ColText : ColMuted, cw, pad, stripH, sr.Width + S(8));
            Shelf.DrawSearch(g, sr, search, hotClear);

            var grid = new Rectangle(0, gridTop, cw, ch - gridTop);
            g.SetClip(grid);
            if (items.Count == 0)
            {
                var box = new RectangleF(pad, gridTop + S(2), cw - 2 * pad, ch - gridTop - pad - S(2));
                using (var dash = new Pen(ColMuted, Math.Max(1f, Ui.Scale)) { DashStyle = DashStyle.Dash })
                using (var path = Ui.Round(box, S(12))) g.DrawPath(dash, path);
                Ui.Text2(g, "Перетащите сюда ярлыки", Ui.Font(12.5), ColMuted, box, CenterWrap);
            }
            else if (shown.Count == 0)
            {
                var box = new RectangleF(pad, gridTop + S(2), cw - 2 * pad, ch - gridTop - pad - S(2));
                Ui.Text2(g, "Ничего не найдено", Ui.Font(12.5), ColMuted, box, CenterWrap);
            }
            for (int i = 0; i < shown.Count; i++)
            {
                var rc = TileRect(i);
                if (rc.Bottom < gridTop || rc.Top > ch) continue;
                bool on = i == hot;
                if (on) Ui.FillRound(g, rc, S(12), ColTileHot);
                var bmp = shown[i].Bmp;
                if (bmp != null)
                {
                    float sz = on ? iconPx + S(4) : iconPx;
                    float cy = rc.Y + S(8) + iconPx / 2f;
                    g.DrawImage(bmp, rc.X + (rc.Width - sz) / 2, cy - sz / 2, sz, sz);
                }
                // название под значком: до двух строк, слова не рвём
                var tr = new RectangleF(rc.X + S(3), rc.Y + S(52), rc.Width - S(6), rc.Height - S(53));
                Shelf.DrawCaption(g, shown[i].Name, Ui.Font(11), ColText, tr, 2, false, true);
            }
            Shelf.DrawScroll(g, cw, ch, gridTop, pad, scroll, MaxScroll());
            g.ResetClip();
            Shelf.DrawGrip(g, cw, ch, up, ColMuted);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            watch.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                watch.Dispose();
                tip.Dispose();
                hint.Dispose();
                search.Dispose();
                foreach (var it in items) if (it.Bmp != null) it.Bmp.Dispose();
                items.Clear();
            }
            base.Dispose(disposing);
        }
    }

    // Общее у полок хаба и папок: строка сверху, полоса прокрутки, края для растягивания, счётчик.
    static class Shelf
    {
        static float F(double v) { return (float)(v * Ui.Scale); }

        static readonly StringFormat CaptionLine = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        // Текст строки и стрелка «свернуть» справа; reserve — место под поле поиска перед стрелкой.
        public static void DrawStrip(Graphics g, string text, Font font, Color col, int cw, int pad, int stripH, int reserve = 0)
        {
            float tw = cw - 2 * pad - Ui.S(22) - reserve;
            if (tw > Ui.S(12)) Ui.Text2(g, text, font, col, new RectangleF(pad, 0, tw, stripH), Ui.Left, true);
            using (var pen = new Pen(Color.FromArgb(190, 255, 255, 255), Math.Max(1.5f, F(1.8))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float ax = cw - pad - F(8), ay = stripH / 2f, a = F(5);
                g.DrawLines(pen, new[] { new PointF(ax - a, ay + a / 2), new PointF(ax, ay - a / 2), new PointF(ax + a, ay + a / 2) });
            }
        }

        public static void DrawScroll(Graphics g, int cw, int ch, int top, int pad, int scroll, int max)
        {
            if (max <= 0) return;
            float viewH = ch - top - pad;
            float barH = Math.Max(Ui.S(24), viewH * viewH / (viewH + max));
            float barY = top + (viewH - barH) * scroll / max;
            Ui.FillRound(g, new RectangleF(cw - Ui.S(5), barY, Ui.S(3), barH), F(1.5), Ui.Edge);
        }

        // Точки в углу: за него полку можно растянуть.
        public static void DrawGrip(Graphics g, int cw, int ch, bool up, Color col)
        {
            float d = Math.Max(2, Ui.S(2));
            using (var mb = new SolidBrush(col))
                for (int k = 0; k < 3; k++)
                {
                    float dx = cw - Ui.S(4) - Ui.S(4) * k - d;
                    float dy = up ? Ui.S(4) + Ui.S(4) * (2 - k) : ch - Ui.S(4) - Ui.S(4) * (2 - k) - d;
                    g.FillEllipse(mb, dx, dy, d, d);
                }
        }

        // Число в углу свёрнутой кнопки.
        public static void DrawBadge(Graphics g, int cw, int n)
        {
            if (n <= 0) return;
            string txt = n > 99 ? "99+" : n.ToString(CultureInfo.InvariantCulture);
            var bf = Ui.Font(10.5, true);
            float tw = Math.Max(F(17), g.MeasureString(txt, bf).Width + F(4)), bh = F(17);
            var badge = new RectangleF(cw - tw - F(4), F(4), tw, bh);
            Ui.FillRound(g, badge, bh / 2, Ui.Accent);
            Ui.Text2(g, txt, bf, Ui.AccentInk, badge, Ui.Center);
        }

        // Подпись плитки по словам в несколько строк, по центру; true, если название не влезло целиком.
        public static bool DrawCaption(Graphics g, string s, Font f, Color c, RectangleF box, int maxLines, bool bottom, bool shadow)
        {
            var fit = TextFit.Wrap(s, f, box.Width, maxLines);
            float lh = TextFit.LineHeight(f);
            float y = bottom ? box.Bottom - fit.Lines.Count * lh : box.Y;
            for (int k = 0; k < fit.Lines.Count; k++)
                Ui.Text2(g, fit.Lines[k], f, c, new RectangleF(box.X, y + k * lh, box.Width, lh), CaptionLine, shadow);
            return fit.Cut;
        }

        // Поиск по названию: без учёта регистра, «ё» как «е», каждое слово запроса должно найтись.
        // Названия, которые с запроса начинаются, идут первыми.
        public static List<T> Filter<T>(List<T> all, Func<T, string> name, string query)
        {
            string q = Norm(query);
            if (q.Length == 0) return new List<T>(all);
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return all.Select(x => new { x, n = Norm(name(x)) })
                      .Where(a => words.All(w => a.n.Contains(w)))
                      .OrderBy(a => a.n.StartsWith(q, StringComparison.Ordinal) ? 0 : 1)
                      .Select(a => a.x).ToList();
        }

        static string Norm(string s) { return (s ?? "").Trim().ToLowerInvariant().Replace('ё', 'е'); }

        // Поле поиска в строке сверху, перед стрелкой «свернуть».
        public static Rectangle SearchRect(int cw, int pad, int stripH)
        {
            int avail = cw - 2 * pad - Ui.S(22);
            int w = Math.Max(Ui.S(64), Math.Min(Ui.S(170), avail * 9 / 20));
            int h = Ui.S(24);
            return new Rectangle(cw - pad - Ui.S(22) - w, (stripH - h) / 2, w, h);
        }

        // Кнопка «×» в правом конце поля.
        public static Rectangle ClearRect(Rectangle sr) { return new Rectangle(sr.Right - sr.Height, sr.Y, sr.Height, sr.Height); }

        public static void DrawSearch(Graphics g, Rectangle sr, GlassInput input, bool hotClear)
        {
            bool focused = input.Box.Focused, has = input.Text.Length > 0;
            Ui.FillRound(g, sr, sr.Height / 2f, Color.FromArgb(focused ? 30 : 18, 255, 255, 255));
            if (focused) Ui.StrokeRound(g, sr, sr.Height / 2f, Color.FromArgb(150, Ui.Accent));
            using (var pen = new Pen(Color.FromArgb(170, 255, 255, 255), Math.Max(1.2f, F(1.4))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                // лупа
                float cx = sr.X + F(12), cy = sr.Y + sr.Height / 2f - F(1), r = F(4);
                g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
                g.DrawLine(pen, cx + r * 0.7f, cy + r * 0.7f, cx + r * 1.7f, cy + r * 1.7f);
            }
            var tr = new RectangleF(sr.X + F(22), sr.Y, sr.Width - F(22) - (has ? sr.Height : F(8)), sr.Height);
            var f = Ui.Font(12);
            if (!has) Ui.Text2(g, "Поиск…", f, Ui.Faint, tr, Ui.Left);
            if (tr.Width > 0) input.Draw(g, tr, f, Ui.Text, Color.FromArgb(90, Ui.Accent));
            if (has)
            {
                var cr = ClearRect(sr);
                float mx = cr.X + cr.Width / 2f, my = cr.Y + cr.Height / 2f, a = F(3.5);
                if (hotClear) Ui.FillRound(g, new RectangleF(mx - F(8), my - F(8), F(16), F(16)), F(8), Color.FromArgb(30, 255, 255, 255));
                using (var pen = new Pen(Color.FromArgb(hotClear ? 240 : 160, 255, 255, 255), Math.Max(1.2f, F(1.5))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, mx - a, my - a, mx + a, my + a);
                    g.DrawLine(pen, mx - a, my + a, mx + a, my - a);
                }
            }
        }

        // Край полки, за который её можно растянуть: код для WM_NCLBUTTONDOWN или 0.
        public static int EdgeHit(Point p, Size client, bool up)
        {
            int m = Ui.S(7);
            bool right = p.X >= client.Width - m;
            bool vert = up ? p.Y <= m : p.Y >= client.Height - m;
            if (right && vert) return up ? 14 : 17;
            if (right) return 11;
            if (vert) return up ? 12 : 15;
            return 0;
        }

        public static Cursor EdgeCursor(int edge)
        {
            switch (edge)
            {
                case 11: return Cursors.SizeWE;
                case 12: case 15: return Cursors.SizeNS;
                case 14: return Cursors.SizeNESW;
                case 17: return Cursors.SizeNWSE;
                default: return Cursors.Default;
            }
        }
    }

    // Маленькое окно для ввода названия.
    static class FolderDialog
    {
        public static string Ask(IWin32Window owner, string prompt, string value)
        {
            using (var f = new Form())
            {
                f.Text = "Папка-виджет";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.TopMost = true;
                f.StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
                f.AutoScaleMode = AutoScaleMode.None;
                f.Font = new Font("Segoe UI", 9.5f);
                f.ClientSize = new Size(Ui.S(360), Ui.S(124));
                var label = new Label { Text = prompt, AutoSize = true, Location = new Point(Ui.S(14), Ui.S(14)) };
                var box = new TextBox { Text = value ?? "", Location = new Point(Ui.S(14), Ui.S(40)), Width = Ui.S(332) };
                var ok = new Button { Text = "ОК", DialogResult = DialogResult.OK, Size = new Size(Ui.S(96), Ui.S(30)), Location = new Point(Ui.S(146), Ui.S(80)) };
                var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Size = new Size(Ui.S(96), Ui.S(30)), Location = new Point(Ui.S(250), Ui.S(80)) };
                f.Controls.AddRange(new Control[] { label, box, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Shown += (s, e) => { box.Focus(); box.SelectAll(); };
                var r = owner != null ? f.ShowDialog(owner) : f.ShowDialog();
                return r == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
