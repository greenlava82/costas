// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>
/// Which band the WSPR beacon is on in a slot when it hops (docs/10-wspr.md section 7). Pure, and driven by the slot's UTC
/// time alone, so a restart picks up where the schedule is. The operator's list is walked in order, a band for
/// <c>dwell</c> slots; or, on WSJT-X's coordinated schedule, the band is the one the 2-minute slot within each 20 minutes
/// names among the 10 coordinated bands (V-WSPR6), for the bands in the operator's list.
/// </summary>
public static class WsprHopSchedule
{
    /// <summary>The 10 globally coordinated bands, in WSJT-X's order: slot 0 of each 20 minutes is 160 m, slot 9 is 10 m.</summary>
    public static IReadOnlyList<string> CoordinatedBands { get; } = ["160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m"];

    /// <summary>
    /// The band for the slot that starts at <paramref name="slotStartUtc"/>, or null when the beacon does not hop (fewer than
    /// two bands in the list).
    /// </summary>
    /// <param name="slotStartUtc">The slot.</param>
    /// <param name="bands">The operator's bands, as <c>20m</c>, in order, each once.</param>
    /// <param name="coordinated">Follow WSJT-X's coordinated schedule where the band is in the list.</param>
    /// <param name="dwellSlots">Slots to stay on a band when walking the list (at least 1).</param>
    public static string? BandFor(DateTime slotStartUtc, IReadOnlyList<string> bands, bool coordinated, int dwellSlots)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Count < 2) return null;
        if (coordinated)
        {
            var named = CoordinatedBands[WsprTxSchedule.HoppingIndex(slotStartUtc)];
            var match = bands.FirstOrDefault(b => string.Equals(b, named, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        var slotsSinceEpoch = slotStartUtc.Ticks / WsprTiming.SlotLength.Ticks;
        var turn = slotsSinceEpoch / Math.Max(1, dwellSlots);
        return bands[(int)(turn % bands.Count)];
    }

    /// <summary>
    /// The operator's band list cleaned up: names normalised to <c>20m</c>, repeated bands dropped, anything without a WSPR
    /// frequency (<paramref name="hasWsprFrequency"/>) left out, in the order given.
    /// </summary>
    public static IReadOnlyList<string> Clean(IEnumerable<string> bands, Func<string, bool> hasWsprFrequency)
    {
        ArgumentNullException.ThrowIfNull(bands);
        ArgumentNullException.ThrowIfNull(hasWsprFrequency);
        var list = new List<string>();
        foreach (var raw in bands)
        {
            var b = raw.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (b.Length == 0 || list.Contains(b) || !hasWsprFrequency(b)) continue;
            list.Add(b);
        }
        return list;
    }
}
