// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>A contact as published in recent_qsos and the qso_logged and new_dxcc events.</summary>
/// <param name="Call">The other station's callsign.</param>
/// <param name="Band">Band, such as "20m".</param>
/// <param name="Mode">Mode, such as "FT8".</param>
/// <param name="FrequencyHz">Frequency in hertz, or null.</param>
/// <param name="TimeUtc">Time off (time on when no time off was logged).</param>
/// <param name="Country">DXCC entity name, or null when unknown.</param>
/// <param name="Dxcc">ADIF DXCC code, or null when unknown.</param>
/// <param name="StateOrRegion">US state or Canadian province, or null.</param>
/// <param name="Grid">The other station's grid, or null.</param>
/// <param name="Lat">Latitude of the grid (else of the entity), or null.</param>
/// <param name="Lon">Longitude of the grid (else of the entity), or null.</param>
/// <param name="DistanceKm">Great-circle distance from my grid in kilometres, or null.</param>
/// <param name="IsNewDxcc">True when this was the first contact ever with the entity.</param>
/// <param name="RstSent">Report sent.</param>
/// <param name="RstRcvd">Report received.</param>
/// <param name="QrzSynced">True when the contact is in the QRZ logbook.</param>
/// <param name="EntityKey">Country-file entity key (not published; used to group by entity).</param>
public sealed record StatsContact(string Call, string Band, string Mode, long? FrequencyHz, DateTime TimeUtc, string? Country, int? Dxcc,
                                  string? StateOrRegion, string? Grid, double? Lat, double? Lon, int? DistanceKm, bool IsNewDxcc,
                                  string? RstSent, string? RstRcvd, bool QrzSynced, string? EntityKey = null);
