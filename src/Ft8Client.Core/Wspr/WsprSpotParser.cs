// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;

namespace Ft8Client.Core.Wspr;

/// <summary>
/// Reads the lines <c>wsprd</c> prints for each decode, e.g. <c>0918  -9  1.1  14.097046  0  ND6P DM04 30</c>:
/// time, SNR, DT, RF frequency in MHz, drift, then the message (docs/08-open-questions.md, V-WSPR2). Messages are
/// type 1 (<c>CALL GRID4 DBM</c>), type 2 (<c>CALL DBM</c>) and type 3 (<c>&lt;CALL&gt; GRID6 DBM</c>).
/// </summary>
public static class WsprSpotParser
{
    /// <summary>The line <c>wsprd</c> prints after its last decode.</summary>
    public const string FinishedMarker = "<DecodeFinished>";

    /// <summary>
    /// Parses one output line. Returns false, never throws, for anything that is not a decode: the finished marker, blank
    /// lines, a frequency or power that does not parse, a grid that is not a locator.
    /// </summary>
    /// <param name="line">The line, with or without its line ending.</param>
    /// <param name="slotStartUtc">Start of the slot the line belongs to.</param>
    /// <param name="dialHz">Dial frequency the slot was recorded at.</param>
    /// <param name="spot">The parsed signal.</param>
    public static bool TryParse(string? line, DateTime slotStartUtc, long dialHz, out WsprSpot spot)
    {
        spot = null!;
        if (string.IsNullOrWhiteSpace(line)) return false;
        var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (f.Length is < 7 or > 8) return false;
        var inv = CultureInfo.InvariantCulture;

        if (f[0].Length != 4 || !f[0].All(char.IsAsciiDigit)) return false;
        if (!int.TryParse(f[1], NumberStyles.AllowLeadingSign, inv, out var snr)) return false;
        if (!double.TryParse(f[2], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, inv, out var dt)) return false;
        if (!decimal.TryParse(f[3], NumberStyles.AllowDecimalPoint, inv, out var mhz) || mhz <= 0) return false;
        if (!int.TryParse(f[4], NumberStyles.AllowLeadingSign, inv, out var drift)) return false;
        if (!int.TryParse(f[^1], NumberStyles.None, inv, out var dbm) || dbm is < 0 or > 60) return false;

        var hz = (long)decimal.Round(mhz * 1_000_000m);
        var first = f[5];
        string call;
        string? grid = null;
        var hashed = false;
        if (first.StartsWith('<') && first.EndsWith('>') && first.Length > 2)
        {
            // Type 3: <CALL> GRID6 DBM, where an unresolved hash prints as <...>.
            if (f.Length != 8 || !IsGrid6(f[6])) return false;
            hashed = true;
            var inner = first[1..^1];
            if (inner == "...") call = WsprSpot.UnresolvedCall;
            else if (!IsCall(inner)) return false;
            else call = inner.ToUpperInvariant();
            grid = f[6].ToUpperInvariant();
        }
        else if (f.Length == 8)
        {
            // Type 1: CALL GRID4 DBM.
            if (!IsCall(first) || !IsGrid4(f[6].ToUpperInvariant())) return false;
            call = first.ToUpperInvariant();
            grid = f[6].ToUpperInvariant();
        }
        else
        {
            // Type 2: CALL DBM, the call carrying a prefix or suffix.
            if (!IsCall(first)) return false;
            call = first.ToUpperInvariant();
        }

        spot = new WsprSpot(slotStartUtc, snr, dt, hz, drift, call, grid, dbm, hashed, dialHz);
        return true;
    }

    // Locators are checked here, not with Grid, whose 4-character check leaves out RR73 (a real square on the WSPR map).
    private static bool IsGrid4(string s) =>
        s.Length == 4 && s[0] is >= 'A' and <= 'R' && s[1] is >= 'A' and <= 'R' && char.IsAsciiDigit(s[2]) && char.IsAsciiDigit(s[3]);

    private static bool IsGrid6(string s) =>
        s.Length == 6 && IsGrid4(s[..4].ToUpperInvariant()) && char.ToUpperInvariant(s[4]) is >= 'A' and <= 'X' && char.ToUpperInvariant(s[5]) is >= 'A' and <= 'X';

    // A callsign as WSPR sends it: letters, digits and a slash, with at least one digit and 3 to 12 characters.
    private static bool IsCall(string s) =>
        s.Length is >= 3 and <= 12 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '/') && s.Any(char.IsAsciiDigit);
}
