// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>A WSPR signal as the WSPR screen lists it: the spot and its distance from my locator.</summary>
/// <param name="Spot">The signal heard.</param>
/// <param name="DistanceKm">Great-circle distance from my locator in kilometres, or null when either locator is unknown.</param>
public sealed record WsprHeardSpot(WsprSpot Spot, double? DistanceKm);
