// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>A logged contact, published as a qso_logged event.</summary>
/// <param name="Call">The other station's callsign.</param>
/// <param name="Band">Band name, such as "20m".</param>
/// <param name="Mode">Mode name, such as "FT8".</param>
/// <param name="FrequencyHz">Dial frequency in hertz.</param>
/// <param name="TimeOffUtc">When the contact ended.</param>
public sealed record LoggedQso(string Call, string Band, string Mode, long FrequencyHz, DateTime TimeOffUtc);
