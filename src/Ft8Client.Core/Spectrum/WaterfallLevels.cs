// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Spectrum;

/// <summary>
/// The waterfall colour scale, in dB relative to the noise floor: <see cref="FloorDb"/> is the darkest colour and
/// <see cref="FloorDb"/> + <see cref="RangeDb"/> the brightest. The defaults (−5 dB, 35 dB) put the noise just above
/// black, give signals near the FT8 decode limit (a few dB above the noise in one bin) most of the change in colour,
/// and show strong signals (+30 dB and up) at full brightness.
/// </summary>
/// <param name="FloorDb">The level shown darkest, in dB relative to the noise floor.</param>
/// <param name="RangeDb">The span from darkest to brightest, in dB.</param>
public sealed record WaterfallLevels(int FloorDb, int RangeDb)
{
    /// <summary>Default darkest level.</summary>
    public const int DefaultFloorDb = -5;

    /// <summary>Default span.</summary>
    public const int DefaultRangeDb = 35;

    /// <summary>Lowest floor offered.</summary>
    public const int MinFloorDb = -20;

    /// <summary>Highest floor offered.</summary>
    public const int MaxFloorDb = 10;

    /// <summary>Narrowest span offered.</summary>
    public const int MinRangeDb = 10;

    /// <summary>Widest span offered.</summary>
    public const int MaxRangeDb = 60;

    /// <summary>The default scale.</summary>
    public static WaterfallLevels Default { get; } = new(DefaultFloorDb, DefaultRangeDb);

    /// <summary>This scale with both values brought inside the offered limits.</summary>
    public WaterfallLevels Clamped() =>
        new(Math.Clamp(FloorDb, MinFloorDb, MaxFloorDb), Math.Clamp(RangeDb, MinRangeDb, MaxRangeDb));

    /// <summary>The palette step for a level relative to the noise floor: 0 at or below the floor, <paramref name="steps"/> − 1 at or above the top.</summary>
    /// <param name="relativeDb">Level minus the noise floor, in dB.</param>
    /// <param name="steps">Number of palette steps.</param>
    public int Step(double relativeDb, int steps)
    {
        var t = (relativeDb - FloorDb) / Math.Max(1, RangeDb);
        if (double.IsNaN(t)) return 0;
        return Math.Clamp((int)(t * (steps - 1) + 0.5), 0, steps - 1);
    }
}
