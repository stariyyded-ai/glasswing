using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Glasswing
{
    // Обновления через GitHub Releases: раз в день смотрим последний релиз, спрашиваем,
    // скачиваем Glasswing.exe, сверяем SHA-256 и запускаем его: он сам заменит установленную копию.
    static class Updater
    {
        public const string Repo = "stariyyded-ai/glasswing";
        const string ExeAsset = "Glasswing.exe", HashAsset = "Glasswing.exe.sha256";

        static readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 60 * 60 * 1000 };
        static Control ui;
        static bool busy, asked;

        public static string Status = "";

        public static bool Auto
        {
            get { return Settings.GetBool("app", "AutoUpdate", true); }
            set { Settings.Set("app", "AutoUpdate", value); }
        }

        public static void Start(Control invoker)
        {
            ui = invoker;
            timer.Tick += (s, e) => { if (Auto && DueForCheck()) Check(false); };
            timer.Start();
            // первая проверка через минуту после запуска, чтобы не мешать старту
            var first = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
            first.Tick += (s, e) => { first.Stop(); first.Dispose(); if (Auto && DueForCheck()) Check(false); };
            first.Start();
        }

        static bool DueForCheck()
        {
            DateTime last;
            return !DateTime.TryParse(Settings.GetString("app", "UpdateChecked", ""), null, System.Globalization.DateTimeStyles.RoundtripKind, out last)
                || (DateTime.UtcNow - last.ToUniversalTime()).TotalHours >= 20;
        }

        static Version Parse(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            int dash = v.IndexOfAny(new[] { '-', '+', ' ' });
            if (dash > 0) v = v.Substring(0, dash);
            Version r;
            if (!Version.TryParse(v.Contains('.') ? v : v + ".0", out r)) return null;
            return new Version(r.Major, r.Minor, Math.Max(0, r.Build), Math.Max(0, r.Revision));
        }

        public static Version Current { get { return Parse(App.Version); } }

        static string Get(string url, int timeout)
        {
            var rq = (HttpWebRequest)WebRequest.Create(url);
            rq.UserAgent = App.Name + "/" + App.Version;
            rq.Accept = "application/vnd.github+json";
            rq.Timeout = timeout; rq.ReadWriteTimeout = timeout;
            using (var rs = rq.GetResponse())
            using (var sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        static void Download(string url, string path)
        {
            var rq = (HttpWebRequest)WebRequest.Create(url);
            rq.UserAgent = App.Name + "/" + App.Version;
            rq.Timeout = 30000; rq.ReadWriteTimeout = 60000;
            using (var rs = rq.GetResponse())
            using (var src = rs.GetResponseStream())
            using (var dst = File.Create(path))
                src.CopyTo(dst);
        }

        // manual: пользователь сам нажал «Проверить обновления», значит, сообщаем и когда обновлений нет.
        public static void Check(bool manual)
        {
            if (busy) return;
            busy = true;
            Status = "проверяю…";
            new Thread(() =>
            {
                string tag = null, exeUrl = null, hashUrl = null, notes = null, err = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var j = Json.Parse(Get("https://api.github.com/repos/" + Repo + "/releases/latest", 15000)) as Dictionary<string, object>;
                    if (j != null)
                    {
                        tag = j.ContainsKey("tag_name") ? j["tag_name"] as string : null;
                        notes = j.ContainsKey("body") ? j["body"] as string : null;
                        var assets = j.ContainsKey("assets") ? j["assets"] as List<object> : null;
                        if (assets != null)
                            foreach (var a in assets.OfType<Dictionary<string, object>>())
                            {
                                string n = a.ContainsKey("name") ? a["name"] as string : null;
                                string u = a.ContainsKey("browser_download_url") ? a["browser_download_url"] as string : null;
                                if (n == ExeAsset) exeUrl = u;
                                else if (n == HashAsset) hashUrl = u;
                            }
                    }
                }
                catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound) { err = "релизов пока нет"; }
                catch (Exception ex) { err = "нет связи"; Log.Write("обновление: " + ex.Message); }
                Done(() => Found(manual, tag, exeUrl, hashUrl, notes, err));
            }) { IsBackground = true }.Start();
        }

        static void Done(Action a)
        {
            try { ui.BeginInvoke(a); } catch { busy = false; }
        }

        static void Found(bool manual, string tag, string exeUrl, string hashUrl, string notes, string err)
        {
            busy = false;
            if (err == null) Settings.Set("app", "UpdateChecked", DateTime.UtcNow.ToString("o"));
            var v = Parse(tag);
            if (err != null) { Status = err; if (manual) MessageBox.Show("Не получилось проверить обновления: " + err + ".", App.Name); return; }
            if (v == null || exeUrl == null || hashUrl == null || v <= Current)
            {
                Status = "у вас последняя";
                if (manual) MessageBox.Show("У вас последняя версия " + App.Name + " " + App.Version + ".", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Status = "доступна " + tag.TrimStart('v', 'V');
            if (!manual && asked) return;          // не спрашиваем по кругу в одном запуске
            asked = true;
            string what = string.IsNullOrWhiteSpace(notes) ? "" : "\n\nЧто нового:\n" + Short(notes);
            if (MessageBox.Show("Вышла новая версия " + App.Name + " " + tag.TrimStart('v', 'V') + " (у вас " + App.Version + ")." + what + "\n\nОбновить сейчас? Настройки сохранятся.",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            Install(v, exeUrl, hashUrl);
        }

        static string Short(string s)
        {
            s = s.Replace("\r", "").Trim();
            return s.Length > 600 ? s.Substring(0, 600) + "…" : s;
        }

        static void Install(Version v, string exeUrl, string hashUrl)
        {
            busy = true;
            Status = "скачиваю…";
            App.Core.Notify(App.Name, "Скачиваю обновление…");
            new Thread(() =>
            {
                string path = null, err = null;
                try
                {
                    string dir = Path.Combine(Path.GetTempPath(), App.Name + "-update");
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, ExeAsset);
                    if (File.Exists(path)) File.Delete(path);
                    string want = Get(hashUrl, 15000).Trim().Split(' ', '\t', '\n')[0].ToLowerInvariant();
                    Download(exeUrl, path);
                    string got;
                    using (var sha = SHA256.Create())
                    using (var f = File.OpenRead(path))
                        got = string.Concat(sha.ComputeHash(f).Select(b => b.ToString("x2")));
                    if (want.Length != 64 || got != want) err = "файл повреждён при скачивании";
                    else
                    {
                        var an = AssemblyName.GetAssemblyName(path);
                        if (an.Name != App.Name || an.Version < v) err = "скачан не тот файл";
                    }
                }
                catch (Exception ex) { err = "не получилось скачать"; Log.Write("обновление, скачивание: " + ex.Message); }
                Done(() =>
                {
                    busy = false;
                    if (err != null)
                    {
                        Status = err;
                        try { if (path != null && File.Exists(path)) File.Delete(path); } catch { }
                        MessageBox.Show("Обновление не установлено: " + err + ". Попробуйте позже.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    // новая версия закроет эту и встанет на её место (см. Installer.InstallAndLaunch)
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
                });
            }) { IsBackground = true }.Start();
        }
    }
}
