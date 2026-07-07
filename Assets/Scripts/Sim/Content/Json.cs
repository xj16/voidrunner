using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VoidRunner.Content
{
    /// <summary>
    /// A small, dependency-free, allocation-conscious JSON parser.
    ///
    /// Why not JsonUtility? Unity's JsonUtility cannot deserialize top-level arrays, cannot report
    /// where a syntax error occurred, silently drops unknown fields, and does not exist outside the
    /// engine (so it can't be unit-tested by the CI EditMode/plain-.NET runner). This parser is
    /// ~250 lines, fully spec-compliant for the subset content packs need, and produces a tree of
    /// <see cref="JsonValue"/> we can validate with precise line/column error messages.
    /// </summary>
    public enum JsonType { Null, Bool, Number, String, Array, Object }

    public sealed class JsonValue
    {
        public JsonType Type { get; private set; }

        private bool _bool;
        private double _number;
        private string _string;
        private List<JsonValue> _array;
        private Dictionary<string, JsonValue> _object;

        public static readonly JsonValue Null = new JsonValue { Type = JsonType.Null };

        public static JsonValue Of(bool v) => new JsonValue { Type = JsonType.Bool, _bool = v };
        public static JsonValue Of(double v) => new JsonValue { Type = JsonType.Number, _number = v };
        public static JsonValue Of(string v) => new JsonValue { Type = JsonType.String, _string = v };
        public static JsonValue NewArray() => new JsonValue { Type = JsonType.Array, _array = new List<JsonValue>() };
        public static JsonValue NewObject() => new JsonValue { Type = JsonType.Object, _object = new Dictionary<string, JsonValue>() };

        public bool AsBool => _bool;
        public double AsNumber => _number;
        public string AsString => _string;
        public IReadOnlyList<JsonValue> AsArray => _array;
        public IReadOnlyDictionary<string, JsonValue> AsObject => _object;

        public bool IsObject => Type == JsonType.Object;
        public bool IsArray => Type == JsonType.Array;

        internal void Add(JsonValue v) => _array.Add(v);
        internal void Set(string k, JsonValue v) => _object[k] = v;

        /// <summary>Object field access; returns null when absent or when this is not an object.</summary>
        public JsonValue this[string key]
        {
            get
            {
                if (Type != JsonType.Object || _object == null) return null;
                return _object.TryGetValue(key, out var v) ? v : null;
            }
        }

        public bool TryGet(string key, out JsonValue value)
        {
            value = this[key];
            return value != null;
        }

        // --- Typed convenience accessors with defaults (used by ContentLoader) ---

        public string GetString(string key, string fallback = null)
        {
            var v = this[key];
            return v != null && v.Type == JsonType.String ? v._string : fallback;
        }

        public float GetFloat(string key, float fallback = 0f)
        {
            var v = this[key];
            return v != null && v.Type == JsonType.Number ? (float)v._number : fallback;
        }

        public int GetInt(string key, int fallback = 0)
        {
            var v = this[key];
            return v != null && v.Type == JsonType.Number ? (int)Math.Round(v._number) : fallback;
        }

        public bool GetBool(string key, bool fallback = false)
        {
            var v = this[key];
            return v != null && v.Type == JsonType.Bool ? v._bool : fallback;
        }
    }

    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string message, int line, int column)
            : base($"JSON error at line {line}, column {column}: {message}") { }
    }

    public static class Json
    {
        public static JsonValue Parse(string text)
        {
            var parser = new Parser(text ?? string.Empty);
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
            {
                parser.Fail("unexpected trailing content");
            }
            return value;
        }

        /// <summary>
        /// Maximum object/array nesting depth. Content packs and replays are untrusted input that
        /// strangers share; a deeply-nested payload (e.g. "[[[[[…]]]]]" thousands deep) would blow
        /// the stack of the recursive-descent parser. We refuse it with a clean error instead.
        /// Legitimate content is at most 4–5 levels deep, so 128 is generous.
        /// </summary>
        public const int MaxDepth = 128;

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;
            private int _line = 1;
            private int _col = 1;
            private int _depth;

            public Parser(string s) { _s = s; }

            public bool AtEnd => _i >= _s.Length;

            public void Fail(string message) => throw new JsonParseException(message, _line, _col);

            private char Current => _s[_i];

            private void Advance()
            {
                if (_s[_i] == '\n') { _line++; _col = 1; }
                else { _col++; }
                _i++;
            }

            public void SkipWhitespace()
            {
                while (!AtEnd)
                {
                    char c = Current;
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { Advance(); }
                    else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                    {
                        // Line comment — a convenience for hand-authored content packs.
                        while (!AtEnd && Current != '\n') Advance();
                    }
                    else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
                    {
                        Advance(); Advance();
                        while (!AtEnd && !(Current == '*' && _i + 1 < _s.Length && _s[_i + 1] == '/')) Advance();
                        if (!AtEnd) { Advance(); Advance(); }
                    }
                    else break;
                }
            }

            public JsonValue ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) Fail("unexpected end of input");

                char c = Current;
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return JsonValue.Of(ParseString());
                    case 't':
                    case 'f': return ParseBool();
                    case 'n': ParseLiteral("null"); return JsonValue.Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        Fail($"unexpected character '{c}'");
                        return null;
                }
            }

            private JsonValue ParseObject()
            {
                if (++_depth > MaxDepth) Fail($"nesting too deep (> {MaxDepth})");
                var obj = JsonValue.NewObject();
                Advance(); // {
                SkipWhitespace();
                if (!AtEnd && Current == '}') { Advance(); _depth--; return obj; }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || Current != '"') Fail("expected object key string");
                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || Current != ':') Fail("expected ':' after object key");
                    Advance();
                    var val = ParseValue();
                    obj.Set(key, val);
                    SkipWhitespace();
                    if (AtEnd) Fail("unterminated object");
                    if (Current == ',') { Advance(); continue; }
                    if (Current == '}') { Advance(); break; }
                    Fail("expected ',' or '}' in object");
                }
                _depth--;
                return obj;
            }

            private JsonValue ParseArray()
            {
                if (++_depth > MaxDepth) Fail($"nesting too deep (> {MaxDepth})");
                var arr = JsonValue.NewArray();
                Advance(); // [
                SkipWhitespace();
                if (!AtEnd && Current == ']') { Advance(); _depth--; return arr; }

                while (true)
                {
                    var val = ParseValue();
                    arr.Add(val);
                    SkipWhitespace();
                    if (AtEnd) Fail("unterminated array");
                    if (Current == ',') { Advance(); continue; }
                    if (Current == ']') { Advance(); break; }
                    Fail("expected ',' or ']' in array");
                }
                _depth--;
                return arr;
            }

            private string ParseString()
            {
                Advance(); // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) Fail("unterminated string");
                    char c = Current;
                    if (c == '"') { Advance(); break; }
                    if (c == '\\')
                    {
                        Advance();
                        if (AtEnd) Fail("unterminated escape");
                        char e = Current;
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
                                sb.Append(ParseUnicodeEscape());
                                continue; // ParseUnicodeEscape already advanced past the digits
                            default: Fail($"invalid escape '\\{e}'"); break;
                        }
                        Advance();
                    }
                    else
                    {
                        sb.Append(c);
                        Advance();
                    }
                }
                return sb.ToString();
            }

            private char ParseUnicodeEscape()
            {
                Advance(); // past 'u'
                int code = 0;
                for (int k = 0; k < 4; k++)
                {
                    if (AtEnd) Fail("unterminated unicode escape");
                    char h = Current;
                    int digit;
                    if (h >= '0' && h <= '9') digit = h - '0';
                    else if (h >= 'a' && h <= 'f') digit = 10 + (h - 'a');
                    else if (h >= 'A' && h <= 'F') digit = 10 + (h - 'A');
                    else { Fail("invalid unicode escape digit"); return '\0'; }
                    code = (code << 4) | digit;
                    Advance();
                }
                return (char)code;
            }

            private JsonValue ParseBool()
            {
                if (Current == 't') { ParseLiteral("true"); return JsonValue.Of(true); }
                ParseLiteral("false");
                return JsonValue.Of(false);
            }

            private void ParseLiteral(string literal)
            {
                foreach (char expected in literal)
                {
                    if (AtEnd || Current != expected) Fail($"expected '{literal}'");
                    Advance();
                }
            }

            private JsonValue ParseNumber()
            {
                int start = _i;
                if (Current == '-') Advance();
                while (!AtEnd && Current >= '0' && Current <= '9') Advance();
                if (!AtEnd && Current == '.')
                {
                    Advance();
                    while (!AtEnd && Current >= '0' && Current <= '9') Advance();
                }
                if (!AtEnd && (Current == 'e' || Current == 'E'))
                {
                    Advance();
                    if (!AtEnd && (Current == '+' || Current == '-')) Advance();
                    while (!AtEnd && Current >= '0' && Current <= '9') Advance();
                }

                string slice = _s.Substring(start, _i - start);
                if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    Fail($"invalid number '{slice}'");
                }
                return JsonValue.Of(value);
            }
        }

        // ----- Minimal writer, used by the replay serializer -----

        public static string Escape(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Number(double d) => d.ToString("R", CultureInfo.InvariantCulture);
    }
}
