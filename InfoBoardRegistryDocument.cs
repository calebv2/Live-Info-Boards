using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed class InfoBoardDefinition
{
    public InfoBoardDefinition(string channel, string text) : this(channel, text, null) { }

    public InfoBoardDefinition(string channel, string text, InfoBoardRotation rotation)
    {
        Channel = channel;
        Text = text;
        Rotation = rotation;
    }

    public string Channel { get; private set; }
    public string Text { get; private set; }
    public InfoBoardRotation Rotation { get; private set; }
}

public sealed class InfoBoardRotation
{
    public bool Enabled { get; private set; }
    public int Interval { get; private set; }
    public string Unit { get; private set; }
    public List<string> Messages { get; private set; }

    public InfoBoardRotation(bool enabled, int interval, string unit, List<string> messages)
    {
        Enabled = enabled;
        Interval = interval;
        Unit = unit;
        Messages = messages;
    }

    public string TextAt(float elapsedSeconds, string fallback)
    {
        if (!Enabled || Messages == null || Messages.Count == 0) return fallback;
        double intervalSeconds = Unit == "minutes" ? Interval * 60d : Interval;
        int index = (int)Math.Floor(Math.Max(0d, elapsedSeconds) / intervalSeconds) % Messages.Count;
        return Messages[index];
    }
}

public static class InfoBoardRegistryDocument
{
    private const int MaxTextLength = 5000;
    public static bool TryParse(string json, out Dictionary<string, InfoBoardDefinition> boards, out string error)
    {
        boards = new Dictionary<string, InfoBoardDefinition>(StringComparer.Ordinal);
        var reader = new Reader(json);
        Dictionary<string, Value> root;
        if (!reader.TryReadObject(out root, out error)) return false;
        if (root.Count != 1 || !root.ContainsKey("boards") || root["boards"].Object == null)
        {
            error = "InfoBoards.json must contain a single 'boards' object.";
            return false;
        }
        foreach (KeyValuePair<string, Value> entry in root["boards"].Object)
        {
            if (entry.Value.Object == null)
            {
                error = "Each board position must have an object value.";
                return false;
            }
            Value channel;
            Value text;
            if ((entry.Value.Object.Count != 2 && entry.Value.Object.Count != 3) || !entry.Value.Object.TryGetValue("channel", out channel) || !entry.Value.Object.TryGetValue("text", out text) || channel.Text == null || text.Text == null)
            {
                error = "Board '" + entry.Key + "' must contain string 'channel' and 'text' properties.";
                return false;
            }
            InfoBoardRotation rotation = null;
            Value rotationValue;
            if (entry.Value.Object.TryGetValue("rotation", out rotationValue))
            {
                if (!TryReadRotation(rotationValue, out rotation, out error)) return false;
            }
            boards.Add(entry.Key, new InfoBoardDefinition(channel.Text, text.Text, rotation));
        }
        error = string.Empty;
        return true;
    }

