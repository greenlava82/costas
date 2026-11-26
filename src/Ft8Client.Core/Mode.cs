// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core;

/// <summary>
/// The digital modes the app knows. FT8 and FT4 run in fixed time slots; the keyboard modes (PSK31, PSK63, RTTY) run
/// continuously (docs/09-keyboard-modes.md); WSPR is a slotted beacon mode (docs/10-wspr.md). New values go at the
/// end so existing ones keep their numbers.
/// </summary>
public enum Mode
{
    /// <summary>FT8: 15 s slots, 8-tone GFSK.</summary>
    Ft8,

    /// <summary>FT4: 7.5 s slots, 4-tone GFSK.</summary>
    Ft4,

    /// <summary>PSK31: BPSK at 31.25 Bd with Varicode, continuous.</summary>
    Psk31,

    /// <summary>PSK63: BPSK at 62.5 Bd with Varicode, continuous.</summary>
    Psk63,

    /// <summary>RTTY: 45.45 Bd Baudot, 170 Hz shift by default, continuous.</summary>
    Rtty,

    /// <summary>
    /// WSPR-2: a beacon mode with 120 s slots on even UTC minutes, 4-FSK at 12000/8192 Hz spacing (docs/10-wspr.md).
    /// Decoded by <c>wsprd</c>, not <c>jt9</c>; it has slots but does not run the FT8/FT4 pipeline. Not operable yet.
    /// </summary>
    Wspr,
}
