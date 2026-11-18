// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Neighbours;

/// <summary>The evidence for one continent in a band advice line.</summary>
/// <param name="Continent">Two-letter continent ("EU").</param>
/// <param name="Receivers">Distinct stations there that heard me (or stations near me) in the last 15 minutes.</param>
/// <param name="Senders">Distinct stations near me they heard; 1 when they heard me.</param>
/// <param name="LatestUtc">The newest report, or null when there was none in the last 15 minutes.</param>
/// <param name="EarlierReceivers">For a closing band, distinct stations there that heard stations near me 15 to 30 minutes ago.</param>
public sealed record BandAdviceContinent(string Continent, int Receivers, int Senders, DateTime? LatestUtc, int EarlierReceivers = 0);
