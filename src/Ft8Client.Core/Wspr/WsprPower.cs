// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>The power values a WSPR message may carry, and an estimate of what a setting puts out (docs/10-wspr.md section 6.3).</summary>
public static class WsprPower
{
    /// <summary>The legal dBm values: 0 to 60 ending in 0, 3 or 7 (the decoder reads any other as part of a type 2 message).</summary>
    public static IReadOnlyList<int> Legal { get; } = [0, 3, 7, 10, 13, 17, 20, 23, 27, 30, 33, 37, 40, 43, 47, 50, 53, 57, 60];

    /// <summary>True when <paramref name="dbm"/> may be sent.</summary>
    public static bool IsLegal(int dbm) => dbm is >= 0 and <= 60 && (dbm % 10) is 0 or 3 or 7;

    /// <summary>
    /// What a radio set to <paramref name="watts"/> puts out with <paramref name="attenuationDb"/> (zero or negative) on the
    /// audio drive: <c>10 log10(watts × 1000) + attenuation</c> dBm. Advice only; never sent in a message.
    /// </summary>
    public static double EstimateDbm(double watts, double attenuationDb) =>
        watts > 0 ? 10 * Math.Log10(watts * 1000) + attenuationDb : double.NegativeInfinity;

    /// <summary>The estimate rounded to one decimal, for display.</summary>
    public static double Display(double dbm) => Math.Round(dbm, 1);

    /// <summary>The power in watts that <paramref name="dbm"/> stands for.</summary>
    public static double Watts(int dbm) => Math.Pow(10, dbm / 10.0) / 1000;
}
