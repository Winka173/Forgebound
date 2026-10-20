using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Forgebound.Core.Content
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Minimal dependency-free JSON DOM used by content, saves and imports. Works identically in Unity
    /// (no Newtonsoft dependency) and in the headless .NET tools. Object key order is preserved.
    /// </summary>
    public sealed class JsonValue
    {
        public JsonKind Kind;
        public bool BoolValue;
        public double NumberValue;
        public string StringValue;
        public List<JsonValue> Items;
        public List<KeyValuePair<string, JsonValue>> Fields;

        public static readonly JsonValue Null = new JsonValue { Kind = JsonKind.Null };

        public static JsonValue Obj() { return new JsonValue { Kind = JsonKind.Object, Fields = new List<KeyValuePair<string, JsonValue>>() }; }
        public static JsonValue Arr() { return new JsonValue { Kind = JsonKind.Array, Items = new List<JsonValue>() }; }
        public static JsonValue Str(string s) { return s == null ? Null : new JsonValue { Kind = JsonKind.String, StringValue = s }; }
        public static JsonValue Num(double d) { return new JsonValue { Kind = JsonKind.Number, NumberValue = d }; }
        public static JsonValue Bool(bool b) { return new JsonValue { Kind = JsonKind.Bool, BoolValue = b }; }

        public bool IsNull { get { return Kind == JsonKind.Null; } }
        public int Count { get { return Kind == JsonKind.Array ? Items.Count : (Kind == JsonKind.Object ? Fields.Count : 0); } }

        public JsonValue this[string key]
        {
            get
            {
                if (Kind != JsonKind.Object) return Null;
                for (int i = 0; i < Fields.Count; i++) if (Fields[i].Key == key) return Fields[i].Value;
                return Null;
            }
        }

        public JsonValue this[int index]
        {
            get { return Kind == JsonKind.Array && index >= 0 && index < Items.Count ? Items[index] : Null; }
        }

        public bool Has(string key)
        {
            if (Kind != JsonKind.Object) return false;
            for (int i = 0; i < Fields.Count; i++) if (Fields[i].Key == key) return true;
            return false;
        }

        public JsonValue Set(string key, JsonValue v)
        {
            if (Kind != JsonKind.Object) throw new InvalidOperationException("Not an object");
            v = v ?? Null;
            for (int i = 0; i < Fields.Count; i++)
            {
                if (Fields[i].Key == key) { Fields[i] = new KeyValuePair<string, JsonValue>(key, v); return this; }
            }
            Fields.Add(new KeyValuePair<string, JsonValue>(key, v));
            return this;
        }

        public JsonValue Set(string key, string v) { return Set(key, Str(v)); }
        public JsonValue Set(string key, int v) { return Set(key, Num(v)); }
        public JsonValue Set(string key, long v) { return Set(key, Num(v)); }
        public JsonValue Set(string key, bool v) { return Set(key, Bool(v)); }
        public JsonValue Add(JsonValue v) { Items.Add(v ?? Null); return this; }

        public string AsString(string def = null) { return Kind == JsonKind.String ? StringValue : def; }
        public bool AsBool(bool def = false) { return Kind == JsonKind.Bool ? BoolValue : def; }
        public double AsDouble(double def = 0) { return Kind == JsonKind.Number ? NumberValue : def; }

        public int AsInt(int def = 0)
        {
            if (Kind != JsonKind.Number) return def;
            double d = NumberValue;
            if (d > int.MaxValue) return int.MaxValue;
            if (d < int.MinValue) return int.MinValue;
            return (int)Math.Round(d, MidpointRounding.AwayFromZero);
        }

        public long AsLong(long def = 0)
        {
            if (Kind != JsonKind.Number) return def;
            return (long)Math.Round(NumberValue, MidpointRounding.AwayFromZero);
        }

        public string Str(string key, string def = null) { return this[key].AsString(def); }
        public int Int(string key, int def = 0) { return this[key].AsInt(def); }
        public bool BoolOf(string key, bool def = false) { return this[key].AsBool(def); }
        public double Dbl(string key, double def = 0) { return this[key].AsDouble(def); }

        /// <summary>Seconds field converted to simulation ticks (rounded).</summary>
        public int Ticks(string key, int defTicks = 0)
        {
            var v = this[key];
            if (v.Kind != JsonKind.Number) return defTicks;
            return (int)Math.Round(v.NumberValue * 30.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Fractional multiplier (e.g. 1.25) converted to permille (1250).</summary>
        public int Permille(string key, int def = 1000)
        {
            var v = this[key];
            if (v.Kind != JsonKind.Number) return def;
            return (int)Math.Round(v.NumberValue * 1000.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>World units field converted to milli-units.</summary>
        public int Milli(string key, int def = 0)
        {
            var v = this[key];
            if (v.Kind != JsonKind.Number) return def;
            return (int)Math.Round(v.NumberValue * 1000.0, MidpointRounding.AwayFromZero);
        }

        public List<string> StrList(string key)
        {
            var list = new List<string>();
            var v = this[key];
            if (v.Kind == JsonKind.Array)
                for (int i = 0; i < v.Items.Count; i++) if (v.Items[i].Kind == JsonKind.String) list.Add(v.Items[i].StringValue);
            return list;
        }

        public IEnumerable<JsonValue> Elements()
        {
            if (Kind == JsonKind.Array) for (int i = 0; i < Items.Count; i++) yield return Items[i];
        }

        public override string ToString() { return JsonWriter.Write(this, false); }
    }

    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string msg) : base(msg) { }
    }

    public static class JsonParser
    {
        public const int MaxDepth = 64;
        public const int MaxLength = 8 * 1024 * 1024;

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new JsonParseException("null input");
            if (text.Length > MaxLength) throw new JsonParseException("input too large");
            int pos = 0;
            SkipWs(text, ref pos);
            var v = ParseValue(text, ref pos, 0);
            SkipWs(text, ref pos);
            if (pos != text.Length) throw new JsonParseException("Trailing characters at " + pos);
            return v;
        }

        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            try { value = Parse(text); error = null; return true; }
            catch (JsonParseException e) { value = JsonValue.Null; error = e.Message; return false; }
        }

        private static void SkipWs(string s, ref int p)
        {
            while (p < s.Length)
            {
                char c = s[p];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { p++; continue; }
                // Allow // line comments in authored content files.
                if (c == '/' && p + 1 < s.Length && s[p + 1] == '/')
                {
                    while (p < s.Length && s[p] != '\n') p++;
                    continue;
                }
                break;
            }
        }

        private static JsonValue ParseValue(string s, ref int p, int depth)
        {
            if (depth > MaxDepth) throw new JsonParseException("Max depth exceeded");
            if (p >= s.Length) throw new JsonParseException("Unexpected end");
            char c = s[p];
            switch (c)
            {
                case '{': return ParseObject(s, ref p, depth);
                case '[': return ParseArray(s, ref p, depth);
                case '"': return JsonValue.Str(ParseString(s, ref p));
                case 't': Expect(s, ref p, "true"); return JsonValue.Bool(true);
                case 'f': Expect(s, ref p, "false"); return JsonValue.Bool(false);
                case 'n': Expect(s, ref p, "null"); return JsonValue.Null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref p);
                    throw new JsonParseException("Unexpected char '" + c + "' at " + p);
            }
        }

        private static void Expect(string s, ref int p, string word)
        {
            if (string.CompareOrdinal(s, p, word, 0, word.Length) != 0) throw new JsonParseException("Expected " + word + " at " + p);
            p += word.Length;
        }

        private static JsonValue ParseObject(string s, ref int p, int depth)
        {
            var obj = JsonValue.Obj();
            p++;
            SkipWs(s, ref p);
            if (p < s.Length && s[p] == '}') { p++; return obj; }
            while (true)
            {
                SkipWs(s, ref p);
                if (p >= s.Length || s[p] != '"') throw new JsonParseException("Expected key at " + p);
                string key = ParseString(s, ref p);
                SkipWs(s, ref p);
                if (p >= s.Length || s[p] != ':') throw new JsonParseException("Expected ':' at " + p);
                p++;
                SkipWs(s, ref p);
                var val = ParseValue(s, ref p, depth + 1);
                obj.Fields.Add(new KeyValuePair<string, JsonValue>(key, val));
                SkipWs(s, ref p);
                if (p >= s.Length) throw new JsonParseException("Unterminated object");
                if (s[p] == ',') { p++; SkipWs(s, ref p); if (p < s.Length && s[p] == '}') { p++; return obj; } continue; }
                if (s[p] == '}') { p++; return obj; }
                throw new JsonParseException("Expected ',' or '}' at " + p);
            }
        }

        private static JsonValue ParseArray(string s, ref int p, int depth)
        {
            var arr = JsonValue.Arr();
            p++;
            SkipWs(s, ref p);
            if (p < s.Length && s[p] == ']') { p++; return arr; }
            while (true)
            {
                SkipWs(s, ref p);
                arr.Items.Add(ParseValue(s, ref p, depth + 1));
                SkipWs(s, ref p);
                if (p >= s.Length) throw new JsonParseException("Unterminated array");
                if (s[p] == ',') { p++; SkipWs(s, ref p); if (p < s.Length && s[p] == ']') { p++; return arr; } continue; }
                if (s[p] == ']') { p++; return arr; }
                throw new JsonParseException("Expected ',' or ']' at " + p);
            }
        }

        private static string ParseString(string s, ref int p)
        {
            p++; // opening quote
            var sb = new StringBuilder();
            while (p < s.Length)
            {
                char c = s[p++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (p >= s.Length) break;
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
                            if (p + 4 > s.Length) throw new JsonParseException("Bad unicode escape");
                            sb.Append((char)int.Parse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            p += 4;
                            break;
                        default: throw new JsonParseException("Bad escape at " + p);
                    }
                }
                else sb.Append(c);
            }
            throw new JsonParseException("Unterminated string");
        }

        private static JsonValue ParseNumber(string s, ref int p)
        {
            int start = p;
            if (s[p] == '-') p++;
            while (p < s.Length && ((s[p] >= '0' && s[p] <= '9') || s[p] == '.' || s[p] == 'e' || s[p] == 'E' || s[p] == '+' || s[p] == '-')) p++;
            double d;
            if (!double.TryParse(s.Substring(start, p - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new JsonParseException("Bad number at " + start);
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new JsonParseException("Non-finite number at " + start);
            return JsonValue.Num(d);
        }
    }

    public static class JsonWriter
    {
        public static string Write(JsonValue v, bool pretty)
        {
            var sb = new StringBuilder();
            WriteValue(sb, v, pretty, 0);
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int n) { sb.Append('\n'); sb.Append(' ', n * 2); }

        private static void WriteValue(StringBuilder sb, JsonValue v, bool pretty, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            switch (v.Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(v.BoolValue ? "true" : "false"); break;
                case JsonKind.Number:
                    double d = v.NumberValue;
                    if (d == Math.Floor(d) && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                    else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case JsonKind.String: WriteString(sb, v.StringValue); break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < v.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) Indent(sb, depth + 1);
                        WriteValue(sb, v.Items[i], pretty, depth + 1);
                    }
                    if (pretty && v.Items.Count > 0) Indent(sb, depth);
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    sb.Append('{');
                    for (int i = 0; i < v.Fields.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) Indent(sb, depth + 1);
                        WriteString(sb, v.Fields[i].Key);
                        sb.Append(pretty ? ": " : ":");
                        WriteValue(sb, v.Fields[i].Value, pretty, depth + 1);
                    }
                    if (pretty && v.Fields.Count > 0) Indent(sb, depth);
                    sb.Append('}');
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
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
}
