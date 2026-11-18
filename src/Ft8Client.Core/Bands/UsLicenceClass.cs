// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Bands;

/// <summary>
/// The US licence class whose RTTY and data privileges the transmit guard enforces (<see cref="UsDataSegments"/>), or
/// <see cref="NotUs"/> to check only the band limits in the frequency table.
/// </summary>
public enum UsLicenceClass
{
    /// <summary>Amateur Extra: the widest data segments, and the default.</summary>
    AmateurExtra,

    /// <summary>Advanced: the Extra data segments less the bottom 25 kHz of 80, 40, 20 and 15 m.</summary>
    Advanced,

    /// <summary>General: the same data segments as Advanced.</summary>
    General,

    /// <summary>Technician: data on HF only on 28.000-28.300 MHz, plus 6 m and up.</summary>
    Technician,

    /// <summary>Not licensed in the US, or the operator does not want the check: only the band limits apply.</summary>
    NotUs,
}
