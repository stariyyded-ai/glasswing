using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Glasswing")]
[assembly: AssemblyProduct("Glasswing")]
[assembly: AssemblyDescription("Стеклянные виджеты для рабочего стола")]
[assembly: AssemblyCompany("Glasswing")]
[assembly: AssemblyCopyright("Glasswing")]
[assembly: AssemblyVersion("1.5.1.0")]
[assembly: AssemblyFileVersion("1.5.1.0")]
[assembly: AssemblyInformationalVersion("1.5.1")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace Glasswing
{
    static class Program
    {
        public const string MutexName = @"Local\Glasswing_Single";

        [STAThread]
        static void Main(string[] args)
        {
            WNative.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Log.Write("ошибка: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("сбой: " + e.ExceptionObject);

            if (args.Contains("--uninstall")) { Installer.Uninstall(); return; }

            // Запустили скачанный файл: ставим себя на место и запускаем установленную копию.
            string exe = Application.ExecutablePath;
            if (!Installer.IsInstalledCopy(exe) && !args.Contains("--portable"))
            {
                Installer.InstallAndLaunch(exe);
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    EventWaitHandle ev;
                    if (EventWaitHandle.TryOpenExisting(App.ShowEventName, out ev)) { ev.Set(); ev.Dispose(); }
                    return;
                }
                ToolStripManager.Renderer = new DarkRenderer();
                WNative.PreferDark();
                Ui.Init();
                Settings.Load();
                bool firstRun = !Settings.GetBool("app", "FirstRunDone", false);
                Legacy.Migrate();
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Glasswing.app.ico"))
                    App.AppIcon = new System.Drawing.Icon(s);
                App.Core = new App();
                App.Core.Start(firstRun);
                if (args.Contains("--updated")) App.Core.Notify(App.Name, "Обновлено до версии " + App.Version + ".");
                Application.Run();
                mutex.ReleaseMutex();
            }
        }
    }
}
