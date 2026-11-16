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

    [Fact]
    public void At_Ends_DarkBlueToBrightRed()
    {
        var dark = WaterfallPalette.At(0);
        var bright = WaterfallPalette.At(WaterfallPalette.Steps - 1);
        ((dark >> 16) & 0xFF).Should().BeLessThan(dark & 0xFF, "the darkest colour is blue");
        ((bright >> 16) & 0xFF).Should().Be(255, "the brightest colour is full red");
        (bright & 0xFF).Should().BeLessThan(100);
    }

    [Fact]
    public void At_EachStep_NoDarkerThanTheOneBefore()
    {
        for (var i = 1; i < WaterfallPalette.Steps; i++)
            Luma(WaterfallPalette.At(i)).Should().BeGreaterThanOrEqualTo(Luma(WaterfallPalette.At(i - 1)) - 1, $"step {i} (allowing for rounding to whole colour levels)");
    }

    [Fact]
    public void For_NoiseFloor_IsNearTheDarkEnd()
    {
        // At the default scale the noise itself (0 dB) sits a seventh of the way up: dark, but not black.
        var noise = WaterfallPalette.For(WaterfallLevels.Default, 0);
        Luma(noise).Should().BeLessThan(Luma(WaterfallPalette.At(WaterfallPalette.Steps / 2)));
        Luma(noise).Should().BeGreaterThan(Luma(WaterfallPalette.At(0)));
    }
}
