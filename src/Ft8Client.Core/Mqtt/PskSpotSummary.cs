// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>PSK Reporter spots of my signal over the last 15 minutes.</summary>
/// <param name="Count">Spots (PSK Reporter keeps one per receiver, band and 5 minutes).</param>
/// <param name="FarthestKm">Distance to the farthest receiver with a known grid, or null.</param>
/// <param name="FarthestReporter">That receiver's callsign, or null.</param>
/// <param name="FarthestGrid">That receiver's grid, or null.</param>
public sealed record PskSpotSummary(int Count, int? FarthestKm, string? FarthestReporter, string? FarthestGrid)
{
    /// <summary>No spots.</summary>
    public static PskSpotSummary None { get; } = new(0, null, null, null);
}
