// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>
/// One WSPR signal heard in a slot, as <c>wsprd</c> reported it (docs/10-wspr.md section 4). A type 3 message names its
/// sender by a hash; <see cref="IsHashed"/> is true for it, and <see cref="Callsign"/> is then the call the hash table
/// resolved, or <see cref="UnresolvedCall"/> when it did not.
/// </summary>
/// <param name="SlotStartUtc">Start of the 2-minute slot the signal was heard in (UTC).</param>
/// <param name="Snr">Signal-to-noise ratio in dB, in a 2500 Hz bandwidth as <c>wsprd</c> reports it.</param>
/// <param name="DtSeconds">Time offset of the signal from the slot's nominal start, in seconds.</param>
/// <param name="FrequencyHz">RF frequency of the signal's lowest tone in hertz (<c>wsprd</c> prints MHz to 1 Hz).</param>
/// <param name="DriftHz">Frequency drift in hertz per minute.</param>
/// <param name="Callsign">Sender's callsign, upper case, without the angle brackets of a hashed call.</param>
/// <param name="Grid">Sender's locator (4 or 6 characters), or null for a type 2 message, which carries none.</param>
/// <param name="PowerDbm">Power the sender reported, in dBm.</param>
/// <param name="IsHashed">True for a type 3 message (the call was sent as a hash).</param>
/// <param name="DialHz">Dial frequency the slot was recorded at, in hertz.</param>
public sealed record WsprSpot(
    DateTime SlotStartUtc,
    int Snr,
    double DtSeconds,
    long FrequencyHz,
    int DriftHz,
    string Callsign,
    string? Grid,
    int PowerDbm,
    bool IsHashed,
    long DialHz)
{
    /// <summary>What <see cref="Callsign"/> holds for a hashed call the hash table could not resolve.</summary>
    public const string UnresolvedCall = "<...>";

    /// <summary>Audio frequency of the signal above the dial, in hertz.</summary>
    public int AudioHz => (int)(FrequencyHz - DialHz);

    /// <summary>True when the call is known (not an unresolved hash).</summary>
    public bool HasCall => Callsign != UnresolvedCall;
}
