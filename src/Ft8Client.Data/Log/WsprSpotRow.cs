// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Wspr;

namespace Ft8Client.Data.Log;

/// <summary>Where a stored WSPR spot stands with the WSPRnet upload.</summary>
public enum WsprUploadState
{
    /// <summary>Not for upload (uploading was off when it was heard).</summary>
    None = 0,

    /// <summary>Waiting to be sent to WSPRnet.</summary>
    Pending = 1,

    /// <summary>WSPRnet accepted it.</summary>
    Uploaded = 2,

    /// <summary>WSPRnet refused it; it is not sent again.</summary>
    Failed = 3,
}

/// <summary>A WSPR spot as stored in the <c>wspr_spot</c> table.</summary>
public sealed class WsprSpotRow
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>Callsign of the station profile that heard it (the receiver, as WSPRnet reports it).</summary>
    public string ProfileCallsign { get; set; } = string.Empty;

    /// <summary>Start of the 2-minute slot (UTC).</summary>
    public DateTime SlotUtc { get; set; }

    /// <summary>Dial frequency in hertz.</summary>
    public long DialHz { get; set; }

    /// <summary>RF frequency of the signal in hertz.</summary>
    public long FreqHz { get; set; }

    /// <summary>SNR in dB.</summary>
    public int Snr { get; set; }

    /// <summary>Time offset in seconds.</summary>
    public double Dt { get; set; }

    /// <summary>Drift in Hz per minute.</summary>
    public int Drift { get; set; }

    /// <summary>Sender's callsign (<see cref="WsprSpot.UnresolvedCall"/> for a hash that did not resolve).</summary>
    public string Callsign { get; set; } = string.Empty;

    /// <summary>Sender's locator, or null.</summary>
    public string? Grid { get; set; }

    /// <summary>Reported power in dBm.</summary>
    public int Dbm { get; set; }

    /// <summary>True for a type 3 message.</summary>
    public bool IsHashed { get; set; }

    /// <summary>Distance from my locator in kilometres, or null when either locator is unknown.</summary>
    public double? DistanceKm { get; set; }

    /// <summary>Upload state.</summary>
    public WsprUploadState UploadState { get; set; }

    /// <summary>Builds a row from a decoded spot.</summary>
    public static WsprSpotRow From(WsprSpot s, string profileCallsign, double? distanceKm, WsprUploadState state) => new()
    {
        ProfileCallsign = profileCallsign,
        SlotUtc = s.SlotStartUtc,
        DialHz = s.DialHz,
        FreqHz = s.FrequencyHz,
        Snr = s.Snr,
        Dt = s.DtSeconds,
        Drift = s.DriftHz,
        Callsign = s.Callsign,
        Grid = s.Grid,
        Dbm = s.PowerDbm,
        IsHashed = s.IsHashed,
        DistanceKm = distanceKm,
        UploadState = state,
    };

    /// <summary>The decoded form of this row.</summary>
    public WsprSpot ToSpot() => new(SlotUtc, Snr, Dt, FreqHz, Drift, Callsign, Grid, Dbm, IsHashed, DialHz);
}
