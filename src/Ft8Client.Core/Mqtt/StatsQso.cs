// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>A logbook contact as the dashboard statistics read it. Times are UTC.</summary>
/// <param name="Id">Row id in the log; the earlier row wins a tie on time.</param>
/// <param name="Call">The other station's callsign.</param>
/// <param name="TimeOnUtc">Time on: which UTC day the contact counts for.</param>
/// <param name="TimeOffUtc">Time off, when logged; published as the contact's time.</param>
/// <param name="Band">ADIF band, such as "20m".</param>
/// <param name="Mode">Mode name, such as "FT8" or "FT4" (the ADIF submode when there is one).</param>
/// <param name="FrequencyHz">Frequency in hertz, when logged.</param>
/// <param name="Country">Country name as logged, used when the country file does not know the entity.</param>
/// <param name="Dxcc">ADIF DXCC code as logged.</param>
/// <param name="EntityKey">Country-file entity key as logged.</param>
/// <param name="Region">US state or Canadian province.</param>
/// <param name="Grid">The other station's grid.</param>
/// <param name="RstSent">Report sent.</param>
/// <param name="RstRcvd">Report received.</param>
/// <param name="QrzSynced">True when the contact is in the QRZ logbook.</param>
public sealed record StatsQso(long Id, string Call, DateTime TimeOnUtc, DateTime? TimeOffUtc = null, string Band = "", string Mode = "",
                              long? FrequencyHz = null, string? Country = null, int? Dxcc = null, string? EntityKey = null,
                              string? Region = null, string? Grid = null, string? RstSent = null, string? RstRcvd = null,
                              bool QrzSynced = false)
{
    /// <summary>The time published for the contact: time off when logged, else time on.</summary>
    public DateTime TimeUtc => TimeOffUtc ?? TimeOnUtc;
}
