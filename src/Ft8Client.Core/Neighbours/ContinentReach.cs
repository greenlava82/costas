// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Neighbours;

/// <summary>
/// Who on one continent heard stations near me on one band (docs/04-domain-logic.md, section 14). Real counts, never
/// scaled up from samples, so band advice can tell a few loud reports from many listeners.
/// </summary>
/// <param name="Receivers">Distinct stations on the continent that heard a station near me.</param>
/// <param name="Senders">Distinct stations near me they heard.</param>
/// <param name="LatestUtc">The newest report.</param>
public sealed record ContinentReach(int Receivers, int Senders, DateTime LatestUtc);
