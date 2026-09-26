using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public static class InfoBoardDocument
{
    private static readonly Regex ChannelPattern = new Regex("^[A-Za-z0-9_-]{1,20}$", RegexOptions.CultureInvariant);

    public static bool TryParse(string json, out Dictionary<string, string> entries, out string error)
    {
        entries = new Dictionary<string, string>(StringComparer.Ordinal);
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "InfoBoards.json is empty.";
            return false;
        }

        var reader = new StringMapReader(json);
        if (!reader.TryRead(entries, out error)) return false;

        foreach (KeyValuePair<string, string> entry in entries)
        {
            if (!ChannelPattern.IsMatch(entry.Key))
            {
                error = "Invalid infoboard channel '" + entry.Key + "'. Use 1-20 letters, digits, underscores, or hyphens.";
                return false;
            }
            if (entry.Value.Length > 500)
            {
                error = "Infoboard channel '" + entry.Key + "' exceeds the 500 character game limit.";
                return false;
            }
        }
        return true;
    }

    private sealed class StringMapReader
    {
        private readonly string source;
        private int position;

        internal StringMapReader(string source) { this.source = source; }

        internal bool TryRead(Dictionary<string, string> entries, out string error)
        {
            error = string.Empty;
            SkipWhitespace();
            if (!Consume('{')) return Fail("InfoBoards.json must contain one JSON object mapping channel names to text.", out error);
            SkipWhitespace();
            if (Consume('}')) return AtEnd(out error);

            while (true)
            {
                string key;
                if (!TryReadString(out key, out error)) return false;
                SkipWhitespace();
                if (!Consume(':')) return Fail("Expected ':' after channel name at character " + position + ".", out error);
                SkipWhitespace();
                string value;
                if (!TryReadString(out value, out error)) return false;
                if (entries.ContainsKey(key)) return Fail("Duplicate infoboard channel '" + key + "'.", out error);
                entries.Add(key, value);
                SkipWhitespace();
                if (Consume('}')) return AtEnd(out error);
                if (!Consume(',')) return Fail("Expected ',' or '}' at character " + position + ".", out error);
                SkipWhitespace();
            }
        }

        private bool TryReadString(out string value, out string error)
        {
            value = string.Empty;
            error = string.Empty;
            if (!Consume('"')) return Fail("Infoboard channel names and values must be JSON strings at character " + position + ".", out error);
            var builder = new StringBuilder();
            while (position < source.Length)
            {
                char character = source[position++];
                if (character == '"')
                {
                    value = builder.ToString();
                    return true;
                }
                if (character < ' ') return Fail("Control character in JSON string at character " + (position - 1) + ".", out error);
                if (character != '\\')
                {
                    builder.Append(character);
                    continue;
                }
                if (position >= source.Length) return Fail("Incomplete JSON escape sequence.", out error);
                switch (source[position++])
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (position + 4 > source.Length) return Fail("Incomplete Unicode escape sequence.", out error);
                        ushort codePoint;
                        if (!ushort.TryParse(source.Substring(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out codePoint))
                            return Fail("Invalid Unicode escape sequence at character " + position + ".", out error);
                        builder.Append((char)codePoint);
                        position += 4;
                        break;
                    default: return Fail("Invalid JSON escape sequence at character " + (position - 1) + ".", out error);
                }
            }
            return Fail("Unterminated JSON string.", out error);
        }

        private void SkipWhitespace()
        {
            while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
        }

        private bool Consume(char expected)
        {
            if (position >= source.Length || source[position] != expected) return false;
            position++;
            return true;
        }

        private bool AtEnd(out string error)
        {
            SkipWhitespace();
            if (position == source.Length)
            {
                error = string.Empty;
                return true;
            }
            return Fail("Unexpected content at character " + position + ".", out error);
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
