/// <summary>
/// Provides the MiniJson class for minimal JSON serialization and deserialization used by the MCP bridge.
/// </summary>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SL.Tasks
{
    /// <summary>
    /// Minimal JSON serializer and deserializer for MCP bridge communication.
    /// Handles dictionaries, lists, strings, numbers, booleans, and null values.
    /// </summary>
    public static class MiniJson
    {
        /// <summary>Deserializes a JSON string into a dictionary.</summary>
        /// <param name="json">The JSON string to parse.</param>
        /// <returns>A dictionary of string keys to object values.</returns>
        public static Dictionary<string, object> Deserialize(string json)
        {
            return Parse(json);
        }

        /// <summary>Serializes a dictionary, list, string, number, boolean, or null value to a JSON string.</summary>
        /// <param name="obj">The object to serialize.</param>
        /// <returns>A JSON string representation.</returns>
        public static string Serialize(object obj)
        {
            if (obj == null)
            {
                return "null";
            }

            if (obj is bool boolValue)
            {
                return boolValue ? "true" : "false";
            }

            if (obj is string stringValue)
            {
                return $"\"{EscapeString(stringValue)}\"";
            }

            if (obj is IFormattable formattable && (obj is int || obj is long || obj is float || obj is double))
            {
                return formattable.ToString(format: null, CultureInfo.InvariantCulture);
            }

            if (obj is Dictionary<string, object> dictionary)
            {
                StringBuilder builder = new StringBuilder("{");
                bool first = true;
                foreach (KeyValuePair<string, object> entry in dictionary)
                {
                    if (!first)
                    {
                        builder.Append(",");
                    }

                    builder.Append($"\"{EscapeString(entry.Key)}\":");
                    builder.Append(Serialize(entry.Value));
                    first = false;
                }

                builder.Append("}");
                return builder.ToString();
            }

            if (obj is Dictionary<string, float> floatDictionary)
            {
                StringBuilder builder = new StringBuilder("{");
                bool first = true;
                foreach (KeyValuePair<string, float> entry in floatDictionary)
                {
                    if (!first)
                    {
                        builder.Append(",");
                    }

                    string formattedValue = entry.Value.ToString(CultureInfo.InvariantCulture);
                    builder.Append($"\"{EscapeString(entry.Key)}\":{formattedValue}");
                    first = false;
                }

                builder.Append("}");
                return builder.ToString();
            }

            if (obj is IEnumerable<object> enumerable)
            {
                StringBuilder builder = new StringBuilder("[");
                bool first = true;
                foreach (object item in enumerable)
                {
                    if (!first)
                    {
                        builder.Append(",");
                    }

                    builder.Append(Serialize(item));
                    first = false;
                }

                builder.Append("]");
                return builder.ToString();
            }

            if (obj is IEnumerable<string> stringEnumerable)
            {
                StringBuilder builder = new StringBuilder("[");
                bool first = true;
                foreach (string item in stringEnumerable)
                {
                    if (!first)
                    {
                        builder.Append(",");
                    }

                    builder.Append(Serialize(item));
                    first = false;
                }

                builder.Append("]");
                return builder.ToString();
            }

            if (obj is IEnumerable<Dictionary<string, object>> dictionaryEnumerable)
            {
                StringBuilder builder = new StringBuilder("[");
                bool first = true;
                foreach (Dictionary<string, object> item in dictionaryEnumerable)
                {
                    if (!first)
                    {
                        builder.Append(",");
                    }

                    builder.Append(Serialize(item));
                    first = false;
                }

                builder.Append("]");
                return builder.ToString();
            }

            return $"\"{EscapeString(obj.ToString())}\"";
        }

        /// <summary>Escapes special characters in a string for JSON encoding.</summary>
        /// <param name="value">The string to escape.</param>
        /// <returns>The escaped string safe for JSON inclusion.</returns>
        private static string EscapeString(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        /// <summary>Parses a JSON string into a dictionary using a recursive-descent parser.</summary>
        /// <param name="json">The raw JSON string.</param>
        /// <returns>A parsed dictionary.</returns>
        private static Dictionary<string, object> Parse(string json)
        {
            int index = 0;
            return ParseObject(json, ref index);
        }

        /// <summary>Parses a JSON object.</summary>
        /// <param name="json">The JSON string being parsed.</param>
        /// <param name="index">The current parse position, advanced past the parsed object.</param>
        /// <returns>A dictionary representing the parsed JSON object.</returns>
        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            SkipWhitespace(json, ref index);

            if (index >= json.Length || json[index] != '{')
            {
                return result;
            }

            index++; // Skips '{'
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == '}')
            {
                index++;
                return result;
            }

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);

                if (index < json.Length && json[index] == ':')
                {
                    index++;
                }

                SkipWhitespace(json, ref index);
                object value = ParseValue(json, ref index);
                result[key] = value;
                SkipWhitespace(json, ref index);

                if (index < json.Length && json[index] == ',')
                {
                    index++;
                }
                else
                {
                    break;
                }
            }

            if (index < json.Length && json[index] == '}')
            {
                index++;
            }

            return result;
        }

        /// <summary>Parses a JSON value (object, array, string, number, boolean, or null).</summary>
        /// <param name="json">The JSON string being parsed.</param>
        /// <param name="index">The current parse position, advanced past the parsed value.</param>
        /// <returns>The parsed value as an object.</returns>
        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);

            if (index >= json.Length)
            {
                return null;
            }

            char character = json[index];

            if (character == '"')
                return ParseString(json, ref index);
            if (character == '{')
                return ParseObject(json, ref index);
            if (character == '[')
                return ParseArray(json, ref index);
            if (character == 't' || character == 'f')
                return ParseBool(json, ref index);
            if (character == 'n')
                return ParseNull(json, ref index);
            return ParseNumber(json, ref index);
        }

        /// <summary>Parses a JSON string.</summary>
        /// <param name="json">The JSON string being parsed.</param>
        /// <param name="index">The current parse position, advanced past the parsed string.</param>
        /// <returns>The parsed string value.</returns>
        private static string ParseString(string json, ref int index)
        {
            if (index >= json.Length || json[index] != '"')
            {
                return "";
            }

            index++; // Skips opening quote
            StringBuilder builder = new StringBuilder();

            while (index < json.Length && json[index] != '"')
            {
                if (json[index] == '\\' && index + 1 < json.Length)
                {
                    index++;
                    char escaped = json[index];
                    switch (escaped)
                    {
                        case '"':
                            builder.Append('"');
                            break;
                        case '\\':
                            builder.Append('\\');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        default:
                            builder.Append(escaped);
                            break;
                    }
                }
                else
                {
                    builder.Append(json[index]);
                }

                index++;
            }

            if (index < json.Length)
            {
                index++; // Skips closing quote
            }

            return builder.ToString();
        }

        /// <summary>Parses a JSON number.</summary>
        /// <param name="json">The JSON string being parsed.</param>
        /// <param name="index">The current parse position, advanced past the parsed number.</param>
        /// <returns>The parsed number as a long or double.</returns>
        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            while (
                index < json.Length
                && (
                    char.IsDigit(json[index])
                    || json[index] == '.'
                    || json[index] == '-'
                    || json[index] == 'e'
                    || json[index] == 'E'
                    || json[index] == '+'
                )
            )
            {
                index++;
            }

            string numberString = json.Substring(start, index - start);

            if (
                numberString.Contains('.', StringComparison.Ordinal)
                || numberString.Contains('e', StringComparison.Ordinal)
                || numberString.Contains('E', StringComparison.Ordinal)
            )
            {
                if (
                    !double.TryParse(
                        numberString,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double doubleValue
                    )
                )
                {
                    throw new FormatException($"MiniJson: '{numberString}' is not a valid floating-point literal.");
                }
                return doubleValue;
            }

            if (!long.TryParse(numberString, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue))
            {
                throw new FormatException($"MiniJson: '{numberString}' is not a valid integer literal.");
            }
            return longValue;
        }

        /// <summary>Parses a JSON boolean.</summary>
        /// <param name="json">The JSON string being parsed.</param>
        /// <param name="index">The current parse position, advanced past the parsed boolean.</param>
        /// <returns>The parsed boolean value.</returns>
        /// <exception cref="FormatException">
        /// The literal at <paramref name="index"/> is not "true" or "false".
        /// </exception>
        private static object ParseBool(string json, ref int index)
        {
            if (json.AsSpan(index).StartsWith("true", StringComparison.Ordinal))
            {
                index += 4;
                return true;
            }
            if (json.AsSpan(index).StartsWith("false", StringComparison.Ordinal))
            {
                index += 5;
                return false;
            }
            throw new FormatException($"MiniJson: expected 'true' or 'false' at index {index}.");
        }

        /// <summary>Parses a JSON null value.</summary>
        /// <param name="json">The JSON string being parsed.</param>
        /// <param name="index">The current parse position, advanced past the null literal.</param>
        /// <returns>Null.</returns>
        /// <exception cref="FormatException">The literal at <paramref name="index"/> is not "null".</exception>
        private static object ParseNull(string json, ref int index)
        {
            if (!json.AsSpan(index).StartsWith("null", StringComparison.Ordinal))
            {
                throw new FormatException($"MiniJson: expected 'null' at index {index}.");
            }
            index += 4;
            return null;
        }

        /// <summary>Parses a JSON array.</summary>
        /// <param name="json">The JSON string being parsed.</param>
        /// <param name="index">The current parse position, advanced past the parsed array.</param>
        /// <returns>A list of parsed values.</returns>
        private static List<object> ParseArray(string json, ref int index)
        {
            List<object> result = new List<object>();
            index++; // Skips '['
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == ']')
            {
                index++;
                return result;
            }

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                result.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);

                if (index < json.Length && json[index] == ',')
                {
                    index++;
                }
                else
                {
                    break;
                }
            }

            if (index < json.Length && json[index] == ']')
            {
                index++;
            }

            return result;
        }

        /// <summary>Advances the index past whitespace characters.</summary>
        /// <param name="json">The JSON string being parsed.</param>
        /// <param name="index">The current parse position, advanced past any whitespace.</param>
        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }
        }
    }
}
