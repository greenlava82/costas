// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Globalization;
using System.Text.RegularExpressions;
using Ft8Client.Core.Cluster;
using Ft8Client.Core.Messages;

namespace Ft8Client.Integrations.DxCluster;

/// <summary>
/// Parses the real-time spot lines that DXSpider, AR-Cluster and CC Cluster nodes send (docs/05-integrations.md 5b), such
/// as <c>DX de DL1NCH:      3573.0 OZ8BLR       FT8 -05dB from JO46 814Hz      2012Z</c>. The spotter may carry a
/// skimmer's <c>-#</c>; the comment may be empty; a node set to add locators (DXSpider <c>set/dxgrid</c>) puts one after
/// the time. Fields are found by spacing, not by column, since long calls push the columns over.
/// </summary>
public static partial class ClusterSpotParser
{
    [GeneratedRegex(@"^DX de (?<sp>[A-Za-z0-9/\-]+?)(?:-#)?:\s*(?<f>\d+(?:\.\d+)?)\s+(?<dx>[A-Za-z0-9/]+)(?<c>.*?)\s(?<t>\d{4})Z(?:\s+(?<g>[A-Ra-r]{2}\d{2}(?:[A-Xa-x]{2})?))?\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Line();

    /// <summary>
    /// Parses one line. <paramref name="nowUtc"/> supplies the date for the line's <c>HHMMZ</c> time (a time more than
    /// five minutes ahead of now is taken as yesterday's). Returns null for anything that is not a spot of a valid call.
    /// </summary>
    public static ClusterSpot? Parse(string line, DateTime nowUtc)
    {
        var m = Line().Match(line.TrimEnd());
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups["f"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var khz) || khz <= 0) return null;
        var dx = m.Groups["dx"].Value.ToUpperInvariant();
        if (!Callsign.IsValid(dx)) return null;
        var t = m.Groups["t"].Value;
        var hh = (t[0] - '0') * 10 + (t[1] - '0');
        var mm = (t[2] - '0') * 10 + (t[3] - '0');
        if (hh > 23 || mm > 59) return null;
        var time = nowUtc.Date.AddHours(hh).AddMinutes(mm);
        if (time > nowUtc.AddMinutes(5)) time = time.AddDays(-1);
        var grid = m.Groups["g"].Success ? m.Groups["g"].Value.ToUpperInvariant() : null;
        return new ClusterSpot(dx, (long)Math.Round(khz * 1000), m.Groups["sp"].Value.ToUpperInvariant(), Squash(m.Groups["c"].Value),
            DateTime.SpecifyKind(time, DateTimeKind.Utc), grid);
    }

    // Comments are padded to a column; runs of spaces inside are kept as one.
    private static string Squash(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
