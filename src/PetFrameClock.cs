using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Glasswing
{
    // Wait on the compositor off the UI thread and keep at most one pending frame.
    sealed class PetFrameClock : IDisposable
    {
        [DllImport("dwmapi.dll")] static extern int DwmFlush();

        readonly Control target;
        readonly Action draw;
        int running, pending;

        public PetFrameClock(Control target, Action draw)
        {
            this.target = target;
            this.draw = draw;
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref running, 1) != 0) return;
            new Thread(Loop) { IsBackground = true, Name = "Glasswing pet frames" }.Start();
        }

        void Loop()
        {
            bool useCompositor = true;
            var clock = Stopwatch.StartNew();
            while (Volatile.Read(ref running) != 0)
            {
                long previous = clock.ElapsedMilliseconds;
                if (useCompositor)
                {
                    try { useCompositor = DwmFlush() == 0; }
                    catch (DllNotFoundException) { useCompositor = false; }
                    catch (EntryPointNotFoundException) { useCompositor = false; }
                }
                int wait = (useCompositor ? 8 : 16) - (int)(clock.ElapsedMilliseconds - previous);
                if (wait > 0) Thread.Sleep(wait);
                if (Volatile.Read(ref running) == 0 || Interlocked.Exchange(ref pending, 1) != 0) continue;
                try { target.BeginInvoke((Action)Draw); }
                catch (InvalidOperationException) { Interlocked.Exchange(ref pending, 0); }
            }
        }

        void Draw()
        {
            try { if (Volatile.Read(ref running) != 0) draw(); }
            finally { Interlocked.Exchange(ref pending, 0); }
        }

        public void Dispose() { Interlocked.Exchange(ref running, 0); }
    }
}
