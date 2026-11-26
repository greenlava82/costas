// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.App.Remote;

/// <summary>One signal in the phone's WSPR list.</summary>
/// <param name="Utc">Slot time, "14:02".</param>
/// <param name="Call">Callsign.</param>
/// <param name="Grid">Locator, or blank.</param>
/// <param name="Snr">SNR, "-12".</param>
/// <param name="Distance">Distance in the operator's unit, or blank.</param>
public sealed record RemoteWsprSpot(string Utc, string Call, string Grid, string Snr, string Distance);

/// <summary>The WSPR card on the phone (docs/10-wspr.md section 8): the beacon's state, the next slot, and what was heard.</summary>
/// <param name="On">The beacon is running.</param>
/// <param name="Status">"Beacon off. Receiving.", "Next slot transmits.", "Transmitting W7LIT DN40 37" and so on.</param>
/// <param name="NextSlotIn">Seconds to the next 2-minute slot.</param>
/// <param name="PresenceLeft">Seconds before the beacon stops without input, or null when it is off.</param>
/// <param name="Power">"Reports 37 dBm (5 W)." and the estimate.</param>
/// <param name="PowerWarning">The 3 dB warning, or null.</param>
/// <param name="Decoder">What the decoder is doing.</param>
/// <param name="Upload">The WSPRnet upload state.</param>
/// <param name="Heard">The newest signals heard.</param>
public sealed record RemoteWspr(bool On, string Status, int NextSlotIn, int? PresenceLeft, string Power, string? PowerWarning, string Decoder, string Upload,
                                IReadOnlyList<RemoteWsprSpot> Heard);
