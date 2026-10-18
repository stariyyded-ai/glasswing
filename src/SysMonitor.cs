using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Glasswing
{
    // ---------------------------------------------------------------- монитор системы

    class SysMonitorModule : Module
    {
        SysMonitorCard card;
        public override string Id => "sysmon";
        public override string Name => "Монитор системы";
        public override string Short => "Процессор, видеокарта, память";
        public override string Description => "Загрузка процессора и видеокарты, температура видеокарты, занятая видеопамять и оперативная память с плавными полосками.";
        public override IconKind Icon => IconKind.Monitor;
        public override bool Running => card != null && !card.IsDisposed;
        public override bool CanReset => true;

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

        public override void Start()
        {
            if (Running) return;
            card = new SysMonitorCard(this);
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

        public override void SetHidden(bool hidden) { if (Running) card.SetHidden(hidden); }
        // Последние показания датчиков для питомца; пусто, если виджет выключен.
        public SysSensors.Snapshot? Sensors => Running ? card.Sensors : (SysSensors.Snapshot?)null;
        public override void GlassChanged() { if (Running) card.ApplyGlass(); }
        public override bool HasGlass => true;
        public override void ResetPosition() { if (Running) card.ResetPosition(); }

        // У прежнего виджета своих настроек не было: только «поверх окон» и «закрепить».
        public override IEnumerable<Row> Rows()
        {
            yield return new Row { Label = "Поверх всех окон", On = () => TopMost, Click = () => TopMost = !TopMost };
            yield return new Row { Label = "Закрепить на месте", On = () => Locked, Click = () => Locked = !Locked };
        }
    }

    class SysMonitorCard : GlassCard
    {
        static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        static readonly Color ColText = Color.White;
        static readonly Color ColMuted = Color.FromArgb(185, 255, 255, 255);
        static readonly Color ColTrack = Color.FromArgb(55, 255, 255, 255);
        static readonly Color ColWarn = Color.FromArgb(255, 255, 196, 90);
        static readonly Color ColHot = Color.FromArgb(255, 255, 110, 100);

        class Line { public string Key, Label, Value; public double Frac; public float Temp = -1; }

        readonly SysSensors sensors = new SysSensors();
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 250 };
        readonly Dictionary<string, double> bars = new Dictionary<string, double>();
        readonly DateTime started = DateTime.Now;
        List<Line> lines;
        string shown = "";
        bool diagSaved;
        public SysSensors.Snapshot Sensors => sensors.Read();

        public SysMonitorCard(SysMonitorModule m) : base(m)
        {
            lines = BuildLines(sensors.Read());
            poll.Tick += (s, e) => UpdateCard();
            sensors.Start();
        }

        static List<Line> BuildLines(SysSensors.Snapshot d)
        {
            var rows = new List<Line>();
            string cv = d.Cpu >= 0 ? d.Cpu.ToString("0", Ru) + " %" : "—";
            rows.Add(new Line { Key = "cpu", Label = "Процессор", Value = cv, Frac = Math.Max(0.0, d.Cpu / 100.0) });
            string gv = d.Gpu >= 0 ? d.Gpu.ToString("0", Ru) + " %" : "—";
            if (d.GpuTemp >= 0) gv += "  ·  " + (int)Math.Round(d.GpuTemp) + "°";
            rows.Add(new Line { Key = "gpu", Label = "Видеокарта", Value = gv, Frac = Math.Max(0.0, d.Gpu / 100.0), Temp = d.GpuTemp });
            if (d.VramTotalGb > 0)
                rows.Add(new Line { Key = "vram", Label = "Видеопамять", Value = d.VramUsedGb.ToString("0.0", Ru) + " / " + d.VramTotalGb.ToString("0", Ru) + " ГБ", Frac = d.VramUsedGb / d.VramTotalGb });
            string rv = "—";
            double rf = 0;
            if (d.RamTotalGb > 0)
            {
                rv = d.RamUsedGb.ToString("0.0", Ru) + " / " + d.RamTotalGb.ToString("0", Ru) + " ГБ";
                rf = d.RamUsedGb / d.RamTotalGb;
            }
            rows.Add(new Line { Key = "ram", Label = "Память", Value = rv, Frac = rf });
            return rows;
        }

        protected override Size CardSize()
        {
            int n = lines == null ? 3 : lines.Count;
            return new Size(Ui.S(290), Ui.S(16) + (n - 1) * Ui.S(40) + Ui.S(31) + Ui.S(16));
        }

        protected override Point DefaultLocation(Rectangle wa)
        {
            return new Point(wa.Right - CardSize().Width - Ui.S(110), wa.Top + Ui.S(160));
        }

        public void SetHidden(bool hidden)
        {
            Visible = !hidden;
            sensors.Paused = hidden;
        }

        void UpdateCard()
        {
            if (!diagSaved && (DateTime.Now - started).TotalSeconds > 8)
            {
                // короткая справка о том, какие датчики нашлись: помогает разобраться, если чего-то не видно
                diagSaved = true;
                try { File.WriteAllText(Path.Combine(Settings.Dir, "sensors.txt"), sensors.Diag, Encoding.UTF8); } catch { }
            }
            var rows = BuildLines(sensors.Read());
            bool changed = false;
            foreach (var r in rows)
            {
                double target = Math.Max(0.0, Math.Min(1.0, r.Frac));
                double cur;
                if (!bars.TryGetValue(r.Key, out cur)) cur = target;
                // полоски плавно догоняют новое значение
                double next = cur + (target - cur) * 0.35;
                if (Math.Abs(target - next) < 0.002) next = target;
                if (Math.Abs(next - cur) > 0.0005) changed = true;
                bars[r.Key] = next;
            }
            var sb = new StringBuilder();
            foreach (var r in rows) sb.Append(r.Value).Append('|');
            string text = sb.ToString();
            bool resize = rows.Count != lines.Count;
            lines = rows;
            if (resize) FitSize();
            if (changed || text != shown)
            {
                shown = text;
                Invalidate();
            }
        }

        static Color LoadColor(double frac)
        {
            if (frac >= 0.9) return ColHot;
            if (frac >= 0.7) return ColWarn;
            return ColText;
        }

        protected override void DrawContent(Graphics g, int w, int h)
        {
            int pad = Ui.S(16);
            float colW = w - 2 * pad;
            float y = pad;
            float barH = Ui.S(5);
            foreach (var r in lines)
            {
                var box = new RectangleF(pad, y - Ui.S(1), colW, Ui.S(20));
                Ui.Text2(g, r.Label, Ui.Font(13), ColMuted, box, Ui.Left, true);
                Color vc = ColText;
                if (r.Key == "gpu" && r.Temp >= 88) vc = ColHot;
                else if (r.Key == "gpu" && r.Temp >= 80) vc = ColWarn;
                Ui.Text2(g, r.Value, Ui.Font(13.5, true), vc, box, Ui.Right, true);
                float by = y + Ui.S(25);
                Ui.FillRound(g, new RectangleF(pad, by, colW, barH), barH / 2, ColTrack);
                double frac;
                if (!bars.TryGetValue(r.Key, out frac)) frac = 0;
                if (frac > 0.004)
                    Ui.FillRound(g, new RectangleF(pad, by, (float)Math.Max(barH, colW * frac), barH), barH / 2, LoadColor(r.Frac));
                y += Ui.S(40);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateCard();
            poll.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { poll.Dispose(); sensors.Stop(); }
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------- датчики (в фоновом потоке)

    // Те же источники, что и в прежнем скрипте: GetSystemTimes, GlobalMemoryStatusEx,
    // счётчики PDH видеокарты, nvidia-smi для NVIDIA и библиотека драйвера ADL для AMD.
    class SysSensors
    {
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        [StructLayout(LayoutKind.Sequential)]
        struct MEMSTAT
        {
            public uint Length; public uint Load; public ulong TotalPhys; public ulong AvailPhys;
            public ulong TotalPage; public ulong AvailPage; public ulong TotalVirt; public ulong AvailVirt; public ulong AvailExt;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQueryW(string src, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);

        // AMD: библиотека драйвера ADL
        delegate IntPtr AdlAlloc(int size);
        static IntPtr AllocMem(int size) { return Marshal.AllocHGlobal(size); }
        static readonly AdlAlloc adlAlloc = AllocMem;
        [DllImport("atiadlxx.dll")] static extern int ADL2_Main_Control_Create(AdlAlloc cb, int connected, out IntPtr ctx);
        [DllImport("atiadlxx.dll")] static extern int ADL2_Main_Control_Destroy(IntPtr ctx);
        [DllImport("atiadlxx.dll")] static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr ctx, out int n);
        [DllImport("atiadlxx.dll")] static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr ctx, IntPtr info, int size);
        [DllImport("atiadlxx.dll")] static extern int ADL2_Overdrive_Caps(IntPtr ctx, int index, out int supported, out int enabled, out int version);
        [DllImport("atiadlxx.dll")] static extern int ADL2_New_QueryPMLogData_Get(IntPtr ctx, int index, IntPtr output);
        [DllImport("atiadlxx.dll")] static extern int ADL2_OverdriveN_Temperature_Get(IntPtr ctx, int index, int type, out int temp);

        public struct Snapshot
        {
            public float Cpu, Gpu, GpuTemp;
            public double RamUsedGb, RamTotalGb, VramUsedGb, VramTotalGb;
        }

        // элемент массива PDH: указатель на имя, затем статус и значение (double, выровнено по 8 байт)
        const int PdhItem = 24, PdhStatus = 8, PdhValue = 16;
        const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;

        readonly object sync = new object();
        readonly ManualResetEvent quit = new ManualResetEvent(false);
        readonly StringBuilder diag = new StringBuilder();
        Snapshot cur = new Snapshot { Cpu = -1, Gpu = -1, GpuTemp = -1 };
        Snapshot shared = new Snapshot { Cpu = -1, Gpu = -1, GpuTemp = -1 };
        public volatile bool Paused;

        long lastIdle, lastTotal;
        IntPtr query, gpuCounter, vramCounter, adlCtx;
        string smiPath;
        int tick, adlIndex = -1, odVersion;
        ulong vramTotal;

        public Snapshot Read() { lock (sync) return shared; }

        public string Diag { get { lock (sync) return diag.ToString(); } }

        void Note(string s) { lock (sync) diag.Append(s); }

        public void Start()
        {
            var t = new Thread(Run) { IsBackground = true, Name = "sysmon" };
            t.Start();
        }

        public void Stop()
        {
            try { quit.Set(); } catch (ObjectDisposedException) { }
        }

        void Run()
        {
            try
            {
                Init();
                while (!quit.WaitOne(0))
                {
                    if (!Paused)
                    {
                        try { ReadCpu(); } catch { }
                        try { ReadRam(); } catch { }
                        if (query != IntPtr.Zero) { try { PdhCollectQueryData(query); } catch { } }
                        try { ReadGpu(); } catch { }
                        if (smiPath == null) { try { ReadVram(); } catch { } }
                        if (smiPath != null && tick % 3 == 0) { try { ReadSmi(); } catch { } }
                        if (adlIndex >= 0) { try { ReadAdl(); } catch (Exception e) { if (tick < 3) Note("adlread: " + e.Message + "; "); } }
                        tick++;
                        lock (sync) shared = cur;
                    }
                    if (quit.WaitOne(1500)) break;
                }
            }
            catch (Exception ex) { Log.Write("sysmon датчики: " + ex.Message); }
            finally
            {
                try { if (query != IntPtr.Zero) PdhCloseQuery(query); } catch { }
                try { if (adlCtx != IntPtr.Zero) ADL2_Main_Control_Destroy(adlCtx); } catch { }
                quit.Dispose();
            }
        }

        void Init()
        {
            string[] smi = {
                Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\nvidia-smi.exe"),
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\NVIDIA Corporation\NVSMI\nvidia-smi.exe")
            };
            foreach (string p in smi) { if (File.Exists(p)) { smiPath = p; break; } }
            try
            {
                // английские имена счётчиков работают и на русской Windows
                if (PdhOpenQueryW(null, IntPtr.Zero, out query) == 0)
                    PdhAddEnglishCounterW(query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out gpuCounter);
                if (query != IntPtr.Zero)
                    PdhAddEnglishCounterW(query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out vramCounter);
            }
            catch { }
            try { vramTotal = FindVramTotal(); } catch { }
            if (smiPath == null) { try { InitAdl(); } catch (Exception e) { Note("adl: " + e.Message + "; "); } }
            Note("smi=" + (smiPath != null) + " adl=" + adlIndex + " od=" + odVersion + " vram=" + vramTotal + "; ");
        }

        void ReadCpu()
        {
            long idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return;
            long total = kernel + user;   // время ядра уже включает простой
            if (lastTotal != 0 && total > lastTotal)
            {
                double busy = 1.0 - (double)(idle - lastIdle) / (total - lastTotal);
                cur.Cpu = (float)Math.Max(0, Math.Min(100, busy * 100));
            }
            lastIdle = idle; lastTotal = total;
        }

        void ReadRam()
        {
            var m = new MEMSTAT { Length = (uint)Marshal.SizeOf(typeof(MEMSTAT)) };
            if (!GlobalMemoryStatusEx(ref m)) return;
            cur.RamTotalGb = m.TotalPhys / 1073741824.0;
            cur.RamUsedGb = (m.TotalPhys - m.AvailPhys) / 1073741824.0;
        }

        // Значения счётчика по всем экземплярам; null, если прочитать не вышло.
        static double[] CounterArray(IntPtr counter, uint format)
        {
            uint size = 0, count;
            PdhGetFormattedCounterArrayW(counter, format, ref size, out count, IntPtr.Zero);
            if (size == 0) return null;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, format, ref size, out count, buf) != 0) return null;
                var res = new double[count];
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + (long)i * PdhItem);
                    uint status = (uint)Marshal.ReadInt32(p, PdhStatus);
                    res[i] = status != 0 && status != 1 ? double.NaN : BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, PdhValue));
                }
                return res;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        void ReadGpu()
        {
            if (gpuCounter == IntPtr.Zero) return;
            var vals = CounterArray(gpuCounter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100);
            if (vals == null) return;
            double sum = 0;
            foreach (double v in vals) if (!double.IsNaN(v)) sum += v;
            cur.Gpu = (float)Math.Max(0, Math.Min(100, sum));
        }

        // объём видеопамяти из реестра драйвера (самая большая видеокарта)
        static ulong FindVramTotal()
        {
            ulong best = 0;
            using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
            {
                if (cls == null) return 0;
                foreach (string name in cls.GetSubKeyNames())
                {
                    try
                    {
                        using (var k = cls.OpenSubKey(name))
                        {
                            if (k == null) continue;
                            object v = k.GetValue("HardwareInformation.qwMemorySize");
                            ulong size = 0;
                            if (v is long) size = (ulong)(long)v;
                            else if (v is byte[] && ((byte[])v).Length >= 8) size = BitConverter.ToUInt64((byte[])v, 0);
                            if (size > best) best = size;
                        }
                    }
                    catch { }
                }
            }
            return best;
        }

        // занятая видеопамять: счётчик Windows, работает с любой видеокартой
        void ReadVram()
        {
            if (vramCounter == IntPtr.Zero || vramTotal == 0) return;
            var vals = CounterArray(vramCounter, PDH_FMT_DOUBLE);
            if (vals == null) return;
            double best = 0;
            foreach (double v in vals) if (v > best) best = v;
            cur.VramUsedGb = best / 1073741824.0;
            cur.VramTotalGb = vramTotal / 1073741824.0;
        }

        void InitAdl()
        {
            if (ADL2_Main_Control_Create(adlAlloc, 1, out adlCtx) != 0) { Note("adl create failed; "); adlCtx = IntPtr.Zero; return; }
            int n;
            if (ADL2_Adapter_NumberOfAdapters_Get(adlCtx, out n) != 0 || n <= 0) return;
            const int infoSize = 1572;   // AdapterInfo в 64-битной Windows
            IntPtr buf = Marshal.AllocHGlobal(infoSize * n);
            try
            {
                for (int i = 0; i < infoSize * n; i++) Marshal.WriteByte(buf, i, 0);
                if (ADL2_Adapter_AdapterInfo_Get(adlCtx, buf, infoSize * n) != 0) return;
                for (int i = 0; i < n; i++)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + (long)i * infoSize);
                    int index = Marshal.ReadInt32(p, 4);
                    int vendor = Marshal.ReadInt32(p, 4 + 4 + 256 + 12);
                    if (vendor != 1002) continue;
                    int sup, en, ver;
                    if (ADL2_Overdrive_Caps(adlCtx, index, out sup, out en, out ver) != 0) continue;
                    adlIndex = index; odVersion = ver;
                    if (ReadAdlTemp() > 0) break;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        float ReadAdlTemp()
        {
            if (odVersion >= 8)
            {
                int size = 4 + 256 * 8;
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
                    Marshal.WriteInt32(buf, 0, size);
                    if (ADL2_New_QueryPMLogData_Get(adlCtx, adlIndex, buf) != 0) return -1;
                    // датчик 8: температура видеокарты (edge), 27: горячая точка
                    int sup = Marshal.ReadInt32(buf, 4 + 8 * 8);
                    if (sup != 0) return Marshal.ReadInt32(buf, 4 + 8 * 8 + 4);
                    return -1;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            int t;
            if (ADL2_OverdriveN_Temperature_Get(adlCtx, adlIndex, 1, out t) != 0) return -1;
            return t > 1000 ? t / 1000f : t;
        }

        void ReadAdl()
        {
            float t = ReadAdlTemp();
            if (t > 0 && t < 150) cur.GpuTemp = t;
            if (tick < 3) Note("t=" + t + "; ");
        }

        void ReadSmi()
        {
            var si = new ProcessStartInfo(smiPath, "--query-gpu=temperature.gpu,memory.used,memory.total --format=csv,noheader,nounits")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (var p = Process.Start(si))
            {
                string line = p.StandardOutput.ReadLine();
                if (!p.WaitForExit(2000)) { try { p.Kill(); } catch { } }
                if (string.IsNullOrEmpty(line)) return;
                string[] parts = line.Split(',');
                if (parts.Length < 3) return;
                var inv = CultureInfo.InvariantCulture;
                float temp = float.Parse(parts[0].Trim(), inv);
                double used = double.Parse(parts[1].Trim(), inv) / 1024.0;
                double total = double.Parse(parts[2].Trim(), inv) / 1024.0;
                cur.GpuTemp = temp; cur.VramUsedGb = used; cur.VramTotalGb = total;
            }
        }
    }
}
