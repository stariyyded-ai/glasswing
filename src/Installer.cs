using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Glasswing
{
    // Установка без отдельного установщика: Glasswing.exe сам копируется в
    // %LOCALAPPDATA%\Programs\Glasswing, создаёт ярлык в «Пуске» и встаёт в автозапуск.
    static class Installer
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", App.Name);
        public static readonly string Exe = Path.Combine(Dir, App.Name + ".exe");
        static readonly string StartMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), App.Name + ".lnk");
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + App.Name;

        public static bool IsInstalledCopy(string exe)
        {
            return string.Equals(Path.GetFullPath(exe), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase);
        }

        public static bool Autostart
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(App.Name) != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(App.Name, "\"" + Exe + "\"");
                    else k.DeleteValue(App.Name, false);
                }
            }
        }

        // Просим уже запущенный Glasswing закрыться и ждём, пока он освободит файл.
        static void StopRunning()
        {
            EventWaitHandle ev;
            if (!EventWaitHandle.TryOpenExisting(App.ExitEventName, out ev)) return;
            ev.Set();
            ev.Dispose();
            Mutex m;
            if (Mutex.TryOpenExisting(Program.MutexName, out m))
            {
                try { m.WaitOne(6000); m.ReleaseMutex(); }
                catch (AbandonedMutexException) { }
                catch { }
                m.Dispose();
            }
            Thread.Sleep(300);
        }

        public static void InstallAndLaunch(string from)
        {
            bool update = File.Exists(Exe);
            StopRunning();
            Directory.CreateDirectory(Dir);
            Exception last = null;
            for (int i = 0; i < 20; i++)
            {
                try { File.Copy(from, Exe, true); last = null; break; }
                catch (Exception ex) { last = ex; Thread.Sleep(250); }
            }
            if (last != null)
            {
                MessageBox.Show("Не получилось установить " + App.Name + ":\n" + last.Message, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { CreateShortcut(StartMenuLink, Exe); } catch (Exception ex) { Log.Write("ярлык: " + ex.Message); }
            if (!update) Autostart = true;
            else if (Autostart) Autostart = true;   // обновляем путь
            WriteUninstallEntry();
            Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = true, WorkingDirectory = Dir, Arguments = update ? "--updated" : "" });
        }

        static void CreateShortcut(string lnk, string target)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic s = shell.CreateShortcut(lnk);
            s.TargetPath = target;
            s.WorkingDirectory = Path.GetDirectoryName(target);
            s.IconLocation = target + ",0";
            s.Description = "Стеклянные виджеты для рабочего стола";
            s.Save();
        }

        // Запись в «Параметры → Приложения», чтобы Glasswing удалялся как обычная программа.
        static void WriteUninstallEntry()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", App.Name);
                    k.SetValue("DisplayVersion", App.Version);
                    k.SetValue("DisplayIcon", Exe + ",0");
                    k.SetValue("Publisher", App.Name);
                    k.SetValue("InstallLocation", Dir);
                    k.SetValue("UninstallString", "\"" + Exe + "\" --uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024) + 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { Log.Write("запись удаления: " + ex.Message); }
        }

        public static void Uninstall()
        {
            if (MessageBox.Show("Удалить " + App.Name + "? Виджеты закроются и уберутся из автозапуска.", App.Name,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            StopRunning();
            try { Autostart = false; } catch { }
            try { if (File.Exists(StartMenuLink)) File.Delete(StartMenuLink); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            MessageBox.Show(App.Name + " удалён. Настройки остались в папке " + Settings.Dir + ", их можно удалить вручную.", App.Name);
            // сам себя exe удалить не может: просим cmd сделать это через пару секунд после выхода
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + Dir + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetTempPath() });
            }
            catch { }
        }
    }

    // Переезд со старых скриптов: закрываем прежнюю версию виджета и забираем её настройки.
    static class Legacy
    {
        public static void Migrate()
        {
            if (!Settings.GetBool("app", "MigratedClock", false)) MigrateClock();
            if (!Settings.GetBool("app", "MigratedSysmon", false)) MigrateSysmon();
            if (!Settings.GetBool("app", "MigratedTr", false)) MigrateTranslator();
            if (!Settings.GetBool("app", "MigratedNp", false)) MigrateNp();
            if (!Settings.GetBool("app", "MigratedFox", false)) MigrateFox();
            if (!Settings.GetBool("app", "MigratedHub", false)) MigrateHub();
            if (!Settings.GetBool("app", "MigratedFolder", false)) MigrateFolder();
        }

        static void MigrateClock()
        {
            try { ImportClock(); } catch (Exception ex) { Log.Write("перенос часов: " + ex.Message); }
            try { StopScript(@"GlassClock\clock.ps1"); } catch (Exception ex) { Log.Write("старые часы: " + ex.Message); }
            DeleteLinks("Часы.lnk");
            Settings.Set("app", "MigratedClock", true);
        }

        static void MigrateSysmon()
        {
            try { ImportSysmon(); } catch (Exception ex) { Log.Write("перенос монитора: " + ex.Message); }
            try { StopScript(@"SysMonitor\sysmonitor.ps1"); } catch (Exception ex) { Log.Write("старый монитор: " + ex.Message); }
            DeleteLinks("Монитор системы.lnk");
            Settings.Set("app", "MigratedSysmon", true);
        }

        // Ярлыки прежнего скрипта в автозагрузке и в «Пуске».
        static void MigrateTranslator()
        {
            try
            {
                string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GlassTranslate", "settings.json");
                if (File.Exists(old) && !Settings.Has("tr", "My"))
                {
                    var j = Json.Parse(File.ReadAllText(old, System.Text.Encoding.UTF8).TrimStart('\uFEFF')) as System.Collections.Generic.Dictionary<string, object>;
                    if (j != null)
                    {
                        foreach (var key in new[] { "My", "Second", "DoubleCopy", "HotKey" })
                        {
                            object v;
                            if (j.TryGetValue(key, out v) && v != null) Settings.Set("tr", key, v, false);
                        }
                        Settings.Save();
                    }
                }
            }
            catch (Exception ex) { Log.Write("перенос переводчика: " + ex.Message); }
            try { StopScript(@"GlassTranslate\translate.ps1"); } catch (Exception ex) { Log.Write("старый переводчик: " + ex.Message); }
            DeleteLinks("Стеклянный переводчик.lnk");
            Settings.Set("app", "MigratedTr", true);
        }

        static void DeleteLinks(string name)
        {
            foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.Programs })
            {
                try
                {
                    string lnk = Path.Combine(Environment.GetFolderPath(folder), name);
                    if (File.Exists(lnk)) File.Delete(lnk);
                }
                catch { }
            }
        }

        static void ImportClock()
        {
            string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GlassClock", "settings.json");
            if (!File.Exists(old) || Settings.Has("clock", "X")) return;
            var j = Json.Parse(File.ReadAllText(old, System.Text.Encoding.UTF8).TrimStart('﻿')) as System.Collections.Generic.Dictionary<string, object>;
            if (j == null) return;
            foreach (var key in new[] { "X", "Y", "TopMost", "Locked", "Seconds", "KeepIcons" })
            {
                object v;
                if (j.TryGetValue(key, out v) && v != null) Settings.Set("clock", key, v, false);
            }
            object glass;
            if (!Settings.Has("app", "Glass") && j.TryGetValue("Glass", out glass) && glass is string && Array.IndexOf(Ui.GlassNames, (string)glass) >= 0)
                Settings.Set("app", "Glass", glass, false);
            Settings.Save();
        }

        static void ImportSysmon()
        {
            string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SysMonitor", "settings.json");
            if (!File.Exists(old) || Settings.Has("sysmon", "X")) return;
            var j = Json.Parse(File.ReadAllText(old, System.Text.Encoding.UTF8).TrimStart('\uFEFF')) as System.Collections.Generic.Dictionary<string, object>;
            if (j == null) return;
            foreach (var key in new[] { "X", "Y", "TopMost", "Locked" })
            {
                object v;
                if (j.TryGetValue(key, out v) && v != null) Settings.Set("sysmon", key, v, false);
            }
            object glass;
            if (!Settings.Has("app", "Glass") && j.TryGetValue("Glass", out glass) && glass is string && Array.IndexOf(Ui.GlassNames, (string)glass) >= 0)
                Settings.Set("app", "Glass", glass, false);
            Settings.Save();
        }

        static void MigrateNp()
        {
            try { ImportSettings(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NowPlaying", "nowplaying.json"),
                "np", new[] { "X", "Y", "TopMost", "Locked" }); }
            catch (Exception ex) { Log.Write("перенос «Сейчас играет»: " + ex.Message); }
            try { StopScript(@"NowPlaying\nowplaying.ps1"); } catch (Exception ex) { Log.Write("старое «Сейчас играет»: " + ex.Message); }
            DeleteLinks("Сейчас играет.lnk");
            Settings.Set("app", "MigratedNp", true);
        }

        // Кицунэ жил в %USERPROFILE%\Kitsune\pet.json. Сначала закрываем скрипт, чтобы забрать самую свежую память.
        static void MigrateFox()
        {
            try { StopScript(@"Kitsune\kitsune.ps1"); } catch (Exception ex) { Log.Write("старый питомец: " + ex.Message); }
            try { ImportFox(); } catch (Exception ex) { Log.Write("перенос питомца: " + ex.Message); }
            DeleteLinks("Кицунэ.lnk");
            Settings.Set("app", "MigratedFox", true);
        }

        static void ImportFox()
        {
            string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Kitsune", "pet.json");
            if (!File.Exists(old) || File.Exists(PetState.FilePath)) return;
            var j = Json.Parse(File.ReadAllText(old, System.Text.Encoding.UTF8).TrimStart('\uFEFF')) as System.Collections.Generic.Dictionary<string, object>;
            if (j == null) return;
            foreach (var key in new[] { "CX", "Y", "TopMost", "Locked", "Walk", "Breaks" })
            {
                object v;
                if (j.TryGetValue(key, out v) && v != null && !Settings.Has("fox", key)) Settings.Set("fox", key, v, false);
            }
            if (!Settings.Has("fox", "Char")) Settings.Set("fox", "Char", "kitsune", false);
            Settings.Save();
            PetState.Load(old).Save();
        }

        // Игровой хаб жил в %USERPROFILE%\GameHub: hub.json и папка «Мои игры» со своими ярлыками.
        static void MigrateHub()
        {
            try { StopScript(@"GameHub\gamehub.ps1"); } catch (Exception ex) { Log.Write("старый хаб: " + ex.Message); }
            try { ImportHub(); } catch (Exception ex) { Log.Write("перенос хаба: " + ex.Message); }
            DeleteLinks("Игровой хаб.lnk");
            Settings.Set("app", "MigratedHub", true);
        }

        static void ImportHub()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GameHub");
            string old = Path.Combine(root, "hub.json");
            if (File.Exists(old) && !Settings.Has("hub", "Hidden"))
            {
                ImportSettings(old, "hub", new[] { "X", "Y", "TopMost", "Locked", "ShelfW", "ShelfH" });
                var j = Json.Parse(File.ReadAllText(old, System.Text.Encoding.UTF8).TrimStart('\uFEFF')) as System.Collections.Generic.Dictionary<string, object>;
                if (j != null)
                {
                    // один скрытый элемент старый PowerShell мог записать строкой, а не списком
                    object h;
                    var hidden = new System.Collections.Generic.List<object>();
                    if (j.TryGetValue("Hidden", out h))
                    {
                        if (h is string) hidden.Add(h);
                        else if (h is System.Collections.Generic.List<object>)
                            foreach (var x in (System.Collections.Generic.List<object>)h) if (x is string) hidden.Add(x);
                    }
                    Settings.Set("hub", "Hidden", hidden, false);
                    object l;
                    var launched = new System.Collections.Generic.Dictionary<string, object>();
                    if (j.TryGetValue("Launched", out l) && l is System.Collections.Generic.Dictionary<string, object>)
                        foreach (var kv in (System.Collections.Generic.Dictionary<string, object>)l) if (kv.Value is string) launched[kv.Key] = kv.Value;
                    if (!Settings.Has("hub", "Launched")) Settings.Set("hub", "Launched", launched, false);
                    Settings.Save();
                }
            }
            // свои ярлыки копируем в папку Glasswing, старую папку не трогаем
            string mine = Path.Combine(root, "Мои игры");
            if (!Directory.Exists(mine)) return;
            Directory.CreateDirectory(HubModule.MyGamesDir);
            foreach (var f in Directory.GetFiles(mine))
            {
                string dest = Path.Combine(HubModule.MyGamesDir, Path.GetFileName(f));
                try { if (!File.Exists(dest)) File.Copy(f, dest); } catch (Exception ex) { Log.Write("ярлык игры: " + ex.Message); }
            }
        }

        // Виджет-папка жил в %USERPROFILE%\DesktopWidgets: <id>.json с настройками и папка <id> с перенесёнными
        // значками. Файлы пользователя никуда не двигаем: новые виджеты ссылаются на эти папки там, где они лежат.
        static void MigrateFolder()
        {
            try { StopScript(@"DesktopWidgets\widget.ps1"); } catch (Exception ex) { Log.Write("старая папка-виджет: " + ex.Message); }
            try { ImportFolders(); } catch (Exception ex) { Log.Write("перенос папок: " + ex.Message); return; }
            DeleteLinks("Виджет-папка.lnk");
            Settings.Set("app", "MigratedFolder", true);
        }

        static void ImportFolders()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DesktopWidgets");
            if (!Directory.Exists(root)) return;
            var list = FolderStore.Load();
            bool top = false, added = false;
            var ids = new System.Collections.Generic.List<string>();
            foreach (var f in Directory.GetFiles(root, "*.json")) ids.Add(Path.GetFileNameWithoutExtension(f));
            // папка без файла настроек тоже виджет: её содержимое не должно потеряться
            foreach (var d in Directory.GetDirectories(root))
            {
                string id = Path.GetFileName(d);
                if (!ids.Exists(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase))) ids.Add(id);
            }
            foreach (var id in ids)
            {
                string dir = Path.Combine(root, id);
                if (list.Exists(x => FolderFiles.Same(x.Dir, dir))) continue;
                var info = new FolderInfo { Id = "old-" + id, Title = "Мои ярлыки", Dir = dir };
                while (list.Exists(x => x.Id == info.Id)) info.Id += "x";
                string cfg = Path.Combine(root, id + ".json");
                if (File.Exists(cfg))
                {
                    System.Collections.Generic.Dictionary<string, object> j = null;
                    try { j = Json.Parse(File.ReadAllText(cfg, System.Text.Encoding.UTF8).TrimStart('\uFEFF')) as System.Collections.Generic.Dictionary<string, object>; }
                    catch (Exception ex) { Log.Write("папка " + id + ": " + ex.Message); }
                    if (j != null)
                    {
                        object v;
                        if (j.TryGetValue("Title", out v) && v is string && ((string)v).Trim().Length > 0) info.Title = ((string)v).Trim();
                        if (j.TryGetValue("X", out v) && v is double && j.ContainsKey("Y") && j["Y"] is double)
                        {
                            info.HasPos = true;
                            info.X = (int)Math.Round((double)v);
                            info.Y = (int)Math.Round((double)j["Y"]);
                        }
                        if (j.TryGetValue("ShelfW", out v) && v is double && j.ContainsKey("ShelfH") && j["ShelfH"] is double)
                        {
                            info.ShelfW = (int)Math.Round((double)v);
                            info.ShelfH = (int)Math.Round((double)j["ShelfH"]);
                        }
                        if (j.TryGetValue("TopMost", out v) && v is bool && (bool)v) top = true;
                        object glass;
                        if (!Settings.Has("app", "Glass") && j.TryGetValue("Glass", out glass) && glass is string && Array.IndexOf(Ui.GlassNames, (string)glass) >= 0)
                            Settings.Set("app", "Glass", glass, false);
                    }
                }
                else if (!Directory.Exists(dir)) continue;
                list.Add(info);
                added = true;
            }
            if (!added) return;
            FolderStore.Save(list);
            if (top && !Settings.Has("folder", "TopMost")) Settings.Set("folder", "TopMost", true, false);
            Settings.Set("folder", "Shown", true, false);
            Settings.Save();
        }

        static void ImportSettings(string old, string id, string[] keys)
        {
            if (!File.Exists(old) || Settings.Has(id, "X")) return;
            var j = Json.Parse(File.ReadAllText(old, System.Text.Encoding.UTF8).TrimStart('﻿')) as System.Collections.Generic.Dictionary<string, object>;
            if (j == null) return;
            foreach (var key in keys)
            {
                object v;
                if (j.TryGetValue(key, out v) && v != null) Settings.Set(id, key, v, false);
            }
            object glass;
            if (!Settings.Has("app", "Glass") && j.TryGetValue("Glass", out glass) && glass is string && Array.IndexOf(Ui.GlassNames, (string)glass) >= 0)
                Settings.Set("app", "Glass", glass, false);
            Settings.Save();
        }

        static void StopScript(string marker)
        {
            using (var q = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'powershell.exe'"))
            {
                foreach (ManagementObject p in q.Get())
                {
                    string cmd = p["CommandLine"] as string;
                    if (cmd == null || cmd.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try { var pr = Process.GetProcessById(Convert.ToInt32(p["ProcessId"])); pr.Kill(); pr.WaitForExit(2000); } catch { }
                }
            }
        }
    }
}
