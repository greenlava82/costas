// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>What <see cref="DashboardStatsBuilder"/> counts from, all read from the log.</summary>
/// <param name="NowUtc">Now.</param>
/// <param name="MyGrid">My grid, for distances; null or empty leaves distances out.</param>
/// <param name="QsosTotal">Contacts in the logbook.</param>
/// <param name="Week">Every contact with time on from the start of the UTC day six days ago.</param>
/// <param name="Recent">The latest contacts, newest first (the first ten are published).</param>
/// <param name="Firsts">
/// At least the earliest contact for each entity: one row per call and logged entity is enough (only Id, Call, times,
/// Dxcc and EntityKey are read).
/// </param>
public sealed record DashboardInput(DateTime NowUtc, string? MyGrid, int QsosTotal, IReadOnlyList<StatsQso> Week, IReadOnlyList<StatsQso> Recent,
                                    IReadOnlyList<StatsQso> Firsts);
