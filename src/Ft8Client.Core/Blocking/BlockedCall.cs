// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Bands;
using Ft8Client.Core.Messages;

namespace Ft8Client.Core.Blocking;

/// <summary>
/// One entry on the block list: a call that Costas hides and never answers, on one band or every band, in one mode or
/// every mode. Portable forms of the call (<c>ON4XYZ/P</c>) are blocked with it.
/// </summary>
/// <param name="Call">The call, upper case, without portable suffixes.</param>
/// <param name="Band">ADIF band in lower case ("40m"), or null for every band.</param>
/// <param name="Mode">The mode, or null for every mode.</param>
public sealed record BlockedCall(string Call, string? Band, Mode? Mode)
{
    /// <summary>
    /// Makes an entry from what the operator typed. Returns null and says why when the call is not a callsign.
    /// </summary>
    /// <param name="text">The call as typed.</param>
    /// <param name="band">ADIF band ("40m" or "40 m"), or null or blank for every band.</param>
    /// <param name="mode">The mode, or null for every mode.</param>
    /// <param name="error">Why it was refused.</param>
    public static BlockedCall? Create(string? text, string? band, Mode? mode, out string error)
    {
        error = string.Empty;
        var t = (text ?? string.Empty).Trim().Trim('<', '>');
        var call = Callsign.Normalize(t);
        if (!Callsign.IsValid(call))
        {
            error = t.Length == 0 ? "Type a callsign." : $"{t} is not a callsign.";
            return null;
        }
        return new BlockedCall(Callsign.BaseCall(call), NormalizeBand(band), mode);
    }

    /// <summary>True when this entry blocks <paramref name="call"/> on <paramref name="band"/> in <paramref name="mode"/>.</summary>
    public bool Blocks(string call, string band, Mode mode) =>
        (Band is null || string.Equals(Band, NormalizeBand(band), StringComparison.Ordinal))
        && (Mode is null || Mode == mode)
        && string.Equals(Callsign.BaseCall(Callsign.Normalize(call.Trim('<', '>'))), Call, StringComparison.Ordinal);

    /// <summary>"ON4XYZ on 20 m in FT8", "ON4XYZ on every band".</summary>
    public string Describe()
    {
        var where = Band is null ? "every band" : BandPlan.Display(Band);
        var how = Mode is { } m ? $" in {ModeInfo.Name(m)}" : string.Empty;
        return $"{Call} on {where}{how}";
    }

    private static string? NormalizeBand(string? band) =>
        string.IsNullOrWhiteSpace(band) ? null : band.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
