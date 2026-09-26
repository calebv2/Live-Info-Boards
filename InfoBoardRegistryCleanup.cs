using System;
using System.Collections.Generic;

public static class InfoBoardRegistryCleanup
{
    public static bool Remove(Dictionary<string, InfoBoardDefinition> definitions, string positionKey, string channel, out bool channelUnused)
    {
        channelUnused = false;
        if (definitions == null || string.IsNullOrEmpty(positionKey)) return false;

        InfoBoardDefinition definition;
        if (!definitions.TryGetValue(positionKey, out definition)) return false;
        if (!string.IsNullOrEmpty(channel) && !string.Equals(definition.Channel, channel, StringComparison.Ordinal)) return false;

        definitions.Remove(positionKey);
        channelUnused = true;
        foreach (InfoBoardDefinition remaining in definitions.Values)
        {
            if (string.Equals(remaining.Channel, definition.Channel, StringComparison.Ordinal))
            {
                channelUnused = false;
                break;
            }
        }
        return true;
    }
}
