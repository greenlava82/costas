// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>A station worked this week, for the worked map: its latest contact.</summary>
/// <param name="Call">Callsign.</param>
/// <param name="Lat">Latitude (grid centre, else the entity's).</param>
/// <param name="Lon">Longitude.</param>
/// <param name="Country">Entity name, or null.</param>
/// <param name="Band">Band of the latest contact.</param>
/// <param name="TimeUtc">Time of the latest contact.</param>
public sealed record MapPoint(string Call, double Lat, double Lon, string? Country, string Band, DateTime TimeUtc);
