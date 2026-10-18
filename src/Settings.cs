using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Glasswing
{
    // Маленький JSON: объекты -> Dictionary<string, object>, массивы -> List<object>, числа -> double.
    static class Json
    {
        public static object Parse(string s) { int i = 0; return Val(s, ref i); }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Val(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '[')
            {
                i++;
                var list = new List<object>();
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length) break;
                    if (s[i] == ']') { i++; break; }
                    if (s[i] == ',') { list.Add(null); i++; continue; }
                    list.Add(Val(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') i++;
                }
                return list;
            }
            if (c == '{')
            {
                i++;
                var obj = new Dictionary<string, object>();
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length) break;
                    if (s[i] == '}') { i++; break; }
                    if (s[i] == ',') { i++; continue; }
                    string key = Str(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ':') i++;
                    obj[key] = Val(s, ref i);
                }
                return obj;
            }
            if (c == '"') return Str(s, ref i);
            int st = i;
            while (i < s.Length && ",]} \t\r\n".IndexOf(s[i]) < 0) i++;
            string word = s.Substring(st, i - st);
            if (word == "null") return null;
            if (word == "true") return true;
            if (word == "false") return false;
            double d;
            double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            return d;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\' || i >= s.Length) { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 <= s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            Write(sb, v, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is string) { Quote(sb, (string)v); return; }
            if (v is int || v is long || v is double || v is float)
            {
                sb.Append(Convert.ToDouble(v).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as Dictionary<string, object>;
            if (dict != null)
            {
                sb.Append("{");
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("\n").Append(' ', (depth + 1) * 2);
                    Quote(sb, kv.Key);
                    sb.Append(": ");
                    Write(sb, kv.Value, depth + 1);
                }
                if (!first) sb.Append("\n").Append(' ', depth * 2);
                sb.Append("}");
                return;
            }
            var list = v as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append("[");
                bool first = true;
                foreach (var x in list)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    Write(sb, x, depth + 1);
                }
                sb.Append("]");
                return;
            }
            Quote(sb, v.ToString());
        }

        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    // Настройки всех виджетов в одном файле: %APPDATA%\Glasswing\settings.json, по разделу на виджет.
    static class Settings
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App.Name);
        static readonly string FilePath = Path.Combine(Dir, "settings.json");
        static Dictionary<string, object> root = new Dictionary<string, object>();

        public static bool Existed { get; private set; }

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath))
                {
                    Existed = true;
                    root = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object> ?? new Dictionary<string, object>();
                }
            }
            catch (Exception ex) { Log.Write("настройки: " + ex.Message); }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json.Write(root), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("сохранение: " + ex.Message); }
        }

        public static Dictionary<string, object> Section(string id)
        {
            object o;
            if (root.TryGetValue(id, out o) && o is Dictionary<string, object>) return (Dictionary<string, object>)o;
            var d = new Dictionary<string, object>();
            root[id] = d;
            return d;
        }

        public static bool Has(string id, string key) { return Section(id).ContainsKey(key) && Section(id)[key] != null; }

        public static bool GetBool(string id, string key, bool def)
        {
            object o;
            return Section(id).TryGetValue(key, out o) && o is bool ? (bool)o : def;
        }

        public static int GetInt(string id, string key, int def)
        {
            object o;
            return Section(id).TryGetValue(key, out o) && o is double ? (int)Math.Round((double)o) : def;
        }

        public static string GetString(string id, string key, string def)
        {
            object o;
            return Section(id).TryGetValue(key, out o) && o is string ? (string)o : def;
        }

        public static void Set(string id, string key, object value, bool save = true)
        {
            if (value is int) value = (double)(int)value;
            Section(id)[key] = value;
            if (save) Save();
        }
    }

    static class Log
    {
        public static void Write(string text)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.AppendAllText(Path.Combine(Settings.Dir, "error.log"), DateTime.Now + "  " + text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
