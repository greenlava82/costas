// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using FluentAssertions;
using Ft8Client.App.Views;
using Ft8Client.Core.Spectrum;

namespace Ft8Client.App.Tests;

public class WaterfallPaletteTests
{
    private static double Luma(int c) => 0.3 * ((c >> 16) & 0xFF) + 0.59 * ((c >> 8) & 0xFF) + 0.11 * (c & 0xFF);

    private static (int R, int G, int B) Rgb(int c) => ((c >> 16) & 0xFF, (c >> 8) & 0xFF, c & 0xFF);

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(32, 0, 6, 136)]
    [InlineData(64, 0, 19, 198)]
    [InlineData(96, 0, 32, 239)]
    [InlineData(128, 172, 167, 105)]
    [InlineData(160, 194, 198, 49)]
    [InlineData(192, 225, 228, 107)]
    [InlineData(224, 255, 255, 0)]
    public void At_Wsjtx_HitsDefaultPalStopsEvery32Steps(int step, int r, int g, int b)
    {
        // WSJT-X Palettes/Default.pal spread over 256 colours by WFPalette::interpolate: a stop every 256/8 = 32 steps.
        Rgb(WaterfallPalette.At(WaterfallPalette.Wsjtx, step)).Should().Be((r, g, b));
    }

    [Fact]
    public void At_WsjtxLastStep_FallsJustShortOfTheFinalStop()
    {
        // WSJT-X never reaches 255,51,0 itself: step 255 is 31/32 of the way from yellow.
        Rgb(WaterfallPalette.At(WaterfallPalette.Wsjtx, 255)).Should().Be((255, 57, 0));
    }

    [Fact]
    public void At_Costas_DarkBlueToBrightRed()
    {
        var dark = WaterfallPalette.At(WaterfallPalette.Costas, 0);
        var bright = WaterfallPalette.At(WaterfallPalette.Costas, WaterfallPalette.Steps - 1);
        ((dark >> 16) & 0xFF).Should().BeLessThan(dark & 0xFF, "the darkest colour is blue");
        ((bright >> 16) & 0xFF).Should().Be(255, "the brightest colour is full red");
        (bright & 0xFF).Should().BeLessThan(100);
    }

    [Fact]
    public void At_CostasEachStep_NoDarkerThanTheOneBefore()
    {
        for (var i = 1; i < WaterfallPalette.Steps; i++)
            Luma(WaterfallPalette.At(WaterfallPalette.Costas, i)).Should()
                .BeGreaterThanOrEqualTo(Luma(WaterfallPalette.At(WaterfallPalette.Costas, i - 1)) - 1, $"step {i} (allowing for rounding to whole colour levels)");
    }

    [Theory]
    [InlineData(null, WaterfallPalette.Wsjtx)]
    [InlineData("", WaterfallPalette.Wsjtx)]
    [InlineData("linrad", WaterfallPalette.Wsjtx)]
    [InlineData(WaterfallPalette.Costas, WaterfallPalette.Costas)]
    public void Known_UnknownId_FallsBackToWsjtx(string? id, string known)
    {
        WaterfallPalette.Known(id).Should().Be(known);
    }

    [Fact]
    public void For_NoiseFloor_IsDarkBlueInWsjtxColours()
    {
        // At the default scale the noise itself (0 dB) sits about a seventh of the way up: between black and navy.
        var colours = WaterfallPalette.Colours(WaterfallPalette.Wsjtx);
        var (r, g, b) = Rgb(WaterfallPalette.For(colours, WaterfallLevels.Default, 0));
        r.Should().Be(0);
        b.Should().BeGreaterThan(g).And.BeGreaterThan(50);
    }
}
