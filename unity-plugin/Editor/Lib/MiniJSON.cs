using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UnityAI.Lib
{
    /// <summary>
    /// Küçük, bağımsız JSON parser/serializer (dinamik object/Dictionary/List üretir).
    /// Unity'de dinamik JSON için pratik. (MIT-tarzı, kamuya açık MiniJSON türevi.)
    /// </summary>
    public static class Json
    {
        public static object Deserialize(string json)
        {
            if (json == null) return null;
            return Parser.Parse(json);
        }

        public static string Serialize(object obj)
        {
            return Serializer.ToJson(obj);
        }

        private sealed class Parser : IDisposable
        {
            private const string WordBreak = "{}[],:\"";
            private System.IO.StringReader json;

            private Parser(string s) { json = new System.IO.StringReader(s); }

            public static object Parse(string s)
            {
                using (var p = new Parser(s)) return p.ParseValue();
            }

            public void Dispose() { json.Dispose(); json = null; }

            private Dictionary<string, object> ParseObject()
            {
                var table = new Dictionary<string, object>();
                json.Read(); // {
                while (true)
                {
                    switch (NextToken)
                    {
                        case Token.None: return null;
                        case Token.Comma: continue;
                        case Token.CurlyClose: return table;
                        default:
                            string name = ParseString();
                            if (name == null) return null;
                            if (NextToken != Token.Colon) return null;
                            json.Read();
                            table[name] = ParseValue();
                            break;
                    }
                }
            }

            private List<object> ParseArray()
            {
                var array = new List<object>();
                json.Read(); // [
                bool parsing = true;
                while (parsing)
                {
                    Token nextToken = NextToken;
                    switch (nextToken)
                    {
                        case Token.None: return null;
                        case Token.Comma: continue;
                        case Token.SquareClose: parsing = false; break;
                        default:
                            array.Add(ParseByToken(nextToken));
                            break;
                    }
                }
                return array;
            }

            private object ParseValue() => ParseByToken(NextToken);

            private object ParseByToken(Token token)
            {
                switch (token)
                {
                    case Token.String: return ParseString();
                    case Token.Number: return ParseNumber();
                    case Token.CurlyOpen: return ParseObject();
                    case Token.SquareOpen: return ParseArray();
                    case Token.True: return true;
                    case Token.False: return false;
                    case Token.Null: return null;
                    default: return null;
                }
            }

            private string ParseString()
            {
                var s = new StringBuilder();
                json.Read(); // "
                bool parsing = true;
                while (parsing)
                {
                    if (json.Peek() == -1) break;
                    char c = NextChar;
                    switch (c)
                    {
                        case '"': parsing = false; break;
                        case '\\':
                            if (json.Peek() == -1) { parsing = false; break; }
                            char esc = NextChar;
                            switch (esc)
                            {
                                case '"': s.Append('"'); break;
                                case '\\': s.Append('\\'); break;
                                case '/': s.Append('/'); break;
                                case 'b': s.Append('\b'); break;
                                case 'f': s.Append('\f'); break;
                                case 'n': s.Append('\n'); break;
                                case 'r': s.Append('\r'); break;
                                case 't': s.Append('\t'); break;
                                case 'u':
                                    var hex = new char[4];
                                    for (int i = 0; i < 4; i++) hex[i] = NextChar;
                                    s.Append((char)Convert.ToInt32(new string(hex), 16));
                                    break;
                            }
                            break;
                        default: s.Append(c); break;
                    }
                }
                return s.ToString();
            }

            private object ParseNumber()
            {
                string number = NextWord;
                if (number.IndexOf('.') == -1 && number.IndexOf('e') == -1 && number.IndexOf('E') == -1)
                {
                    long.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out long l);
                    return l;
                }
                double.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out double d);
                return d;
            }

            private void EatWhitespace()
            {
                while (json.Peek() != -1 && char.IsWhiteSpace((char)json.Peek())) json.Read();
            }

            private char NextChar => Convert.ToChar(json.Read());

            private string NextWord
            {
                get
                {
                    var word = new StringBuilder();
                    while (json.Peek() != -1 && WordBreak.IndexOf((char)json.Peek()) == -1
                           && !char.IsWhiteSpace((char)json.Peek()))
                        word.Append(NextChar);
                    return word.ToString();
                }
            }

            private Token NextToken
            {
                get
                {
                    EatWhitespace();
                    if (json.Peek() == -1) return Token.None;
                    switch ((char)json.Peek())
                    {
                        case '{': return Token.CurlyOpen;
                        case '}': json.Read(); return Token.CurlyClose;
                        case '[': return Token.SquareOpen;
                        case ']': json.Read(); return Token.SquareClose;
                        case ',': json.Read(); return Token.Comma;
                        case '"': return Token.String;
                        case ':': return Token.Colon;
                        case '0': case '1': case '2': case '3': case '4':
                        case '5': case '6': case '7': case '8': case '9': case '-':
                            return Token.Number;
                    }
                    switch (NextWord)
                    {
                        case "false": return Token.False;
                        case "true": return Token.True;
                        case "null": return Token.Null;
                    }
                    return Token.None;
                }
            }

            private enum Token
            {
                None, CurlyOpen, CurlyClose, SquareOpen, SquareClose,
                Colon, Comma, String, Number, True, False, Null
            }
        }

        private sealed class Serializer
        {
            private readonly StringBuilder builder = new StringBuilder();

            public static string ToJson(object obj)
            {
                var s = new Serializer();
                s.SerializeValue(obj);
                return s.builder.ToString();
            }

            private void SerializeValue(object value)
            {
                if (value == null) { builder.Append("null"); return; }
                if (value is string str) { SerializeString(str); return; }
                if (value is bool b) { builder.Append(b ? "true" : "false"); return; }
                if (value is IDictionary dict) { SerializeObject(dict); return; }
                if (value is IList list) { SerializeArray(list); return; }
                if (value is char c) { SerializeString(c.ToString()); return; }
                if (value is float f) { builder.Append(f.ToString("R", CultureInfo.InvariantCulture)); return; }
                if (value is double d) { builder.Append(d.ToString("R", CultureInfo.InvariantCulture)); return; }
                if (value is int || value is long || value is byte || value is short)
                { builder.Append(System.Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
                SerializeString(value.ToString());
            }

            private void SerializeObject(IDictionary obj)
            {
                bool first = true;
                builder.Append('{');
                foreach (object key in obj.Keys)
                {
                    if (!first) builder.Append(',');
                    SerializeString(key.ToString());
                    builder.Append(':');
                    SerializeValue(obj[key]);
                    first = false;
                }
                builder.Append('}');
            }

            private void SerializeArray(IList array)
            {
                builder.Append('[');
                bool first = true;
                foreach (object obj in array)
                {
                    if (!first) builder.Append(',');
                    SerializeValue(obj);
                    first = false;
                }
                builder.Append(']');
            }

            private void SerializeString(string str)
            {
                builder.Append('"');
                foreach (char c in str)
                {
                    switch (c)
                    {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\b': builder.Append("\\b"); break;
                        case '\f': builder.Append("\\f"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default: builder.Append(c); break;
                    }
                }
                builder.Append('"');
            }
        }

    }
}
