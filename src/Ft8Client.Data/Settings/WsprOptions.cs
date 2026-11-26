// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Settings;

/// <summary>
/// WSPR settings for one station profile (docs/10-wspr.md). Nothing here ever stores the beacon as running: it starts
/// only from Start beacon and is off after every start (section 6.1).
/// </summary>
public sealed class WsprOptions
{
    /// <summary>Upload the signals I hear to WSPRnet. Off until the operator turns it on.</summary>
    public bool UploadEnabled { get; set; }

    /// <summary>Share of 2-minute slots the beacon transmits in, 1 to 100 (WSJT-X default 20).</summary>
    public int TxPercent { get; set; } = 20;

    /// <summary>
    /// The power in dBm sent in the beacon message. The operator picks it from the legal list; Costas never changes it.
    /// </summary>
    public int ReportedDbm { get; set; } = 37;

    /// <summary>Audio attenuation in dB applied on top of the RF power setting, 0 to -20.</summary>
    public int AttenuationDb { get; set; }

    /// <summary>Percent of the radio's maximum power set while the beacon runs (at least 1 W), 1 to 100.</summary>
    public int PowerPercent { get; set; } = 5;

    /// <summary>Audio frequency of the beacon's lowest tone in hertz, inside the 1400 to 1600 Hz window.</summary>
    public int OffsetHz { get; set; } = 1500;

    /// <summary>Prefer a type 1 message (4-character grid) even when the profile grid has 6 characters, as WSJT-X's option does.</summary>
    public bool PreferType1 { get; set; }

    /// <summary>Bands the beacon hops between, such as <c>20m</c>; empty means it stays on the current band.</summary>
    public List<string> HopBands { get; set; } = [];

    /// <summary>
    /// Hop on WSJT-X's coordinated schedule (the 2-minute slot within each 20 minutes picks the band from the 10
    /// coordinated bands) so hopping stations meet; off hops through <see cref="HopBands"/> in order.
    /// </summary>
    public bool HopCoordinated { get; set; }
}
