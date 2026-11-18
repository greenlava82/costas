// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Globalization;
using Ft8Client.Core.Bands;

namespace Ft8Client.Core.Cluster;

/// <summary>Display texts for a DX cluster spot. Every value comes straight from the spot.</summary>
public static class ClusterText
{
    /// <summary>The short line under Why: "Spotted 4 min ago", "Spotted just now". Empty for null.</summary>
    public static string Why(ClusterSpot? s, DateTime nowUtc) => s is null ? string.Empty : $"Spotted {Age(s, nowUtc)}";

    /// <summary>
    /// The full line for station details and tooltips: "Spotted 4 min ago on 15 m by DL1ABC", with the comment after a
    /// dot when there is one ("… by DL1ABC · FT8 -12 dB"). Empty for null.
    /// </summary>
    public static string Details(ClusterSpot? s, DateTime nowUtc)
    {
        if (s is null) return string.Empty;
        var band = BandPlan.BandOf(s.FrequencyHz) is { } b ? $" on {BandPlan.Display(b)}" : string.Empty;
        var line = $"Spotted {Age(s, nowUtc)}{band} by {s.Spotter}";
        return s.Comment.Length == 0 ? line : $"{line} · {s.Comment}";
    }

    /// <summary>The frequency as clusters write it, in kHz with one decimal: "21074.0".</summary>
    public static string Khz(long hz) => (hz / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Age(ClusterSpot s, DateTime nowUtc)
    {
        var m = (int)Math.Max(0, (nowUtc - s.TimeUtc).TotalMinutes);
        return m == 0 ? "just now" : m == 1 ? "1 min ago" : $"{m} min ago";
    }
}
