using System;
using System.Collections.Generic;

public sealed class InfoBoardPlacementTracker
{
    private sealed class Observation
    {
        internal string ObservedKey;
        internal float ObservedAt;
        internal string SettledKey;
    }

    private readonly float settleSeconds;
    private readonly Dictionary<uint, Observation> observations = new Dictionary<uint, Observation>();

    public InfoBoardPlacementTracker(float settleSeconds)
    {
        this.settleSeconds = settleSeconds;
    }

    public bool TryGetSettledKey(uint boardId, string positionKey, float now, out string settledKey)
    {
        string ignored;
        return TryGetSettledKey(boardId, positionKey, now, out settledKey, out ignored);
    }

    public bool TryGetSettledKey(uint boardId, string positionKey, float now, out string settledKey, out string movedFromKey)
    {
        settledKey = null;
        movedFromKey = null;
        Observation observation;
        if (!observations.TryGetValue(boardId, out observation))
        {
            observations.Add(boardId, new Observation { ObservedKey = positionKey, ObservedAt = now, SettledKey = positionKey });
            settledKey = positionKey;
            return true;
        }

        if (!string.Equals(observation.ObservedKey, positionKey, StringComparison.Ordinal))
        {
            observation.ObservedKey = positionKey;
            observation.ObservedAt = now;
            settledKey = observation.SettledKey;
            return true;
        }

        if (now - observation.ObservedAt < settleSeconds)
        {
            settledKey = observation.SettledKey;
            return true;
        }
        settledKey = positionKey;
        if (!string.Equals(observation.SettledKey, positionKey, StringComparison.Ordinal))
        {
            movedFromKey = observation.SettledKey;
            observation.SettledKey = positionKey;
        }
        return true;
    }
}