    public static string Serialize(Dictionary<string, InfoBoardDefinition> boards)
    {
        var keys = new List<string>(boards.Keys);
        keys.Sort(StringComparer.Ordinal);
        var output = new StringBuilder("{\n  \"boards\": {");
        for (int i = 0; i < keys.Count; i++)
        {
            string key = keys[i];
            InfoBoardDefinition board = boards[key];
            output.Append("\n    \"").Append(Escape(key)).Append("\": { \"channel\": \"")
                .Append(Escape(board.Channel)).Append("\", \"text\": \"").Append(Escape(board.Text)).Append("\"");
            if (board.Rotation != null)
            {
                output.Append(", \"rotation\": { \"enabled\": ").Append(board.Rotation.Enabled ? "true" : "false")
                    .Append(", \"interval\": ").Append(board.Rotation.Interval.ToString(CultureInfo.InvariantCulture))
                    .Append(", \"unit\": \"").Append(Escape(board.Rotation.Unit)).Append("\", \"messages\": [");
                for (int messageIndex = 0; messageIndex < board.Rotation.Messages.Count; messageIndex++)
                {
                    if (messageIndex > 0) output.Append(", ");
                    output.Append("\"").Append(Escape(board.Rotation.Messages[messageIndex])).Append("\"");
                }
                output.Append("] }");
            }
            output.Append(" }");
            if (i + 1 < keys.Count) output.Append(',');
        }
        return output.Append("\n  }\n}\n").ToString();
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    private static bool TryReadRotation(Value value, out InfoBoardRotation rotation, out string error)
    {
        rotation = null;
        error = string.Empty;
        if (value == null || value.Object == null || value.Object.Count != 4) { error = "Rotation must contain enabled, interval, unit, and messages."; return false; }
        Value enabled; Value interval; Value unit; Value messages;
        if (!value.Object.TryGetValue("enabled", out enabled) || enabled.Boolean == null || !value.Object.TryGetValue("interval", out interval) || interval.Number == null || !value.Object.TryGetValue("unit", out unit) || unit.Text == null || !value.Object.TryGetValue("messages", out messages) || messages.Array == null)
        { error = "Rotation must contain enabled, interval, unit, and messages."; return false; }
        int parsedInterval;
        if (!int.TryParse(interval.Number, NumberStyles.None, CultureInfo.InvariantCulture, out parsedInterval) || parsedInterval < 1 || parsedInterval > 86400 || (unit.Text != "seconds" && unit.Text != "minutes"))
        { error = "Rotation interval must be 1-86400 and unit must be seconds or minutes."; return false; }
        if (messages.Array.Count > 20) { error = "Rotation messages must contain no more than 20 messages."; return false; }
        var texts = new List<string>();
        foreach (Value message in messages.Array)
        {
            if (message.Text == null || message.Text.Length == 0 || message.Text.Length > MaxTextLength) { error = "Rotation messages must be non-empty strings of 5000 characters or fewer."; return false; }
            texts.Add(message.Text);
        }
        rotation = new InfoBoardRotation(enabled.Boolean.Value, parsedInterval, unit.Text, texts);
        return true;
    }

    private sealed class Value
    {
        internal string Text;
        internal Dictionary<string, Value> Object;
        internal List<Value> Array;
        internal bool? Boolean;
        internal string Number;
    }

    private sealed class Reader
    {
        private readonly string source;
        private int position;
        internal Reader(string source) { this.source = source ?? string.Empty; }

        internal bool TryReadObject(out Dictionary<string, Value> result, out string error)
        {
            return ReadObject(out result, out error, true);
        }

        private bool ReadObject(out Dictionary<string, Value> result, out string error, bool requireEnd)
        {
            result = new Dictionary<string, Value>(StringComparer.Ordinal);
            error = string.Empty;
            Skip();
            if (!Consume('{')) return Fail("Expected JSON object.", out error);
            Skip();
            if (Consume('}')) return requireEnd ? End(out error) : Success(out error);
            while (true)
            {
                string key;
                if (!String(out key, out error)) return false;
                Skip();
                if (!Consume(':')) return Fail("Expected ':' at character " + position + ".", out error);
                Skip();
                Value value;
                if (!ReadValue(out value, out error)) return false;
                if (result.ContainsKey(key)) return Fail("Duplicate property '" + key + "'.", out error);
                result.Add(key, value);
                Skip();
                if (Consume('}')) return requireEnd ? End(out error) : Success(out error);
                if (!Consume(',')) return Fail("Expected ',' or '}' at character " + position + ".", out error);
                Skip();
            }
        }

        private bool ReadValue(out Value value, out string error)
        {
            value = new Value();
            error = string.Empty;
            if (position < source.Length && source[position] == '"') return String(out value.Text, out error);
            if (source.Substring(position).StartsWith("true", StringComparison.Ordinal)) { position += 4; value.Boolean = true; return true; }
            if (source.Substring(position).StartsWith("false", StringComparison.Ordinal)) { position += 5; value.Boolean = false; return true; }
            if (position < source.Length && (source[position] == '-' || char.IsDigit(source[position])))
            {
                int start = position;
                if (source[position] == '-') position++;
                while (position < source.Length && char.IsDigit(source[position])) position++;
                value.Number = source.Substring(start, position - start);
                return true;
            }
            if (position < source.Length && source[position] == '[') return ReadArray(out value.Array, out error);
            if (position >= source.Length || source[position] != '{') return Fail("Board values must be strings or objects at character " + position + ".", out error);
            return ReadObject(out value.Object, out error, false);
        }

        private bool ReadArray(out List<Value> result, out string error)
        {
            result = new List<Value>(); error = string.Empty; Consume('['); Skip();
            if (Consume(']')) return true;
            while (true)
            {
                Value value; if (!ReadValue(out value, out error)) return false; result.Add(value); Skip();
                if (Consume(']')) return true;
                if (!Consume(',')) return Fail("Expected ',' or ']' at character " + position + ".", out error); Skip();
            }
        }

        private bool String(out string value, out string error)
        {
            value = string.Empty;
            error = string.Empty;
            if (!Consume('"')) return Fail("Expected JSON string at character " + position + ".", out error);
            var output = new StringBuilder();
            while (position < source.Length)
            {
                char c = source[position++];
                if (c == '"') { value = output.ToString(); return true; }
                if (c < ' ') return Fail("Control character in JSON string.", out error);
                if (c != '\\') { output.Append(c); continue; }
                if (position >= source.Length) return Fail("Incomplete JSON escape.", out error);
                switch (source[position++])
                {
                    case '"': output.Append('"'); break; case '\\': output.Append('\\'); break; case '/': output.Append('/'); break;
                    case 'b': output.Append('\b'); break; case 'f': output.Append('\f'); break; case 'n': output.Append('\n'); break;
                    case 'r': output.Append('\r'); break; case 't': output.Append('\t'); break;
                    default: return Fail("Unsupported JSON escape.", out error);
                }
            }
            return Fail("Unterminated JSON string.", out error);
        }

        private void Skip() { while (position < source.Length && char.IsWhiteSpace(source[position])) position++; }
        private bool Consume(char c) { if (position >= source.Length || source[position] != c) return false; position++; return true; }
        private bool End(out string error) { Skip(); return position == source.Length ? Success(out error) : Fail("Unexpected content at character " + position + ".", out error); }
        private static bool Success(out string error) { error = string.Empty; return true; }
        private static bool Fail(string message, out string error) { error = message; return false; }
    }
}
