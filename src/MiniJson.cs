using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PocketRoguesEditor
{
    internal enum JsonKind { Object, Array, String, Number, True, False, Null }

    /// <summary>
    /// Значение JSON вместе с местом в исходном тексте (Start — первый символ, End — за последним).
    /// Места нужны, чтобы менять в строке сохранения только правленые кусочки, а всё остальное
    /// оставить байт в байт как записала игра: дробные координаты, порядок полей, незнакомые поля.
    /// </summary>
    internal sealed class JsonNode
    {
        public JsonKind Kind;
        public int Start;
        public int End;
        public string Text;                                      // для строк — уже раскодированный текст
        public List<KeyValuePair<string, JsonNode>> Members;     // для объектов, в исходном порядке
        public List<JsonNode> Items;                             // для массивов

        public JsonNode Get(string key)
        {
            if (Members == null) return null;
            foreach (KeyValuePair<string, JsonNode> m in Members) if (m.Key == key) return m.Value;
            return null;
        }

        public string GetString(string key)
        {
            JsonNode n = Get(key);
            return n != null && n.Kind == JsonKind.String ? n.Text : null;
        }

        public int GetInt(string key, int fallback)
        {
            JsonNode n = Get(key);
            if (n == null || n.Kind != JsonKind.Number) return fallback;
            int v;
            return int.TryParse(n.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public List<string> GetStrings(string key)
        {
            List<string> list = new List<string>();
            JsonNode n = Get(key);
            if (n == null || n.Kind != JsonKind.Array) return list;
            foreach (JsonNode i in n.Items) if (i.Kind == JsonKind.String) list.Add(i.Text);
            return list;
        }
    }

    /// <summary>
    /// Небольшой разборщик JSON ровно под сохранения Unity (JsonUtility): строгий, без
    /// комментариев и прочих вольностей. На битом тексте бросает FormatException — редактор
    /// такую запись не трогает.
    /// </summary>
    internal static class MiniJson
    {
        public static JsonNode Parse(string text)
        {
            int p = 0;
            JsonNode n = ParseValue(text, ref p);
            SkipWs(text, ref p);
            if (p != text.Length) throw new FormatException(L.T("лишний текст после JSON, позиция ", "extra text after JSON, position ") + p);
            return n;
        }

        private static void SkipWs(string s, ref int p)
        {
            while (p < s.Length && (s[p] == ' ' || s[p] == '\t' || s[p] == '\r' || s[p] == '\n')) p++;
        }

        private static JsonNode ParseValue(string s, ref int p)
        {
            SkipWs(s, ref p);
            if (p >= s.Length) throw new FormatException(L.T("JSON оборвался", "JSON ends too early"));
            char c = s[p];
            if (c == '{') return ParseObject(s, ref p);
            if (c == '[') return ParseArray(s, ref p);
            if (c == '"')
            {
                JsonNode n = new JsonNode();
                n.Kind = JsonKind.String;
                n.Start = p;
                n.Text = ParseString(s, ref p);
                n.End = p;
                return n;
            }
            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref p);
            if (Match(s, p, "true")) return Literal(JsonKind.True, ref p, 4);
            if (Match(s, p, "false")) return Literal(JsonKind.False, ref p, 5);
            if (Match(s, p, "null")) return Literal(JsonKind.Null, ref p, 4);
            throw new FormatException(L.T("непонятный символ «" + c + "» в JSON, позиция ", "unexpected character “" + c + "” in JSON, position ") + p);
        }

        private static bool Match(string s, int p, string word)
        {
            return string.CompareOrdinal(s, p, word, 0, word.Length) == 0;
        }

        private static JsonNode Literal(JsonKind kind, ref int p, int length)
        {
            JsonNode n = new JsonNode();
            n.Kind = kind;
            n.Start = p;
            p += length;
            n.End = p;
            return n;
        }

        private static JsonNode ParseNumber(string s, ref int p)
        {
            JsonNode n = new JsonNode();
            n.Kind = JsonKind.Number;
            n.Start = p;
            if (s[p] == '-') p++;
            while (p < s.Length && "0123456789.eE+-".IndexOf(s[p]) >= 0) p++;
            n.End = p;
            n.Text = s.Substring(n.Start, n.End - n.Start);
            return n;
        }

        private static JsonNode ParseObject(string s, ref int p)
        {
            JsonNode n = new JsonNode();
            n.Kind = JsonKind.Object;
            n.Start = p;
            n.Members = new List<KeyValuePair<string, JsonNode>>();
            p++;
            SkipWs(s, ref p);
            if (p < s.Length && s[p] == '}') { p++; n.End = p; return n; }
            while (true)
            {
                SkipWs(s, ref p);
                if (p >= s.Length || s[p] != '"') throw new FormatException(L.T("ожидалось имя поля, позиция ", "a field name was expected, position ") + p);
                string key = ParseString(s, ref p);
                SkipWs(s, ref p);
                if (p >= s.Length || s[p] != ':') throw new FormatException(L.T("ожидалось «:», позиция ", "“:” was expected, position ") + p);
                p++;
                n.Members.Add(new KeyValuePair<string, JsonNode>(key, ParseValue(s, ref p)));
                SkipWs(s, ref p);
                if (p >= s.Length) throw new FormatException(L.T("объект JSON оборвался", "a JSON object ends too early"));
                if (s[p] == ',') { p++; continue; }
                if (s[p] == '}') { p++; n.End = p; return n; }
                throw new FormatException(L.T("ожидалось «,» или «}», позиция ", "“,” or “}” was expected, position ") + p);
            }
        }

        private static JsonNode ParseArray(string s, ref int p)
        {
            JsonNode n = new JsonNode();
            n.Kind = JsonKind.Array;
            n.Start = p;
            n.Items = new List<JsonNode>();
            p++;
            SkipWs(s, ref p);
            if (p < s.Length && s[p] == ']') { p++; n.End = p; return n; }
            while (true)
            {
                n.Items.Add(ParseValue(s, ref p));
                SkipWs(s, ref p);
                if (p >= s.Length) throw new FormatException(L.T("массив JSON оборвался", "a JSON array ends too early"));
                if (s[p] == ',') { p++; continue; }
                if (s[p] == ']') { p++; n.End = p; return n; }
                throw new FormatException(L.T("ожидалось «,» или «]», позиция ", "“,” or “]” was expected, position ") + p);
            }
        }

        private static string ParseString(string s, ref int p)
        {
            StringBuilder sb = new StringBuilder();
            p++;   // открывающая кавычка
            while (true)
            {
                if (p >= s.Length) throw new FormatException(L.T("строка JSON оборвалась", "a JSON string ends too early"));
                char c = s[p++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (p >= s.Length) throw new FormatException(L.T("строка JSON оборвалась", "a JSON string ends too early"));
                char e = s[p++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (p + 4 > s.Length) throw new FormatException(L.T("оборванный \\u в строке JSON", "a cut-off \\u in a JSON string"));
                        sb.Append((char)int.Parse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        p += 4;
                        break;
                    default: throw new FormatException(L.T("неизвестная экранировка \\", "unknown escape \\") + e);
                }
            }
        }

        // --- запись ---------------------------------------------------------

        /// <summary>Строка в JSON так, как её пишет JsonUtility: кавычки, обратная косая и управляющие — экранируются.</summary>
        public static string Quote(string value)
        {
            StringBuilder sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string StringArray(List<string> values)
        {
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Quote(values[i]));
            }
            return sb.Append(']').ToString();
        }

        /// <summary>
        /// Заменить куски исходного текста. Куски не должны пересекаться; порядок в списке любой.
        /// Всё, что между ними, остаётся нетронутым.
        /// </summary>
        public static string Splice(string source, List<Replacement> parts)
        {
            List<Replacement> sorted = new List<Replacement>(parts);
            sorted.Sort(delegate(Replacement a, Replacement b) { return a.Start.CompareTo(b.Start); });
            StringBuilder sb = new StringBuilder(source.Length + 64);
            int at = 0;
            foreach (Replacement r in sorted)
            {
                if (r.Start < at || r.End < r.Start || r.End > source.Length)
                    throw new InvalidOperationException(L.T("замены в тексте пересекаются", "replacements in the text overlap"));
                sb.Append(source, at, r.Start - at).Append(r.Text);
                at = r.End;
            }
            sb.Append(source, at, source.Length - at);
            return sb.ToString();
        }

        /// <summary>
        /// Кусок исходного текста от start до end с заменами внутри него — например, одна вещь со
        /// своими правками, чтобы переложить её в другую запись. Замены вне куска — ошибка.
        /// </summary>
        public static string SpliceRange(string source, int start, int end, List<Replacement> parts)
        {
            List<Replacement> inner = new List<Replacement>();
            foreach (Replacement r in parts)
            {
                if (r.Start < start || r.End > end)
                    throw new InvalidOperationException(L.T("замена вне своего куска текста", "a replacement lies outside its piece of text"));
                inner.Add(new Replacement(r.Start - start, r.End - start, r.Text));
            }
            return Splice(source.Substring(start, end - start), inner);
        }
    }

    internal sealed class Replacement
    {
        public int Start;
        public int End;
        public string Text;

        public Replacement(JsonNode node, string text)
        {
            Start = node.Start;
            End = node.End;
            Text = text;
        }

        public Replacement(int start, int end, string text)
        {
            Start = start;
            End = end;
            Text = text;
        }
    }
}
