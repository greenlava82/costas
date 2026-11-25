// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Settings;

/// <summary>Ranking settings as stored.</summary>
public sealed class RankingOptions
{
    /// <summary>Tier order: <c>country</c>, <c>band</c>, <c>grid</c>, <c>call</c>.</summary>
    public List<string> TierOrder { get; set; } = ["country", "band", "grid", "call"];

    /// <summary>Prefer stations that hear me.</summary>
    public bool PreferHearsMe { get; set; } = true;

    /// <summary>Only stations calling CQ.</summary>
    public bool OnlyCallingCq { get; set; } = true;

    /// <summary>
    /// Show the Repeats list (stations heard now that are already worked on this band and mode) under Next in line, on
    /// the desk and the phone. Worked stations are never in Next in line either way. Replaces <c>hideWorked</c>, which
    /// did nothing; a stored <c>hideWorked</c> is ignored.
    /// </summary>
    public bool ShowRepeats { get; set; } = true;

    /// <summary>
    /// Days after the last contact on a band and mode before that station counts as new again (Next in line instead of
    /// Repeats). 0 never: any contact ever makes it a repeat.
    /// </summary>
    public int NewAgainAfterDays { get; set; }

    /// <summary>Skip long shots.</summary>
    public bool SkipLongShots { get; set; }

    /// <summary>Rank activators at parks not in the log higher.</summary>
    public bool PreferNewParks { get; set; } = true;

    /// <summary>Count only confirmed contacts.</summary>
    public bool ConfirmedOnly { get; set; }

    /// <summary>Which confirmations count: <c>lotw</c> (what DXCC and WAS credit) or <c>any</c> (QRZ, LoTW or a card).</summary>
    public string ConfirmedBy { get; set; } = "lotw";

    /// <summary>
    /// Minutes a CQ caller stays in Next in line after it was last heard, unless it has been heard working someone
    /// since. 0 keeps only stations heard in their latest slot.
    /// </summary>
    public int KeepCqCallersMinutes { get; set; } = 2;
}
