// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Bands;
using Ft8Client.Core.Messages;

namespace Ft8Client.Core.Cluster;

/// <summary>
/// The DX cluster spots kept for display, immutable. One spot per base call and band (a spot of K1ABC/P matches
/// K1ABC), the newest wins; spots older than <see cref="MaxAge"/> are left out of every answer and dropped on the next
/// change. Spots never take part in ranking: this only answers "was this station spotted, and when".
/// </summary>
public sealed class ClusterSpots
{
    /// <summary>How long a spot is shown (docs/05-integrations.md 5b).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly Dictionary<(string Call, string Band), ClusterSpot> _spots;

    private ClusterSpots(Dictionary<(string Call, string Band), ClusterSpot> spots) => _spots = spots;

    /// <summary>No spots.</summary>
    public static ClusterSpots Empty { get; } = new([]);

    /// <summary>Number of spots kept, including any not yet dropped for age.</summary>
    public int Count => _spots.Count;

    /// <summary>
    /// A copy with these spots added. Spots outside the amateur bands, without a call, older than
    /// <see cref="MaxAge"/> or older than the one kept for the same call and band are ignored.
    /// </summary>
    public ClusterSpots With(IEnumerable<ClusterSpot> spots, DateTime nowUtc)
    {
        var map = new Dictionary<(string, string), ClusterSpot>();
        foreach (var (k, v) in _spots)
            if (Fresh(v, nowUtc)) map[k] = v;
        var changed = map.Count != _spots.Count;
        foreach (var s in spots)
        {
            if (!Fresh(s, nowUtc) || BandPlan.BandOf(s.FrequencyHz) is not { } band) continue;
            var key = (Callsign.BaseCall(Callsign.Normalize(s.DxCall)), band);
            if (key.Item1.Length == 0) continue;
            if (map.TryGetValue(key, out var old) && old.TimeUtc > s.TimeUtc) continue;
            map[key] = s;
            changed = true;
        }
        return changed ? new ClusterSpots(map) : this;
    }

    /// <summary>The newest fresh spot of a call on a band (ADIF band name, <c>20m</c>), or null.</summary>
    public ClusterSpot? SpotFor(string call, string band, DateTime nowUtc) =>
        _spots.GetValueOrDefault((Callsign.BaseCall(Callsign.Normalize(call)), band.Trim().ToLowerInvariant())) is { } s && Fresh(s, nowUtc) ? s : null;

    /// <summary>The newest fresh spot of a call on any band, or null.</summary>
    public ClusterSpot? Latest(string call, DateTime nowUtc)
    {
        var baseCall = Callsign.BaseCall(Callsign.Normalize(call));
        ClusterSpot? best = null;
        foreach (var (k, v) in _spots)
            if (k.Call == baseCall && Fresh(v, nowUtc) && (best is null || v.TimeUtc > best.TimeUtc)) best = v;
        return best;
    }

    /// <summary>Every fresh spot with its band, newest first.</summary>
    public IReadOnlyList<(ClusterSpot Spot, string Band)> All(DateTime nowUtc) =>
        [.. _spots.Where(p => Fresh(p.Value, nowUtc)).OrderByDescending(p => p.Value.TimeUtc).Select(p => (p.Value, p.Key.Band))];

    // A spot time slightly ahead of the clock (the node's clock, or rounding to the minute) is still fresh.
    private static bool Fresh(ClusterSpot s, DateTime nowUtc) => nowUtc - s.TimeUtc <= MaxAge;
}
