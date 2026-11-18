// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;

namespace Ft8Client.Core.Bands;

/// <summary>
/// Where a US amateur may send RTTY and data (FT8, FT4, PSK, RTTY) on HF, 6 m and 2 m, per licence class. Each segment
/// is a 47 CFR 97.305(c) RTTY/data segment cut to the class's privileges in 97.301(b) to (e), with 97.307(f)(9) (a
/// Technician may send only CW in the 80, 40 and 15 m data segments). 60 m is from 97.303(h): the four 2.8 kHz channels
/// and the 5351.5-5366.5 kHz segment, General class and up. Read on eCFR, Title 47 up to date as of 2026-11-16
/// (docs/08-open-questions.md). 630 m and 2200 m are left out, as before: Costas has no rows for them.
/// </summary>
public static class UsDataSegments
{
    private static readonly (long LowHz, long HighHz)[] SixtyMetres =
    [
        (5_330_600, 5_333_400),
        (5_346_600, 5_349_400),
        (5_351_500, 5_366_500),
        (5_371_600, 5_374_400),
        (5_403_600, 5_406_400),
    ];

    private static readonly (long LowHz, long HighHz)[] VhfUp =
    [
        (50_100_000, 54_000_000),
        (144_100_000, 148_000_000),
    ];

    /// <summary>Amateur Extra: the whole of each 97.305(c) data segment.</summary>
    public static readonly IReadOnlyList<(long LowHz, long HighHz)> Extra =
    [
        (1_800_000, 2_000_000),
        (3_500_000, 3_600_000),
        .. SixtyMetres,
        (7_000_000, 7_125_000),
        (10_100_000, 10_150_000),
        (14_000_000, 14_150_000),
        (18_068_000, 18_110_000),
        (21_000_000, 21_200_000),
        (24_890_000, 24_930_000),
        (28_000_000, 28_300_000),
        .. VhfUp,
    ];

    /// <summary>
    /// General and Advanced: the Extra segments without 3.500-3.525, 7.000-7.025, 14.000-14.025 and 21.000-21.025 MHz.
    /// The two classes differ only in their phone segments.
    /// </summary>
    public static readonly IReadOnlyList<(long LowHz, long HighHz)> General =
    [
        (1_800_000, 2_000_000),
        (3_525_000, 3_600_000),
        .. SixtyMetres,
        (7_025_000, 7_125_000),
        (10_100_000, 10_150_000),
        (14_025_000, 14_150_000),
        (18_068_000, 18_110_000),
        (21_025_000, 21_200_000),
        (24_890_000, 24_930_000),
        (28_000_000, 28_300_000),
        .. VhfUp,
    ];

    /// <summary>Technician: 28.000-28.300 MHz, then 6 m and 2 m. No data on 160 to 12 m.</summary>
    public static readonly IReadOnlyList<(long LowHz, long HighHz)> Technician =
    [
        (28_000_000, 28_300_000),
        .. VhfUp,
    ];

    /// <summary>The Amateur Extra segments, lowest and highest transmitted frequency in hertz.</summary>
    public static IReadOnlyList<(long LowHz, long HighHz)> Segments => Extra;

    /// <summary>The data segments for a class; empty for <see cref="UsLicenceClass.NotUs"/>, which is not checked.</summary>
    public static IReadOnlyList<(long LowHz, long HighHz)> For(UsLicenceClass licence) => licence switch
    {
        UsLicenceClass.AmateurExtra => Extra,
        UsLicenceClass.Advanced or UsLicenceClass.General => General,
        UsLicenceClass.Technician => Technician,
        _ => [],
    };

    /// <summary>True when the whole signal, <paramref name="lowHz"/> to <paramref name="highHz"/>, lies inside one Extra segment.</summary>
    public static bool Contains(long lowHz, long highHz) => Contains(UsLicenceClass.AmateurExtra, lowHz, highHz);

    /// <summary>
    /// True when the whole signal, <paramref name="lowHz"/> to <paramref name="highHz"/>, lies inside one data segment
    /// of the class. Always true for <see cref="UsLicenceClass.NotUs"/>.
    /// </summary>
    public static bool Contains(UsLicenceClass licence, long lowHz, long highHz) =>
        licence == UsLicenceClass.NotUs || For(licence).Any(s => lowHz >= s.LowHz && highHz <= s.HighHz);

    /// <summary>The class's data segments that overlap <paramref name="lowHz"/> to <paramref name="highHz"/> (a band), clipped to it.</summary>
    public static IReadOnlyList<(long LowHz, long HighHz)> Within(UsLicenceClass licence, long lowHz, long highHz) =>
        [.. For(licence).Where(s => s.LowHz < highHz && s.HighHz > lowHz).Select(s => (Math.Max(s.LowHz, lowHz), Math.Min(s.HighHz, highHz)))];

    /// <summary>"Amateur Extra", "Advanced", "General", "Technician" or "Not US".</summary>
    public static string Name(UsLicenceClass licence) => licence switch
    {
        UsLicenceClass.AmateurExtra => "Amateur Extra",
        UsLicenceClass.Advanced => "Advanced",
        UsLicenceClass.General => "General",
        UsLicenceClass.Technician => "Technician",
        _ => "Not US",
    };

    /// <summary>Segments as "14.025-14.150 MHz", or "5.3306-5.3334, 5.3515-5.3665 and 5.3716-5.3744 MHz".</summary>
    public static string Describe(IReadOnlyList<(long LowHz, long HighHz)> segments)
    {
        var parts = segments.Select(s => $"{Mhz(s.LowHz)}-{Mhz(s.HighHz)}").ToList();
        var list = parts.Count <= 1 ? string.Concat(parts) : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        return list + " MHz";
    }

    /// <summary>A frequency in MHz with three to six decimals, as needed: "14.025", "5.3306", "14.0715".</summary>
    public static string Mhz(long hz) => (hz / 1_000_000m).ToString("0.000###", CultureInfo.InvariantCulture);
}
