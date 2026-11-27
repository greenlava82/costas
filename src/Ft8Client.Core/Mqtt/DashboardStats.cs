// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>Logbook statistics for a Home Assistant dashboard, all counted from the log. Built by <see cref="DashboardStatsBuilder"/>.</summary>
/// <param name="TodayUtc">The UTC day "today" means.</param>
/// <param name="QsosToday">Contacts this UTC day.</param>
/// <param name="QsosWeek">Contacts over the last 7 UTC days, today included.</param>
/// <param name="QsosTotal">Contacts in the logbook.</param>
/// <param name="DxccWorkedTotal">DXCC entities worked, all time.</param>
/// <param name="NewDxccToday">Entities first worked today, oldest first.</param>
/// <param name="FarthestToday">Today's farthest contact with a known distance, or null.</param>
/// <param name="Recent">The latest contacts, newest first.</param>
/// <param name="Days">The last 7 UTC days, oldest first.</param>
/// <param name="GridsWeek">Unique four-character grids worked this week.</param>
/// <param name="MapPoints">Stations worked this week with a known position, newest first.</param>
public sealed record DashboardStats(DateTime TodayUtc, int QsosToday, int QsosWeek, int QsosTotal, int DxccWorkedTotal,
                                    IReadOnlyList<NewEntity> NewDxccToday, StatsContact? FarthestToday, IReadOnlyList<StatsContact> Recent,
                                    IReadOnlyList<DayCount> Days, int GridsWeek, IReadOnlyList<MapPoint> MapPoints)
{
    /// <summary>The row id of each entity's first contact, by entity key (not published; marks a new entity in an event).</summary>
    public IReadOnlyDictionary<string, long> FirstContactIds { get; init; } = new Dictionary<string, long>(StringComparer.Ordinal);
}
