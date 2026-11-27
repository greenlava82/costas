// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Bands;
using Ft8Client.Core.Geo;
using Ft8Client.Core.Messages;

namespace Ft8Client.Core.Mqtt;

/// <summary>
/// Counts the dashboard statistics from the log. "Today" is the current UTC day and "week" the last 7 UTC days, today
/// included. A contact's entity is the one logged with it (by entity key, else ADIF DXCC code), else the country file's
/// match for its call; entities the country file does not list are not counted, as on the Awards page.
/// </summary>
public sealed class DashboardStatsBuilder
{
    /// <summary>Days in the rolling week.</summary>
    public const int WeekDays = 7;

    /// <summary>Contacts in recent_qsos.</summary>
    public const int RecentCount = 10;

    private readonly CountryFile _countries;
    private readonly IReadOnlyDictionary<int, string> _keyByAdif;
    private readonly Dictionary<string, int> _adifByKey = new(StringComparer.Ordinal);

    /// <summary>Creates a builder.</summary>
    /// <param name="countries">The country file.</param>
    /// <param name="keyByAdif">ADIF DXCC code to entity key.</param>
    public DashboardStatsBuilder(CountryFile countries, IReadOnlyDictionary<int, string> keyByAdif)
    {
        ArgumentNullException.ThrowIfNull(countries);
        ArgumentNullException.ThrowIfNull(keyByAdif);
        _countries = countries;
        _keyByAdif = keyByAdif;
        foreach (var (adif, key) in keyByAdif) _adifByKey.TryAdd(key, adif);
    }

    /// <summary>The statistics for <paramref name="input"/>.</summary>
    public DashboardStats Build(DashboardInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var today = DateTime.SpecifyKind(input.NowUtc.ToUniversalTime().Date, DateTimeKind.Utc);
        var tomorrow = today.AddDays(1);
        var weekStart = today.AddDays(-(WeekDays - 1));

        // The first contact with each entity: earliest time on, then lowest row id.
        var first = new Dictionary<string, StatsQso>(StringComparer.Ordinal);
        foreach (var q in input.Firsts)
        {
            if (EntityKeyOf(q) is not { } key) continue;
            if (!first.TryGetValue(key, out var f) || q.TimeOnUtc < f.TimeOnUtc || (q.TimeOnUtc == f.TimeOnUtc && q.Id < f.Id)) first[key] = q;
        }
        var firstIds = first.ToDictionary(p => p.Key, p => p.Value.Id, StringComparer.Ordinal);

        var week = input.Week.Where(q => q.TimeOnUtc >= weekStart && q.TimeOnUtc < tomorrow).ToList();
        var todays = week.Where(q => q.TimeOnUtc >= today).ToList();

        var newToday = first
            .Where(p => p.Value.TimeOnUtc >= today && p.Value.TimeOnUtc < tomorrow)
            .Select(p => new NewEntity(_adifByKey.TryGetValue(p.Key, out var a) ? a : p.Value.Dxcc, _countries.FindByKey(p.Key)!.Name, p.Value.Call,
                p.Value.TimeUtc))
            .OrderBy(e => e.TimeUtc).ThenBy(e => e.Call, StringComparer.Ordinal)
            .ToList();

        var farthest = todays.Select(q => Describe(q, input.MyGrid, firstIds))
            .Where(c => c.DistanceKm is not null)
            .OrderByDescending(c => c.DistanceKm).ThenBy(c => c.TimeUtc)
            .FirstOrDefault();

        var recent = input.Recent.OrderByDescending(q => q.TimeOnUtc).ThenByDescending(q => q.Id).Take(RecentCount)
            .Select(q => Describe(q, input.MyGrid, firstIds)).ToList();

        var bands = week.Select(q => q.Band.ToLowerInvariant()).Where(b => b.Length > 0).Distinct(StringComparer.Ordinal)
            .OrderBy(BandOrder).ThenBy(b => b, StringComparer.Ordinal).ToList();
        var days = new List<DayCount>(WeekDays);
        for (var d = weekStart; d < tomorrow; d = d.AddDays(1))
        {
            var end = d.AddDays(1);
            var on = week.Where(q => q.TimeOnUtc >= d && q.TimeOnUtc < end).ToList();
            var byBand = bands.Select(b => new KeyValuePair<string, int>(b, on.Count(q => string.Equals(q.Band, b, StringComparison.OrdinalIgnoreCase)))).ToList();
            days.Add(new DayCount(d, on.Count, byBand));
        }

        var grids = week.Select(q => Grid.Grid4(q.Grid)).OfType<string>().Distinct(StringComparer.Ordinal).Count();
        var points = week
            .GroupBy(q => q.Call.ToUpperInvariant(), StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(q => q.TimeOnUtc).ThenByDescending(q => q.Id).First())
            .Select(q => Describe(q, input.MyGrid, firstIds))
            .Where(c => c.Lat is not null && c.Lon is not null)
            .OrderByDescending(c => c.TimeUtc).ThenBy(c => c.Call, StringComparer.Ordinal)
            .Select(c => new MapPoint(c.Call, c.Lat!.Value, c.Lon!.Value, c.Country, c.Band, c.TimeUtc))
            .ToList();

        return new DashboardStats(today, todays.Count, week.Count, input.QsosTotal, first.Count, newToday, farthest, recent, days, grids, points)
        {
            FirstContactIds = firstIds,
        };
    }

