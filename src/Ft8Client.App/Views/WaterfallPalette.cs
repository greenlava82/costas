// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Spectrum;

namespace Ft8Client.App.Views;

/// <summary>
/// The waterfall colours: dark navy for the noise, through blue and purple, to bright red for the strongest signals
/// (owner request 2026-11-16). Plain integers, no Avalonia objects, so it can be used from any thread.
/// </summary>
public static class WaterfallPalette
{
    /// <summary>Colour steps from darkest to brightest.</summary>
    public const int Steps = 256;

    private static readonly (double At, int R, int G, int B)[] Stops =
    [
        (0.00, 4, 10, 32),
        (0.30, 20, 60, 200),
        (0.55, 120, 40, 190),
        (0.80, 220, 30, 50),
        (1.00, 255, 70, 50),
    ];

    private static readonly int[] Colours = Enumerable.Range(0, Steps).Select(i => Colour(i / (Steps - 1.0))).ToArray();

    /// <summary>The colour at a step, as 0xAARRGGBB (BGRA bytes in memory).</summary>
    public static int At(int step) => Colours[Math.Clamp(step, 0, Steps - 1)];

    /// <summary>The colour for a level relative to the noise floor on a scale.</summary>
    public static int For(WaterfallLevels scale, double relativeDb) => Colours[scale.Step(relativeDb, Steps)];

    private static int Colour(double t)
    {
        var k = 1;
        while (k < Stops.Length - 1 && t > Stops[k].At) k++;
        var (a, b) = (Stops[k - 1], Stops[k]);
        var f = Math.Clamp((t - a.At) / (b.At - a.At), 0, 1);
        int Mix(int x, int y) => (int)Math.Round(x + (y - x) * f);
        return unchecked((int)0xFF000000) | (Mix(a.R, b.R) << 16) | (Mix(a.G, b.G) << 8) | Mix(a.B, b.B);
    }
}
