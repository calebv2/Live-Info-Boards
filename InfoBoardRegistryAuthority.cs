using System;
using System.Collections.Generic;

public sealed class InfoBoardRegistryChange
{
    public InfoBoardRegistryChange(string key, string channel, string text)
    {
        Key = key;
        Channel = channel;
        Text = text;
    }

    public string Key { get; private set; }
    public string Channel { get; private set; }
    public string Text { get; private set; }
}

public sealed class InfoBoardRegistryAuthority
{
    private readonly Dictionary<string, string> applied = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool GetChanges(Dictionary<string, InfoBoardDefinition> definitions, out IReadOnlyList<InfoBoardRegistryChange> changes)
    {
        var detected = new List<InfoBoardRegistryChange>();
        if (definitions == null)
        {
            changes = detected;
            return false;
        }

        var keys = new List<string>(definitions.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            InfoBoardDefinition definition = definitions[key];
            string state = definition.Channel + "\0" + definition.Text;
            string previous;
            if (applied.TryGetValue(key, out previous) && string.Equals(previous, state, StringComparison.Ordinal)) continue;
            detected.Add(new InfoBoardRegistryChange(key, definition.Channel, definition.Text));
        }
        changes = detected;
        return true;
    }

    public void MarkApplied(InfoBoardRegistryChange change)
    {
        if (change == null) throw new ArgumentNullException(nameof(change));
        applied[change.Key] = change.Channel + "\0" + change.Text;
    }

    public void Forget(string key)
    {
        if (key != null) applied.Remove(key);
    }
}
