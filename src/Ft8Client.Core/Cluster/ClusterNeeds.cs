// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using Ft8Client.Core.Bands;
using Ft8Client.Core.Geo;
using Ft8Client.Core.Logbook;

namespace Ft8Client.Core.Cluster;

/// <summary>
/// Picks the spotted stations worth showing: spots for the mode being worked (<see cref="ClusterModeFilter"/>) of a
/// country not in the log, or not in it on the spot's band. New calls and grids are left out: a cluster spot carries no
/// grid of the station, and nearly every spot would be a new call. Display only; it never feeds the ranking.
/// </summary>
public static class ClusterNeeds
{
    /// <summary>The needed spots, new countries first, then newest first.</summary>
    /// <param name="spots">The current spots.</param>
    /// <param name="log">The log.</param>
    /// <param name="countries">The country file.</param>
    /// <param name="mode">The mode being worked.</param>
    /// <param name="table">The frequency table, for the mode's dial frequencies.</param>
    /// <param name="nowUtc">Now.</param>
    /// <param name="confirmedOnly">Count a country as worked only when confirmed (the ranking setting).</param>
    public static IReadOnlyList<NeededSpot> Find(ClusterSpots spots, LogIndex log, CountryFile countries, Mode mode, FrequencyTable table,
                                                 DateTime nowUtc, bool confirmedOnly = false)
    {
        var list = new List<NeededSpot>();
        foreach (var (spot, band) in spots.All(nowUtc))
        {
            if (!ClusterModeFilter.Matches(spot, mode, table) || countries.Lookup(spot.DxCall)?.Entity is not { } entity) continue;
            NeedTag? need = !log.EntityWorked(entity.Key, confirmedOnly) ? NeedTag.NewCountry
                : !log.EntityWorkedOnBand(entity.Key, band, confirmedOnly) ? NeedTag.NewBand
                : null;
            if (need is { } n) list.Add(new NeededSpot(spot, band, entity, n));
        }
        return [.. list.OrderBy(n => n.Need == NeedTag.NewCountry ? 0 : 1).ThenByDescending(n => n.Spot.TimeUtc)];
    }
}
