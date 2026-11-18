// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Ranking;

namespace Ft8Client.Core.Neighbours;

/// <summary>What <see cref="BandAdvisor"/> judges from: the evidence Costas already holds about this station.</summary>
/// <param name="Openings">The band menu's openings (<see cref="BandOpenings.Shown"/>); empty when the feed or setting is off.</param>
/// <param name="NeighbourReports">Reports of stations near me on any band, kept for the last 30 minutes, for the closing line.</param>
/// <param name="MyReports">PSK Reporter and RBN reports of my own signal (any band; only the band I am on is used).</param>
/// <param name="Band">ADIF band I am on.</param>
/// <param name="Mode">The mode I am in, as PSK Reporter names it (<c>FT8</c>).</param>
/// <param name="MyCall">My call.</param>
/// <param name="MyGrid">My grid; without a valid one there is no advice.</param>
/// <param name="MyContinent">My continent from the country file; that continent is never advised. Null when unknown.</param>
/// <param name="CurrentBandFollowedSinceUtc">
/// Since when the band I am on has been followed without a break; null when it is not followed all the time. The closing line
/// needs 30 minutes of it.
/// </param>
/// <param name="NowUtc">Now.</param>
public sealed record BandAdviceInput(IReadOnlyDictionary<string, BandOpening> Openings, IReadOnlyList<NeighbourReport> NeighbourReports,
                                     IReadOnlyList<ReceptionReport> MyReports, string Band, string Mode, string MyCall, string? MyGrid,
                                     string? MyContinent, DateTime? CurrentBandFollowedSinceUtc, DateTime NowUtc);
