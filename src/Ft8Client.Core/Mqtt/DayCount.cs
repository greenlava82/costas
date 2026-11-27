// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>Contacts on one UTC day.</summary>
/// <param name="Date">The UTC day (midnight, kind UTC).</param>
/// <param name="Total">All contacts that day.</param>
/// <param name="ByBand">Contacts per band, with every band worked in the week (zero when none that day), in band-plan order.</param>
public sealed record DayCount(DateTime Date, int Total, IReadOnlyList<KeyValuePair<string, int>> ByBand);
