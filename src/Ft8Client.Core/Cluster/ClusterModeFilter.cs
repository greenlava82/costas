// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using Ft8Client.Core.Bands;

namespace Ft8Client.Core.Cluster;

/// <summary>
/// Decides whether a DX cluster spot is for the mode being worked (docs/05-integrations.md 5b). Cluster spots carry no
/// mode field, so the comment is read first: a spot whose comment names a mode (FT8, FT4, PSK31, PSK63, RTTY, or
/// another such as CW or SSB) is that mode. A spot whose comment names none is judged by its frequency: FT8, FT4, PSK31
/// and PSK63 spots must fall within 500 Hz below to 3.5 kHz above one of that mode's dial frequencies in the frequency
/// table. RTTY shares its segments with too much else, so an RTTY spot must say RTTY.
/// </summary>
public static class ClusterModeFilter
{
    /// <summary>How far below a dial frequency a spot may be and still count (a dial rounded down, a slightly off VFO).</summary>
    public const long BelowDialHz = 500;

    /// <summary>How far above a dial frequency a spot may be: the audio passband (skimmers spot dial plus offset).</summary>
    public const long AboveDialHz = 3_500;

    private const string Psk = "PSK";
    private const string Other = "OTHER";

    private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FT8"] = "FT8", ["FT4"] = "FT4", ["RTTY"] = "RTTY",
        ["PSK31"] = "PSK31", ["BPSK31"] = "PSK31", ["PSK63"] = "PSK63", ["BPSK63"] = "PSK63", ["PSK"] = Psk, ["BPSK"] = Psk,
        ["CW"] = Other, ["SSB"] = Other, ["USB"] = Other, ["LSB"] = Other, ["AM"] = Other, ["FM"] = Other, ["PHONE"] = Other,
        ["SSTV"] = Other, ["JT65"] = Other, ["JT9"] = Other, ["JS8"] = Other, ["MSK144"] = Other, ["Q65"] = Other,
        ["WSPR"] = Other, ["FST4"] = Other, ["OLIVIA"] = Other, ["VARAC"] = Other, ["MFSK"] = Other, ["CONTESTIA"] = Other,
    };

    /// <summary>
    /// The mode a comment names: <c>FT8</c>, <c>FT4</c>, <c>PSK31</c>, <c>PSK63</c>, <c>RTTY</c>, <c>PSK</c> (PSK without a
    /// speed), <c>OTHER</c> for a mode Costas does not work, or null when it names none. The first mode word wins.
    /// </summary>
    public static string? ModeNamed(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return null;
        foreach (var word in comment.Split([' ', ',', ';', ':', '/', '(', ')', '[', ']', '!', '.', '-'], StringSplitOptions.RemoveEmptyEntries))
            if (Words.TryGetValue(word, out var mode)) return mode;
        return null;
    }

    /// <summary>True when the spot is for <paramref name="mode"/> by the rules above.</summary>
    public static bool Matches(ClusterSpot spot, Mode mode, FrequencyTable table)
    {
        var want = ModeName(mode);
        var named = ModeNamed(spot.Comment);
        if (named is not null)
            return named == want || (named == Psk && mode is Mode.Psk31 or Mode.Psk63);
        if (mode == Mode.Rtty) return false;
        foreach (var e in table.Entries)
            if (e.Mode == mode && spot.FrequencyHz >= e.DialHz - BelowDialHz && spot.FrequencyHz <= e.DialHz + AboveDialHz)
                return true;
        return false;
    }

    /// <summary>The name a cluster comment uses for a mode: <c>FT8</c>, <c>FT4</c>, <c>PSK31</c>, <c>PSK63</c>, <c>RTTY</c>.</summary>
    public static string ModeName(Mode mode) => mode switch
    {
        Mode.Ft8 => "FT8",
        Mode.Ft4 => "FT4",
        Mode.Psk31 => "PSK31",
        Mode.Psk63 => "PSK63",
        _ => "RTTY",
    };
}
