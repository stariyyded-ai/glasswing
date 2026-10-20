using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Glasswing
{
    // ---------------------------------------------------------------- список дел

    class TodoModule : Module
    {
        const string Key = "todo";
        TodoCard card;
        public override string Id => Key;
        public override string Name => "Список дел";
        public override string Short => "Дела на сегодня";
        public override string Description => "Короткий список дел на рабочем столе. Пишете дело, отмечаете кружок, и оно зачёркивается.";
        public override IconKind Icon => IconKind.Todo;
        public override bool Running => card != null && !card.IsDisposed;
        public override bool CanReset => true;

        public static readonly string[] DoneStyles = { "убирать на следующий день", "оставлять", "убирать сразу" };
        public static readonly string[] NewStyles = { "вверху списка", "внизу списка" };

        public static int DoneStyle
        {
            get { return Math.Max(0, Math.Min(2, Settings.GetInt(Key, "DoneStyle", 0))); }
            set { Settings.Set(Key, "DoneStyle", value); TodoList.Purge(); }
        }

        public static int NewStyle
        {
            get { return Math.Max(0, Math.Min(1, Settings.GetInt(Key, "NewStyle", 0))); }
            set { Settings.Set(Key, "NewStyle", value); }
        }

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

        // Для питомца и других виджетов: что ещё не сделано, по порядку списка.
        public static List<string> OpenItems()
        {
            return TodoList.Items.Where(i => !i.Done).Select(i => i.Text).ToList();
        }

        public static void AddItem(string text) { TodoList.Add(text); }

        // Список поменялся (вызывается в потоке окон).
        public static event Action Changed
        {
            add { TodoList.Changed += value; }
            remove { TodoList.Changed -= value; }
        }

        public static string Count(int n)
        {
            int a = n % 100, b = n % 10;
            string w = a >= 11 && a <= 14 ? "дел" : b == 1 ? "дело" : b >= 2 && b <= 4 ? "дела" : "дел";
            return n + " " + w;
        }

        public override void Start()
        {
            if (Running) return;
            TodoList.Purge();
            card = new TodoCard(this);
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
        public override bool HasGlass => true;
        public override void ResetPosition() { if (Running) card.ResetPosition(); }

        public override IEnumerable<Row> Rows()
        {
            yield return new Row { Label = "Выполненные дела", Value = () => DoneStyles[DoneStyle], Click = () => DoneStyle = (DoneStyle + 1) % DoneStyles.Length };
            yield return new Row { Label = "Новые дела", Value = () => NewStyles[NewStyle], Click = () => NewStyle = (NewStyle + 1) % NewStyles.Length };
            yield return new Row
            {
                Label = "Очистить выполненные",
                Value = () => { int n = TodoList.DoneCount; return n == 0 ? "нет" : Count(n); },
                Click = TodoList.ClearDone
            };
            yield return new Row { Label = "Поверх всех окон", On = () => TopMost, Click = () => TopMost = !TopMost };
            yield return new Row { Label = "Закрепить на месте", On = () => Locked, Click = () => Locked = !Locked };
        }
    }

    class TodoItem
    {
        public string Text = "";
        public bool Done;
        public DateTime Created = DateTime.Now, DoneAt = DateTime.MinValue;
    }

    // Дела лежат отдельно от настроек: %APPDATA%\Glasswing\todo.json. Сохраняем после каждого изменения.
    static class TodoList
    {
        static readonly string FilePath = Path.Combine(Settings.Dir, "todo.json");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string Stamp = "yyyy-MM-ddTHH:mm:ss";
        static List<TodoItem> items;
        public static event Action Changed;

        public static List<TodoItem> Items
        {
            get { if (items == null) Load(); return items; }
        }

        public static int DoneCount => Items.Count(i => i.Done);

        static void Load()
        {
            items = new List<TodoItem>();
            try
            {
                if (!File.Exists(FilePath)) return;
                var root = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
                object o;
                var list = root != null && root.TryGetValue("items", out o) ? o as List<object> : null;
                if (list == null) throw new FormatException("нет списка items");
                foreach (var x in list)
                {
                    var d = x as Dictionary<string, object>;
                    if (d == null) continue;
                    var it = new TodoItem
                    {
                        Text = d.TryGetValue("text", out o) && o is string ? ((string)o).Trim() : "",
                        Done = d.TryGetValue("done", out o) && o is bool && (bool)o,
                        Created = Date(d, "created", DateTime.Now),
                        DoneAt = Date(d, "doneAt", DateTime.MinValue)
                    };
                    if (it.Done && it.DoneAt == DateTime.MinValue) it.DoneAt = it.Created;
                    if (it.Text.Length > 0) items.Add(it);
                }
            }
            catch (Exception ex)
            {
                // испорченный файл не затираем: откладываем копию рядом
                Log.Write("дела: " + ex.Message);
                try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
            }
        }

        static DateTime Date(Dictionary<string, object> d, string key, DateTime def)
        {
            object o;
            DateTime t;
            if (d.TryGetValue(key, out o) && o is string && DateTime.TryParseExact((string)o, Stamp, Inv, DateTimeStyles.None, out t)) return t;
            return def;
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                var list = new List<object>();
                foreach (var it in items)
                {
                    var d = new Dictionary<string, object>();
                    d["text"] = it.Text;
                    d["done"] = it.Done;
                    d["created"] = it.Created.ToString(Stamp, Inv);
                    d["doneAt"] = it.Done ? it.DoneAt.ToString(Stamp, Inv) : null;
                    list.Add(d);
                }
                var root = new Dictionary<string, object> { { "items", list } };
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json.Write(root), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("дела, сохранение: " + ex.Message); }
        }

        static void Commit()
        {
            Save();
            var h = Changed;
            if (h != null) h();
        }

        static string Clean(string s)
        {
            return (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        }

        public static void Add(string text)
        {
            text = Clean(text);
            if (text.Length == 0) return;
            var it = new TodoItem { Text = text };
            if (TodoModule.NewStyle == 1) Items.Add(it);
            else Items.Insert(0, it);
            Commit();
        }

        public static void Toggle(TodoItem it)
        {
            if (!Items.Contains(it)) return;
            it.Done = !it.Done;
            it.DoneAt = it.Done ? DateTime.Now : DateTime.MinValue;
            Commit();
        }

        public static void Edit(TodoItem it, string text)
        {
            text = Clean(text);
            if (!Items.Contains(it)) return;
            if (text.Length == 0) { Remove(it); return; }
            if (text == it.Text) return;
            it.Text = text;
            Commit();
        }

        public static void Remove(TodoItem it)
        {
            if (Items.Remove(it)) Commit();
        }

        public static void MoveTop(TodoItem it)
        {
            if (Items.IndexOf(it) <= 0) return;
            Items.Remove(it);
            Items.Insert(0, it);
            Commit();
        }

        public static void ClearDone()
        {
            if (Items.RemoveAll(i => i.Done) > 0) Commit();
        }

        // Выполненные уходят из списка по правилу из настроек; «сразу» = через пару секунд, чтобы было видно галочку.
        public static void Purge()
        {
            int style = TodoModule.DoneStyle;
            if (style == 1) return;
            var now = DateTime.Now;
            int n = Items.RemoveAll(i => i.Done && (style == 0 ? i.DoneAt.Date < now.Date : (now - i.DoneAt).TotalSeconds >= 1.5));
            if (n > 0) Commit();
        }
    }

    class TodoCard : GlassCard
    {
        const int MaxLines = 3;   // длинное дело переносится не больше чем на три строки
        readonly TodoModule todo;
        readonly GlassInput input;
        readonly GlassHint hint;
        readonly Hits hits = new Hits();
        readonly Timer tick = new Timer { Interval = 1000 };
        readonly Font strike;
        readonly int cardW = Ui.S(330), pad = Ui.S(18), top = Ui.S(14), headH = Ui.S(22), pitch = Ui.S(34), rowH = Ui.S(30),
                     inH = Ui.S(34), bottom = Ui.S(16), maxH = Ui.S(470);
        readonly int lineH;
        readonly List<KeyValuePair<Rectangle, TodoItem>> shownRows = new List<KeyValuePair<Rectangle, TodoItem>>();
        readonly Dictionary<string, string> cutText = new Dictionary<string, string>();   // что не влезло: ключ строки -> полный текст
        Rectangle inputRect;
        bool typing;
        TodoItem editing;      // null, пока пишем новое дело
        TodoItem menuItem;
        string draft = "", hot, down;
        int scroll;

        public TodoCard(TodoModule m) : base(m)
        {
            todo = m;
            strike = new Font("Segoe UI", (float)(14 * Ui.Scale), FontStyle.Strikeout, GraphicsUnit.Pixel);
            lineH = (int)Math.Ceiling(TextFit.LineHeight(Ui.Font(14)));
            hint = new GlassHint(this);
            input = new GlassInput(this, 200);
            input.Submit += Submit;
            input.Cancel += EndTyping;
            TodoList.Changed += ListChanged;
            tick.Tick += (s, e) => TodoList.Purge();
        }

        // Карточка не отнимает фокус: клавиатуру берёт, только пока пишем.
        protected override bool ShowWithoutActivation => true;

        int ListTop => top + headH + Ui.S(8);
        int ListMax => maxH - ListTop - Ui.S(6) - inH - bottom;
        int ListH => Math.Min(TotalH(), Math.Max(pitch, ListMax));

        // Ширина текста дела: от кружка до правого края строки.
        int TextW => cardW - 2 * pad - Ui.S(28);

        TextFit.Fit Wrap(TodoItem it) { return TextFit.Wrap(it.Text, Ui.Font(14), TextW, MaxLines); }

        // Высота строки дела растёт на каждую перенесённую строку текста.
        int RowHeight(TodoItem it) { return rowH + (Math.Max(1, Wrap(it).Lines.Count) - 1) * lineH; }

        int Gap => pitch - rowH;

        int TotalH()
        {
            int t = 0;
            foreach (var it in TodoList.Items) t += RowHeight(it) + Gap;
            return t;
        }

        int OffsetOf(TodoItem x)
        {
            int t = 0;
            foreach (var it in TodoList.Items)
            {
                if (it == x) break;
                t += RowHeight(it) + Gap;
            }
            return t;
        }

        protected override Size CardSize()
        {
            int lh = TodoList.Items.Count == 0 ? 0 : ListH + Ui.S(2);
            return new Size(cardW, ListTop + lh + Ui.S(4) + inH + bottom);
        }

        protected override Point DefaultLocation(Rectangle wa)
        {
            int y = Math.Max(wa.Top, Math.Min(wa.Top + Ui.S(380), wa.Bottom - CardSize().Height - Ui.S(16)));
            return new Point(wa.Right - cardW - Ui.S(400), y);
        }

        void ListChanged()
        {
            if (IsDisposed) return;
            if (editing != null && !TodoList.Items.Contains(editing)) EndTyping();
            ClampScroll();
            FitSize();
            Invalidate();
        }

        void ClampScroll()
        {
            int max = Math.Max(0, TotalH() - ListH);
            scroll = Math.Max(0, Math.Min(max, scroll));
        }

        // ---------------------------------------------------------------- ввод

        void BeginNew()
        {
            if (typing && editing == null) return;
            editing = null;
            typing = true;
            input.Begin(draft, false);
            Invalidate();
        }

        void BeginEdit(TodoItem it)
        {
            if (!TodoList.Items.Contains(it)) return;
            if (typing && editing == null) draft = input.Text;
            editing = it;
            typing = true;
            // прокручиваем, чтобы дело было видно
            int y0 = OffsetOf(it), y1 = y0 + RowHeight(it) + Gap;
            if (y0 < scroll) scroll = y0;
            else if (y1 > scroll + ListH) scroll = y1 - ListH;
            ClampScroll();
            input.Begin(it.Text, true);
            Invalidate();
        }

        void Submit()
        {
            if (editing != null)
            {
                var it = editing;
                string text = input.Text;
                EndTyping();
                TodoList.Edit(it, text);
                return;
            }
            string t = input.Text.Trim();
            if (t.Length == 0) { EndTyping(); return; }
            draft = "";
            input.Begin("", false);   // остаёмся в строке: можно сразу писать следующее дело
            TodoList.Add(t);
            scroll = TodoModule.NewStyle == 1 ? int.MaxValue : 0;
            ClampScroll();
            Invalidate();
        }

        void EndTyping()
        {
            if (!typing) return;
            typing = false;
            editing = null;
            input.End();
            Invalidate();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!typing) return;
            if (editing == null) draft = input.Text;
            EndTyping();
        }

        // Щелчок по делу не уводит фокус из активного окна; строку ввода и правую кнопку пропускаем.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21 && !typing)   // WM_MOUSEACTIVATE
            {
                int msg = (int)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                if (msg == 0x201 && !inputRect.Contains(PointToClient(Cursor.Position)))
                {
                    m.Result = (IntPtr)3;    // MA_NOACTIVATE
                    return;
                }
            }
            base.WndProc(ref m);
        }

        // ---------------------------------------------------------------- мышь

        protected override bool HandleMouseDown(Point p)
        {
            hint.Hide();
            down = hits.KeyAt(p);
            return down != null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                menuItem = null;
                foreach (var r in shownRows) if (r.Key.Contains(e.Location)) menuItem = r.Value;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && down != null && hits.KeyAt(e.Location) == down) hits.ClickAt(e.Location);
            if (e.Button == MouseButtons.Left) down = null;
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string k = hits.KeyAt(e.Location);
            Cursor = hits.CursorAt(e.Location);
            if (k != hot) { hot = k; Invalidate(); }
            // полный текст дела, если он не влез в строки
            string full = null;
            if (k != null && !cutText.TryGetValue(k, out full)) full = null;
            hint.Set(full != null ? k : null, full);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hint.Set(null, null);
            if (hot != null) { hot = null; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int before = scroll;
            scroll -= e.Delta * pitch / 120;
            ClampScroll();
            if (scroll != before) { hot = null; hint.Set(null, null); Invalidate(); }
        }

        protected override void AddMenuItems(ContextMenuStrip menu)
        {
            var it = menuItem;
            menuItem = null;
            if (it != null && TodoList.Items.Contains(it))
            {
                menu.Items.Insert(0, new ToolStripMenuItem("Изменить", null, (s, e) => BeginEdit(it)));
                menu.Items.Insert(1, new ToolStripMenuItem("Удалить", null, (s, e) => TodoList.Remove(it)));
                menu.Items.Insert(2, new ToolStripMenuItem("Наверх", null, (s, e) => TodoList.MoveTop(it)) { Enabled = TodoList.Items.IndexOf(it) > 0 });
                menu.Items.Insert(3, new ToolStripSeparator());
            }
            menu.Items.Add(new ToolStripMenuItem("Очистить выполненные", null, (s, e) => TodoList.ClearDone()) { Enabled = TodoList.DoneCount > 0 });
        }

        // ---------------------------------------------------------------- отрисовка

        protected override void DrawContent(Graphics g, int w, int h)
        {
            hits.Clear();
            shownRows.Clear();
            cutText.Clear();
            var list = TodoList.Items;

            // счётчик справа, заголовок занимает только оставшееся место
            float countW = 0;
            if (list.Count > 0)
            {
                string cnt = list.Count(i => i.Done) + " из " + list.Count;
                countW = TextFit.Width(cnt, Ui.Font(12)) + Ui.S(10);
                Ui.Text2(g, cnt, Ui.Font(12), Ui.Faint, new RectangleF(pad, top, w - 2 * pad, headH), Ui.Right);
            }
            Ui.Text2(g, "ДЕЛА НА СЕГОДНЯ", Ui.Font(11, true), Ui.Faint, new RectangleF(pad, top, Math.Max(0, w - 2 * pad - countW), headH), Ui.Left);

            int y = ListTop;
            if (list.Count > 0)
            {
                int lh = ListH;
                var view = new Rectangle(0, y, w, lh);
                var st = g.Save();
                g.SetClip(view);
                int total = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    int rh = RowHeight(list[i]);
                    int ry = y + total - scroll;
                    total += rh + Gap;
                    if (ry + rh < y || ry > y + lh) continue;
                    DrawItem(g, list[i], i, new Rectangle(pad - Ui.S(6), ry, w - 2 * pad + Ui.S(12), rh), view);
                }
                g.Restore(st);
                // полоска прокрутки, если дела не помещаются
                if (total > lh)
                {
                    float bh = Math.Max(Ui.S(18), lh * (float)lh / total);
                    float by = y + (lh - bh) * scroll / (float)(total - lh);
                    Ui.FillRound(g, new RectangleF(w - Ui.S(7), by, Ui.S(3), bh), Ui.S(1.5), Color.FromArgb(70, 255, 255, 255));
                }
                y += lh + Ui.S(2);
            }

            // строка «Новое дело…»
            inputRect = new Rectangle(pad, y + Ui.S(4), w - 2 * pad, inH);
            bool here = typing && editing == null;
            Ui.FillRound(g, inputRect, Ui.S(9), here ? Color.FromArgb(26, 255, 255, 255) : hot == "in" ? Color.FromArgb(22, 255, 255, 255) : Color.FromArgb(15, 255, 255, 255));
            if (here) Ui.StrokeRound(g, inputRect, Ui.S(9), Color.FromArgb(150, Ui.Accent));
            float px = inputRect.X + Ui.S(17), py = inputRect.Y + inH / 2f, ps = Ui.S(5);
            using (var p = new Pen(Ui.Faint, 1.6f * Ui.Scale) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
            {
                g.DrawLine(p, px - ps, py, px + ps, py);
                g.DrawLine(p, px, py - ps, px, py + ps);
            }
            var tr = new RectangleF(inputRect.X + Ui.S(32), inputRect.Y, inputRect.Width - Ui.S(42), inH);
            var f = Ui.Font(13);
            if (here)
            {
                if (input.Text.Length == 0) Ui.Text2(g, "Новое дело…", f, Ui.Faint, tr, Ui.Left);
                input.Draw(g, tr, f, Ui.Text, Color.FromArgb(90, Ui.Accent));
            }
            else if (draft.Length > 0)
            {
                Ui.Text2(g, draft, f, Ui.Muted, tr, Ui.Left);
                if (TextFit.Width(draft, f) > tr.Width - 1) cutText["in"] = draft;
            }
            else Ui.Text2(g, "Новое дело…", f, Ui.Faint, tr, Ui.Left);
            hits.Add(inputRect, "in", BeginNew, Cursors.IBeam);
        }

        void DrawItem(Graphics g, TodoItem it, int i, Rectangle row, Rectangle view)
        {
            string key = "it:" + i;
            bool edit = typing && editing == it;
            if (edit) Ui.FillRound(g, row, Ui.S(8), Color.FromArgb(26, 255, 255, 255));
            else if (hot == key) Ui.FillRound(g, row, Ui.S(8), Ui.Panel);

            float d = Ui.S(18);
            var box = new RectangleF(pad, row.Y + (rowH - d) / 2f, d, d);   // кружок напротив первой строки
            float pw = 1.5f * Ui.Scale;
            var inner = new RectangleF(box.X + pw / 2, box.Y + pw / 2, d - pw, d - pw);
            if (it.Done)
            {
                using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, box);
                using (var p = new Pen(Ui.AccentInk, 1.8f * Ui.Scale) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                {
                    float k = d / 18f;
                    g.DrawLines(p, new[] {
                        new PointF(box.X + 5.5f * k, box.Y + 9.2f * k),
                        new PointF(box.X + 7.8f * k, box.Y + 11.5f * k),
                        new PointF(box.X + 12.5f * k, box.Y + 6.5f * k) });
                }
            }
            else
                using (var p = new Pen(Color.FromArgb(hot == key ? 190 : 115, 255, 255, 255), pw)) g.DrawEllipse(p, inner);

            float tx = box.Right + Ui.S(10), tw = row.Right - Ui.S(6) - tx;
            // при правке строка ввода одна и прокручивается вбок: так виден весь текст
            if (edit) input.Draw(g, new RectangleF(tx, row.Y, tw, row.Height), Ui.Font(14), Ui.Text, Color.FromArgb(90, Ui.Accent));
            else
            {
                var fit = Wrap(it);
                float ly = row.Y + (rowH - lineH) / 2f;
                for (int k = 0; k < fit.Lines.Count; k++)   // зачёркивание у каждой строки своё
                    Ui.Text2(g, fit.Lines[k], it.Done ? strike : Ui.Font(14), it.Done ? Ui.Faint : Ui.Text, new RectangleF(tx, ly + k * lineH, tw, lineH), Ui.Left);
                if (fit.Cut) cutText[key] = it.Text;
            }

            var hit = Rectangle.Intersect(row, view);
            shownRows.Add(new KeyValuePair<Rectangle, TodoItem>(hit, it));
            if (!edit && hit.Height > 0) hits.Add(hit, key, () => TodoList.Toggle(it));
            else if (edit) hits.Add(hit, key, null, Cursors.IBeam);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            tick.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TodoList.Changed -= ListChanged;
                tick.Dispose();
                input.Dispose();
                hint.Dispose();
                strike.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Строка ввода прямо на стекле. Настоящий TextBox стоит за краем окна и принимает клавиши,
    // а текст, курсор и выделение рисуем сами: обычный TextBox на прозрачном стекле выглядит чужим.
    class GlassInput : IDisposable
    {
        static readonly StringFormat Typo = MakeFormat();
        public readonly TextBox Box = new TextBox();
        readonly Control host;
        readonly Timer blink = new Timer { Interval = 530 };
        bool caret = true;
        float scroll;
        public event Action Submit, Cancel;

        public GlassInput(Control host, int maxLength)
        {
            this.host = host;
            Box.BorderStyle = BorderStyle.None;
            Box.MaxLength = maxLength;
            Box.TabStop = false;
            Box.Bounds = new Rectangle(-5000, 0, Ui.S(200), Ui.S(20));
            host.Controls.Add(Box);
            Box.TextChanged += (s, e) => Kick();
            Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; if (Submit != null) Submit(); return; }
                if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; if (Cancel != null) Cancel(); return; }
                if (e.Control && e.KeyCode == Keys.A) { Box.SelectAll(); e.Handled = e.SuppressKeyPress = true; }
                // курсор сдвигается уже после KeyDown
                if (host.IsHandleCreated) host.BeginInvoke(new Action(Kick));
            };
            Box.KeyUp += (s, e) => Kick();
            // без фокуса курсор не рисуется: не перерисовываем окно зря
            blink.Tick += (s, e) => { if (!Box.Focused) return; caret = !caret; host.Invalidate(); };
            Box.GotFocus += (s, e) => Kick();
            Box.LostFocus += (s, e) => host.Invalidate();
        }

        public string Text => Box.Text;

        public void Begin(string text, bool selectAll)
        {
            Box.Text = text ?? "";
            if (selectAll) Box.SelectAll();
            else Box.SelectionStart = Box.Text.Length;
            scroll = 0;
            var f = host.FindForm();
            if (f != null)
            {
                f.Activate();
                WNative.SetForegroundWindow(f.Handle);
            }
            Box.Focus();
            blink.Start();
            Kick();
        }

        public void End()
        {
            blink.Stop();
            var f = host.FindForm();
            if (f != null && f.ActiveControl == Box) f.ActiveControl = null;
            Box.Text = "";
            host.Invalidate();
        }

        void Kick()
        {
            caret = true;
            if (blink.Enabled) { blink.Stop(); blink.Start(); }
            host.Invalidate();
        }

        public void Draw(Graphics g, RectangleF r, Font f, Color color, Color selection)
        {
            string s = Box.Text;
            int a = Math.Max(0, Math.Min(s.Length, Box.SelectionStart));
            int b = Math.Max(a, Math.Min(s.Length, a + Box.SelectionLength));
            float cx = Width(g, s.Substring(0, a), f);
            float full = Width(g, s, f);
            float room = r.Width - Ui.S(2);
            if (cx - scroll > room) scroll = cx - room;
            if (cx - scroll < 0) scroll = cx;
            if (full - scroll < room) scroll = Math.Max(0, full - room);

            var st = g.Save();
            g.SetClip(r, System.Drawing.Drawing2D.CombineMode.Intersect);   // не выходим ни за поле, ни за уже заданную область
            float x0 = r.X - scroll;
            float th = f.GetHeight(g);
            float ty = r.Y + (r.Height - th) / 2;
            if (b > a)
            {
                float sb = Width(g, s.Substring(0, b), f);
                using (var br = new SolidBrush(selection)) g.FillRectangle(br, x0 + cx, ty, sb - cx, th);
            }
            using (var br = new SolidBrush(color)) g.DrawString(s, f, br, x0, ty, Typo);
            if (caret && b == a && Box.Focused)
                using (var p = new Pen(color, Math.Max(1f, 1.2f * Ui.Scale))) g.DrawLine(p, x0 + cx + 0.5f, ty + Ui.S(2), x0 + cx + 0.5f, ty + th - Ui.S(2));
            g.Restore(st);
        }

        static float Width(Graphics g, string s, Font f)
        {
            return s.Length == 0 ? 0 : g.MeasureString(s, f, PointF.Empty, Typo).Width;
        }

        static StringFormat MakeFormat()
        {
            var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
            sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            return sf;
        }

        public void Dispose() { blink.Dispose(); }
    }

    // Перенос текста по словам на несколько строк; что не влезло, заканчивается многоточием.
    // Слово режется, только если оно одно шире строки. Мерим на своей картинке с теми же
    // настройками, что при отрисовке: размеры нужны и вне OnPaint (высота карточки).
    static class TextFit
    {
        public class Fit
        {
            public readonly List<string> Lines = new List<string>();
            public bool Cut;
        }

        static readonly StringFormat Measure = new StringFormat { FormatFlags = StringFormatFlags.NoWrap };
        static readonly Dictionary<string, Fit> cache = new Dictionary<string, Fit>();
        static Bitmap probeBmp;
        static Graphics probe;

        static Graphics Probe
        {
            get
            {
                if (probe == null)
                {
                    probeBmp = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    probe = Graphics.FromImage(probeBmp);
                    Ui.Prepare(probe);
                }
                return probe;
            }
        }

        public static float Width(string s, Font f)
        {
            return string.IsNullOrEmpty(s) ? 0 : Probe.MeasureString(s, f, PointF.Empty, Measure).Width;
        }

        public static float LineHeight(Font f) { return f.GetHeight(Probe); }

        public static Fit Wrap(string s, Font f, float width, int maxLines)
        {
            s = s ?? "";
            string key = f.Name + "|" + f.Size + "|" + (int)f.Style + "|" + width + "|" + maxLines + "|" + s;
            Fit fit;
            if (cache.TryGetValue(key, out fit)) return fit;
            fit = Build(s, f, Math.Max(1, width - 1), Math.Max(1, maxLines));
            if (cache.Count > 4000) cache.Clear();
            cache[key] = fit;
            return fit;
        }

        static Fit Build(string s, Font f, float width, int max)
        {
            var fit = new Fit();
            var words = new List<string>(s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            int i = 0;
            while (i < words.Count && fit.Lines.Count < max)
            {
                bool last = fit.Lines.Count == max - 1;
                string w = words[i];
                if (Width(w, f) > width)
                {
                    if (last)
                    {
                        fit.Lines.Add(Chop(string.Join(" ", words.Skip(i)), f, width));
                        fit.Cut = true;
                        return fit;
                    }
                    // одно слово шире строки: здесь без разрыва никак
                    int n = Prefix(w, f, width, "");
                    fit.Lines.Add(w.Substring(0, n));
                    words[i] = w.Substring(n);
                    continue;
                }
                string line = w;
                i++;
                while (i < words.Count && Width(line + " " + words[i], f) <= width) line += " " + words[i++];
                if (last && i < words.Count)
                {
                    // последняя строка: убираем слова, пока не влезет многоточие
                    fit.Cut = true;
                    string cut = line + "…";
                    while (Width(cut, f) > width)
                    {
                        int sp = line.LastIndexOf(' ');
                        if (sp <= 0) { cut = Chop(line, f, width); break; }
                        line = line.Substring(0, sp);
                        cut = line + "…";
                    }
                    line = cut;
                }
                fit.Lines.Add(line);
            }
            return fit;
        }

        // Самое длинное начало строки (не меньше одного знака), которое с хвостом tail влезает в ширину.
        static int Prefix(string s, Font f, float width, string tail)
        {
            int lo = 1, hi = s.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Width(s.Substring(0, mid).TrimEnd() + tail, f) <= width) lo = mid;
                else hi = mid - 1;
            }
            // пару суррогатов (эмодзи) не разрываем
            if (lo > 1 && lo < s.Length && char.IsHighSurrogate(s[lo - 1])) lo--;
            return lo;
        }

        static string Chop(string s, Font f, float width)
        {
            return s.Substring(0, Prefix(s, f, width, "…")).TrimEnd() + "…";
        }
    }

    // Подсказка с полным текстом: появляется, когда мышь задержалась на одном месте.
    // key — что сейчас под мышью (null — ничего); пока key тот же, подсказка не мигает.
    class GlassHint : IDisposable
    {
        readonly Control host;
        readonly ToolTip tip = new ToolTip { ShowAlways = true, UseAnimation = false, UseFading = false };
        readonly Timer wait = new Timer { Interval = 600 };
        string key, text;
        bool shown;

        public GlassHint(Control host)
        {
            this.host = host;
            wait.Tick += (s, e) => { wait.Stop(); Pop(); };
        }

        public void Set(string k, string t)
        {
            if (string.IsNullOrEmpty(t)) k = null;
            if (k == key) return;
            Hide();
            key = k;
            text = t;
            if (k != null) wait.Start();
        }

        public void Hide()
        {
            wait.Stop();
            key = null;
            if (!shown) return;
            shown = false;
            try { tip.Hide(host); } catch { }
        }

        void Pop()
        {
            if (key == null || host.IsDisposed || !host.Visible) return;
            var p = host.PointToClient(Cursor.Position);
            if (!host.ClientRectangle.Contains(p)) { Hide(); return; }
            try
            {
                tip.Show(Breaks(text, 60), host, p.X + Ui.S(12), p.Y + Ui.S(22), 8000);
                shown = true;
            }
            catch { }
        }

        // Длинный текст подсказки разбиваем на строки по словам.
        static string Breaks(string s, int width)
        {
            var sb = new StringBuilder();
            int len = 0;
            foreach (var w in s.Split(' '))
            {
                if (len > 0 && len + 1 + w.Length > width) { sb.Append('\n'); len = 0; }
                else if (len > 0) { sb.Append(' '); len++; }
                sb.Append(w);
                len += w.Length;
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            wait.Dispose();
            tip.Dispose();
        }
    }
}
