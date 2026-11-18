// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Log;

/// <summary>
/// A report of a station heard (<c>spot</c> table): a PSK Reporter or RBN report of my signal, or a DX cluster spot of
/// another station. For a cluster spot <see cref="MyCall"/> holds the station spotted and <see cref="RxCall"/> the spotter,
/// so "RxCall heard MyCall" reads the same for every source; readers of reports of my signal skip source <c>cluster</c>.
/// </summary>
public sealed class SpotRecord
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>My call; for a cluster spot, the station spotted.</summary>
    public string MyCall { get; set; } = string.Empty;

    /// <summary>Receiver call; for a cluster spot, the spotter.</summary>
    public string RxCall { get; set; } = string.Empty;

    /// <summary>Receiver grid.</summary>
    public string? RxGrid { get; set; }

    /// <summary>Receiver ADIF DXCC code.</summary>
    public int? RxDxcc { get; set; }

    /// <summary>Receiver entity key.</summary>
    public string? RxEntityKey { get; set; }

    /// <summary>Band.</summary>
    public string Band { get; set; } = string.Empty;

    /// <summary>Mode.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Frequency in hertz.</summary>
    public long? FreqHz { get; set; }

    /// <summary>Reported SNR.</summary>
    public int? Snr { get; set; }

    /// <summary>Report time.</summary>
    public DateTime TimeUtc { get; set; }

    /// <summary><c>mqtt</c> or <c>query</c> (PSK Reporter), <c>rbn</c> (Reverse Beacon Network) or <c>cluster</c> (DX cluster).</summary>
    public string Source { get; set; } = "mqtt";
}
