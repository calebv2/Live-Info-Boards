using System;
using System.Collections.Generic;

public sealed class InfoBoardChange
{
    public InfoBoardChange(string channel, string text)
    {
        Channel = channel;
        Text = text;
    }

    public string Channel { get; private set; }
    public string Text { get; private set; }
}

public sealed class InfoBoardAuthority
{
    private readonly Dictionary<string, string> applied = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool TryReadChanges(string json, out IReadOnlyList<InfoBoardChange> changes, out string error)
    {
        changes = new List<InfoBoardChange>();
        Dictionary<string, string> entries;
        if (!InfoBoardDocument.TryParse(json, out entries, out error)) return false;

        var orderedChannels = new List<string>(entries.Keys);
        orderedChannels.Sort(StringComparer.Ordinal);
        var detected = (List<InfoBoardChange>)changes;
        foreach (string channel in orderedChannels)
        {
            string text = entries[channel];
            string previous;
            if (applied.TryGetValue(channel, out previous) && string.Equals(previous, text, StringComparison.Ordinal)) continue;
            detected.Add(new InfoBoardChange(channel, text));
        }

        error = string.Empty;
        return true;
    }

    public void MarkApplied(InfoBoardChange change)
    {
        if (change == null) throw new ArgumentNullException(nameof(change));
        applied[change.Channel] = change.Text;
    }
}