    /// <summary>
    /// The published form of <paramref name="q"/>: entity, position and distance filled in. It is a new entity when
    /// <paramref name="firstIds"/> (from <see cref="DashboardStats.FirstContactIds"/>) names it as the entity's first contact.
    /// </summary>
    public StatsContact Describe(StatsQso q, string? myGrid, IReadOnlyDictionary<string, long> firstIds)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(firstIds);
        var key = EntityKeyOf(q);
        var entity = _countries.FindByKey(key);
        LatLon? pos = Grid.ToLatLon(q.Grid);
        if (pos is null && key is not null)
            pos = _countries.Lookup(q.Call) is { } m && m.Entity.Key == key ? m.Position : entity?.Position;
        int? km = pos is { } p && Grid.ToLatLon(myGrid) is { } me ? (int)Math.Round(GreatCircle.DistanceKm(me, p)) : null;
        int? dxcc = key is not null && _adifByKey.TryGetValue(key, out var a) ? a : q.Dxcc;
        return new StatsContact(q.Call, q.Band.ToLowerInvariant(), q.Mode, q.FrequencyHz, q.TimeUtc, entity?.Name ?? NullIfEmpty(q.Country), dxcc,
            NullIfEmpty(q.Region), NullIfEmpty(q.Grid), pos is { } la ? Math.Round(la.Lat, 2) : null, pos is { } lo ? Math.Round(lo.Lon, 2) : null, km,
            key is not null && firstIds.TryGetValue(key, out var id) && id == q.Id, NullIfEmpty(q.RstSent), NullIfEmpty(q.RstRcvd), q.QrzSynced, key);
    }

    /// <summary>The contact's entity key: as logged, else from its ADIF DXCC code, else the country file's match for the call.</summary>
    public string? EntityKeyOf(StatsQso q)
    {
        ArgumentNullException.ThrowIfNull(q);
        if (q.EntityKey is { Length: > 0 } k && _countries.FindByKey(k) is not null) return k;
        if (q.Dxcc is { } code && _keyByAdif.TryGetValue(code, out var byCode) && _countries.FindByKey(byCode) is not null) return byCode;
        return _countries.Lookup(q.Call)?.Entity.Key;
    }

    private static int BandOrder(string band)
    {
        for (var i = 0; i < BandPlan.Bands.Count; i++)
            if (BandPlan.Bands[i].Band == band) return i;
        return int.MaxValue;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
