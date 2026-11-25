// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>The slow-changing station state published, retained, to the state topic.</summary>
/// <param name="Callsign">The active station callsign (it can differ from the station id when a profile changes).</param>
/// <param name="RigConnected">The radio answers CAT.</param>
/// <param name="Activity">What the station is doing.</param>
/// <param name="Band">Band name, such as "20m".</param>
/// <param name="FrequencyHz">Dial frequency in hertz.</param>
/// <param name="Mode">Mode name, such as "FT8".</param>
public sealed record StationState(string Callsign, bool RigConnected, StationActivity Activity, string Band, long FrequencyHz, string Mode);
