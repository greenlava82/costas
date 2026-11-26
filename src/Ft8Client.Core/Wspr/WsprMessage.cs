// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;

namespace Ft8Client.Core.Wspr;

/// <summary>The three WSPR message layouts.</summary>
public enum WsprMessageType
{
    /// <summary>Callsign, 4-character grid and power: <c>K1ABC FN42 37</c>.</summary>
    Type1 = 1,

    /// <summary>Callsign with a prefix or suffix, and power, no grid: <c>PJ4/K1ABC 37</c>.</summary>
    Type2 = 2,

    /// <summary>A hash of the callsign, a 6-character grid and power: <c>&lt;PJ4/K1ABC&gt; FK52UD 37</c>.</summary>
    Type3 = 3,
}

/// <summary>
/// One WSPR message as the operator would read it. The text is exactly what is packed, or the message is refused
/// (<see cref="WsprPacker"/>); nothing is adjusted on the way.
/// </summary>
/// <param name="Type">Which layout.</param>
/// <param name="Callsign">The callsign, upper case. For type 3 it is the call the hash stands for.</param>
/// <param name="Grid">The locator: 4 characters for type 1, 6 for type 3, null for type 2.</param>
/// <param name="Dbm">Power in dBm.</param>
public sealed record WsprMessage(WsprMessageType Type, string Callsign, string? Grid, int Dbm)
{
    /// <summary>A type 1 message.</summary>
    public static WsprMessage Standard(string callsign, string grid4, int dbm) => new(WsprMessageType.Type1, callsign, grid4, dbm);

    /// <summary>A type 2 message (a callsign with a prefix or suffix).</summary>
    public static WsprMessage Compound(string callsign, int dbm) => new(WsprMessageType.Type2, callsign, null, dbm);

    /// <summary>A type 3 message (a hashed callsign and a 6-character grid).</summary>
    public static WsprMessage Hashed(string callsign, string grid6, int dbm) => new(WsprMessageType.Type3, callsign, grid6, dbm);

    /// <summary>The message as WSJT-X and <c>wsprd</c> write it.</summary>
    public string Text => Type switch
    {
        WsprMessageType.Type1 => string.Create(CultureInfo.InvariantCulture, $"{Callsign} {Grid} {Dbm}"),
        WsprMessageType.Type2 => string.Create(CultureInfo.InvariantCulture, $"{Callsign} {Dbm}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"<{Callsign}> {Grid} {Dbm}"),
    };

    /// <summary>
    /// Reads a message from its text (<c>K1ABC FN42 37</c>, <c>PJ4/K1ABC 37</c>, <c>&lt;PJ4/K1ABC&gt; FK52UD 37</c>). Only the
    /// shape is read here; whether it can be sent is for <see cref="WsprPacker"/>.
    /// </summary>
    public static bool TryParse(string? text, out WsprMessage message)
    {
        message = null!;
        var f = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length is < 2 or > 3 || !int.TryParse(f[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var dbm)) return false;
        if (f[0].StartsWith('<'))
        {
            if (f.Length != 3 || !f[0].EndsWith('>') || f[0].Length < 3) return false;
            message = Hashed(f[0][1..^1], f[1], dbm);
        }
        else if (f.Length == 3) message = Standard(f[0], f[1], dbm);
        else if (f[0].Contains('/')) message = Compound(f[0], dbm);
        else return false;
        return true;
    }
}
