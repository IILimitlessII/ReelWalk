using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ReelWalk.Models;
using ReelWalk.Services.Controls;

namespace ReelWalk.Services;
internal static partial class ConfigService
{
    private sealed class TomlDoc
    {
        internal readonly Dictionary<string, string> Values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, List<string>> Arrays =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // True when the toml key was present.
        // key is section.name.
        internal bool Has(string key)
        {
            return Values.ContainsKey(key);
        }

        // String value for key.
        // Returns fallback when the key is missing.
        internal string Text(string key, string fallback)
        {
            string value;
            return Values.TryGetValue(key, out value) ? value : fallback;
        }

        // Number value for key.
        // Returns fallback when the key is missing or not a number.
        internal double Number(string key, double fallback)
        {
            string value;
            if (!Values.TryGetValue(key, out value))
                return fallback;
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CI, out parsed) ? parsed : fallback;
        }

        // Boolean value for key.
        // Returns fallback when the key is missing or not true or false.
        internal bool Bool(string key, bool fallback)
        {
            string value;
            if (!Values.TryGetValue(key, out value))
                return fallback;
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;
            return fallback;
        }
    }

    // Parses the toml subset this app writes.
    // lines are the file. Returns keys and arrays.
    private static TomlDoc ParseToml(string[] lines)
    {
        var doc = new TomlDoc();
        string section = "";
        string arrayKey = null;
        List<string> arrayItems = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = StripComment(lines[i]).Trim();
            if (raw.Length == 0)
                continue;

            if (arrayKey != null)
            {
                if (raw[0] == ']')
                {
                    doc.Arrays[arrayKey] = arrayItems;
                    arrayKey = null;
                    arrayItems = null;
                    continue;
                }
                ReadStrings(raw, arrayItems);
                continue;
            }

            if (raw[0] == '[' && raw[raw.Length - 1] == ']' && raw.IndexOf('=') < 0)
            {
                section = raw.Substring(1, raw.Length - 2).Trim();
                continue;
            }

            int eq = raw.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = raw.Substring(0, eq).Trim();
            var val = raw.Substring(eq + 1).Trim();
            var full = section.Length == 0 ? key : section + "." + key;

            if (val.Length > 0 && val[0] == '[')
            {
                var items = new List<string>();
                var body = val.Substring(1).Trim();
                if (body.IndexOf(']') >= 0)
                {
                    ReadStrings(body, items);
                    doc.Arrays[full] = items;
                }
                else
                {
                    if (body.Length > 0)
                        ReadStrings(body, items);
                    arrayKey = full;
                    arrayItems = items;
                }
                continue;
            }

            doc.Values[full] = Unquote(val);
        }

        if (arrayKey != null)
            doc.Arrays[arrayKey] = arrayItems;

        return doc;
    }

    // Removes a # comment that is outside quotes.
    // line is one source line. Returns the code portion.
    private static string StripComment(string line)
    {
        bool inString = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"' && !IsEscaped(line, i))
                inString = !inString;
            else if (c == '#' && !inString)
                return line.Substring(0, i);
        }
        return line;
    }

    // True when the quote at index is escaped by a backslash.
    // line is the source line.
    private static bool IsEscaped(string line, int index)
    {
        int slashes = 0;
        for (int i = index - 1; i >= 0 && line[i] == '\\'; i--)
            slashes++;
        return (slashes % 2) == 1;
    }

    // Removes quotes and toml escapes.
    // raw is the value after equals. Returns the text.
    private static string Unquote(string raw)
    {
        raw = raw.Trim();
        if (raw.Length < 2 || raw[0] != '"')
            return raw;
        var sb = new StringBuilder(raw.Length);
        for (int i = 1; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '\\' && i + 1 < raw.Length)
            {
                char n = raw[++i];
                if (n == 'n') sb.Append('\n');
                else if (n == 't') sb.Append('\t');
                else sb.Append(n);
            }
            else if (c == '"')
                break;
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    // Pulls quoted strings out of one array line.
    // into receives them. Returns nothing.
    private static void ReadStrings(string line, List<string> into)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (char.IsWhiteSpace(c) || c == ',')
                continue;
            if (c == ']')
                return;
            if (c == '"')
            {
                var sb = new StringBuilder();
                for (i++; i < line.Length; i++)
                {
                    if (line[i] == '\\' && i + 1 < line.Length)
                    {
                        char n = line[++i];
                        if (n == 'n') sb.Append('\n');
                        else if (n == 't') sb.Append('\t');
                        else sb.Append(n);
                    }
                    else if (line[i] == '"')
                        break;
                    else
                        sb.Append(line[i]);
                }
                into.Add(sb.ToString());
            }
            else
            {
                int start = i;
                while (i < line.Length && line[i] != ',' && line[i] != ']')
                    i++;
                var token = line.Substring(start, i - start).Trim();
                if (token.Length > 0)
                    into.Add(token);
                if (i < line.Length && line[i] == ']')
                    return;
            }
        }
    }
}
