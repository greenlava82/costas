// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>A DXCC entity worked today for the first time ever.</summary>
/// <param name="Dxcc">ADIF DXCC code, or null when the code is not known.</param>
/// <param name="Country">Entity name.</param>
/// <param name="Call">The station that gave the first contact.</param>
/// <param name="TimeUtc">When.</param>
public sealed record NewEntity(int? Dxcc, string Country, string Call, DateTime TimeUtc);
