using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · Minimal JSON reader for the pipeline's provenance files.
    ///
    /// <c>JsonUtility</c> cannot read the layout JSON: it has nested lists
    /// (<c>[[x, y, z], ...]</c>, <c>materials: [...]</c>) and dictionaries keyed by
    /// name (<c>canonical_lod_bounds_gltf</c>), neither of which it supports. Pulling
    /// in a JSON package for one editor tool is not worth a dependency, so this is a
    /// plain recursive-descent parser (RFC 8259): objects become
    /// <c>Dictionary&lt;string, object&gt;</c>, arrays <c>List&lt;object&gt;</c>, numbers
    /// <c>double</c>, plus <c>bool</c>, <c>string</c> and <c>null</c>. Editor-only;
    /// nothing in a Player ever parses JSON this way.
    ///
    /// The typed helpers below turn the loosely typed tree into the vectors and ints
    /// the builders want and throw a readable <see cref="FormatException"/> on a
    /// mismatch, naming the key, so a malformed export fails at the field and not
    /// with an InvalidCastException three frames deep.
    /// </summary>
    public static class ArtJson
    {
        // ------------------------------------------------------------------- parse

        /// <summary>Parses a complete JSON document. Throws <see cref="FormatException"/> on malformed input.</summary>
        public static object Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var reader = new Reader(json);
            reader.SkipWhitespace();
            object value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw reader.Error("trailing characters after the document");
            }

            return value;
        }

        // ----------------------------------------------------------------- helpers

        public static Dictionary<string, object> AsObject(object value, string what = "value")
        {
            if (value is Dictionary<string, object> dict)
            {
                return dict;
            }

            throw new FormatException("[ArtJson] " + what + " is not an object (" + Describe(value) + ")");
        }

        public static List<object> AsList(object value, string what = "value")
        {
            if (value is List<object> list)
            {
                return list;
            }

            throw new FormatException("[ArtJson] " + what + " is not an array (" + Describe(value) + ")");
        }

        public static double AsDouble(object value, string what = "value")
        {
            if (value is double d)
            {
                return d;
            }

            throw new FormatException("[ArtJson] " + what + " is not a number (" + Describe(value) + ")");
        }

        public static float AsFloat(object value, string what = "value")
        {
            return (float)AsDouble(value, what);
        }

        public static int AsInt(object value, string what = "value")
        {
            double d = AsDouble(value, what);
            return (int)Math.Round(d);
        }

        public static bool AsBool(object value, string what = "value")
        {
            if (value is bool b)
            {
                return b;
            }

            throw new FormatException("[ArtJson] " + what + " is not a boolean (" + Describe(value) + ")");
        }

        public static string AsString(object value, string what = "value")
        {
            if (value is string s)
            {
                return s;
            }

            throw new FormatException("[ArtJson] " + what + " is not a string (" + Describe(value) + ")");
        }

        /// <summary>The member, or null when absent or JSON null.</summary>
        public static object Get(Dictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out object value) ? value : null;
        }

        /// <summary>The member; throws when absent or null.</summary>
        public static object Require(Dictionary<string, object> obj, string key)
        {
            object value = Get(obj, key);
            if (value == null)
            {
                throw new FormatException("[ArtJson] required member '" + key + "' is missing or null");
            }

            return value;
        }

        /// <summary><c>[x, y, z]</c> → Vector3.</summary>
        public static Vector3 ToVector3(object value, string what = "value")
        {
            List<object> list = AsList(value, what);
            if (list.Count != 3)
            {
                throw new FormatException("[ArtJson] " + what + " has " + list.Count + " components, expected 3");
            }

            return new Vector3(AsFloat(list[0], what), AsFloat(list[1], what), AsFloat(list[2], what));
        }

        /// <summary><c>[x, y, z, w]</c> → Quaternion (glTF component order).</summary>
        public static Quaternion ToQuaternion(object value, string what = "value")
        {
            List<object> list = AsList(value, what);
            if (list.Count != 4)
            {
                throw new FormatException("[ArtJson] " + what + " has " + list.Count + " components, expected 4");
            }

            return new Quaternion(AsFloat(list[0], what), AsFloat(list[1], what), AsFloat(list[2], what),
                AsFloat(list[3], what));
        }

        private static string Describe(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is Dictionary<string, object>)
            {
                return "object";
            }

            if (value is List<object> list)
            {
                return "array[" + list.Count + "]";
            }

            return value.GetType().Name + " " + value;
        }

        // ------------------------------------------------------------------ reader

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s)
            {
                _s = s;
                _i = 0;

                // A UTF-8 BOM survives File.ReadAllText/GetString as U+FEFF.
                if (_s.Length > 0 && _s[0] == '﻿')
                {
                    _i = 1;
                }
            }

            public bool AtEnd => _i >= _s.Length;

            public FormatException Error(string message)
            {
                int line = 1;
                int column = 1;
                for (int k = 0; k < _i && k < _s.Length; k++)
                {
                    if (_s[k] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                }

                return new FormatException("[ArtJson] " + message + " at line " + line + ", column " + column);
            }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        _i++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public object ReadValue()
            {
                if (AtEnd)
                {
                    throw Error("unexpected end of input");
                }

                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ReadLiteral("true"); return true;
                    case 'f': ReadLiteral("false"); return false;
                    case 'n': ReadLiteral("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ReadNumber();
                        }

                        throw Error("unexpected character '" + c + "'");
                }
            }

            private Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++; // '{'
                SkipWhitespace();

                if (Peek() == '}')
                {
                    _i++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                    {
                        throw Error("expected a string key");
                    }

                    string key = ReadString();
                    SkipWhitespace();
                    if (Peek() != ':')
                    {
                        throw Error("expected ':' after key '" + key + "'");
                    }

                    _i++;
                    SkipWhitespace();

                    // Last one wins on a duplicate key, as every mainstream parser does.
                    result[key] = ReadValue();
                    SkipWhitespace();

                    char c = Peek();
                    if (c == ',')
                    {
                        _i++;
                        continue;
                    }

                    if (c == '}')
                    {
                        _i++;
                        return result;
                    }

                    throw Error("expected ',' or '}' in object");
                }
            }

            private List<object> ReadArray()
            {
                var result = new List<object>();
                _i++; // '['
                SkipWhitespace();

                if (Peek() == ']')
                {
                    _i++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    result.Add(ReadValue());
                    SkipWhitespace();

                    char c = Peek();
                    if (c == ',')
                    {
                        _i++;
                        continue;
                    }

                    if (c == ']')
                    {
                        _i++;
                        return result;
                    }

                    throw Error("expected ',' or ']' in array");
                }
            }

            private string ReadString()
            {
                _i++; // opening quote

                // Fast path: no escapes, one Substring.
                int start = _i;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == '"')
                    {
                        string plain = _s.Substring(start, _i - start);
                        _i++;
                        return plain;
                    }

                    if (c == '\\')
                    {
                        break;
                    }

                    _i++;
                }

                var sb = new StringBuilder(_s, start, _i - start, 64);
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Error("unterminated string");
                    }

                    char c = _s[_i++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }

                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (AtEnd)
                    {
                        throw Error("unterminated escape");
                    }

                    char e = _s[_i++];
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
                            if (_i + 4 > _s.Length)
                            {
                                throw Error("truncated \\u escape");
                            }

                            string hex = _s.Substring(_i, 4);
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
                                throw Error("invalid \\u escape '" + hex + "'");
                            }

                            // Surrogate halves are appended as-is; a pair reassembles itself.
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            throw Error("invalid escape '\\" + e + "'");
                    }
                }
            }

            private double ReadNumber()
            {
                int start = _i;
                if (Peek() == '-')
                {
                    _i++;
                }

                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        _i++;
                    }
                    else
                    {
                        break;
                    }
                }

                string text = _s.Substring(start, _i - start);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    throw Error("invalid number '" + text + "'");
                }

                return value;
            }

            private void ReadLiteral(string literal)
            {
                if (string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                {
                    throw Error("expected '" + literal + "'");
                }

                _i += literal.Length;
            }

            private char Peek()
            {
                if (AtEnd)
                {
                    throw Error("unexpected end of input");
                }

                return _s[_i];
            }
        }
    }
}
