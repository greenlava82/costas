// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using Ft8Client.Core.Geo;
using Ft8Client.Core.Logbook;

namespace Ft8Client.Core.Cluster;

/// <summary>A DX cluster spot of a station I need, with what makes it needed.</summary>
/// <param name="Spot">The spot.</param>
/// <param name="Band">Its band, ADIF name (<c>15m</c>).</param>
/// <param name="Entity">The station's entity from the country file.</param>
/// <param name="Need"><see cref="NeedTag.NewCountry"/> or <see cref="NeedTag.NewBand"/>.</param>
public sealed record NeededSpot(ClusterSpot Spot, string Band, Entity Entity, NeedTag Need);
