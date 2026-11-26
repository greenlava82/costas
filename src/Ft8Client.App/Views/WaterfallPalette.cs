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
/// The waterfall colour tables, darkest first. Plain integers (0xAARRGGBB, BGRA bytes in memory), no Avalonia objects,
/// so they can be used from any thread.
/// </summary>
public static class WaterfallPalette
{
    /// <summary>Colour steps from darkest to brightest.</summary>
    public const int Steps = 256;

    /// <summary>WSJT-X's Default palette: black, blues, yellows, then red-orange.</summary>
    public const string Wsjtx = "wsjtx";

    /// <summary>Dark navy through blue and purple to bright red (owner request 2026-11-16).</summary>
    public const string Costas = "costas";

    /// <summary>Black through dark red to bright red-orange only, for night operating with the Darkroom skin.</summary>
    public const string NightRed = "night-red";

    /// <summary>The palettes offered, by id and the name shown.</summary>
    public static IReadOnlyList<(string Id, string Name)> Choices { get; } = [(Wsjtx, "WSJT-X"), (Costas, "Blue to red"), (NightRed, "Night red")];

    // WSJT-X Palettes/Default.pal (sourceforge.net/p/wsjt/wsjtx, b4f9a43, read 2026-11-16). GPLv3, as Costas.
    private static readonly (int R, int G, int B)[] WsjtxStops =
    [
        (0, 0, 0), (0, 6, 136), (0, 19, 198), (0, 32, 239), (172, 167, 105),
        (194, 198, 49), (225, 228, 107), (255, 255, 0), (255, 51, 0),
    ];

    private static readonly (double At, int R, int G, int B)[] CostasStops =
    [
        (0.00, 4, 10, 32),
        (0.30, 20, 60, 200),
        (0.55, 120, 40, 190),
        (0.80, 220, 30, 50),
        (1.00, 255, 70, 50),
    ];

    // No green or blue light beyond a little warmth at the top, so a dark-adapted eye stays dark-adapted.
    private static readonly (double At, int R, int G, int B)[] NightRedStops =
    [
        (0.00, 0, 0, 0),
        (0.35, 70, 6, 4),
        (0.70, 190, 28, 18),
        (1.00, 255, 110, 70),
    ];

    private static readonly int[] WsjtxColours = WsjtxTable();
    private static readonly int[] CostasColours = Enumerable.Range(0, Steps).Select(i => Interpolate(CostasStops, i / (Steps - 1.0))).ToArray();
    private static readonly int[] NightRedColours = Enumerable.Range(0, Steps).Select(i => Interpolate(NightRedStops, i / (Steps - 1.0))).ToArray();

    /// <summary>The colour table for a palette id; an unknown id gets the WSJT-X palette.</summary>
    public static int[] Colours(string? id) => id switch { Costas => CostasColours, NightRed => NightRedColours, _ => WsjtxColours };

    /// <summary>A known palette id, or the WSJT-X palette for anything else.</summary>
    public static string Known(string? id) => id is Costas or NightRed ? id : Wsjtx;

    /// <summary>The colour at a step of a palette.</summary>
    public static int At(string? id, int step) => Colours(id)[Math.Clamp(step, 0, Steps - 1)];

    /// <summary>The colour for a level relative to the noise floor on a scale.</summary>
    public static int For(int[] colours, WaterfallLevels scale, double relativeDb) => colours[scale.Step(relativeDb, Steps)];

    // The stops spread evenly over the table and joined in straight lines, as WSJT-X's WFPalette::interpolate does it,
    // including its last step falling just short of the final stop.
    private static int[] WsjtxTable()
    {
        var table = new int[Steps];
        var interval = (double)Steps / (WsjtxStops.Length - 1);
        for (var i = 0; i < Steps; i++)
        {
            var prior = Math.Min((int)(i / interval), WsjtxStops.Length - 2);
            var (a, b) = (WsjtxStops[prior], WsjtxStops[prior + 1]);
            var f = (i - interval * prior) / interval;
            table[i] = Argb(a.R + f * (b.R - a.R), a.G + f * (b.G - a.G), a.B + f * (b.B - a.B));
        }
        return table;
    }

    private static int Interpolate((double At, int R, int G, int B)[] stops, double t)
    {
        var k = 1;
        while (k < stops.Length - 1 && t > stops[k].At) k++;
        var (a, b) = (stops[k - 1], stops[k]);
        var f = Math.Clamp((t - a.At) / (b.At - a.At), 0, 1);
        return Argb(a.R + f * (b.R - a.R), a.G + f * (b.G - a.G), a.B + f * (b.B - a.B));
    }

    private static int Argb(double r, double g, double b) =>
        unchecked((int)0xFF000000) | ((int)Math.Round(r) << 16) | ((int)Math.Round(g) << 8) | (int)Math.Round(b);
}
