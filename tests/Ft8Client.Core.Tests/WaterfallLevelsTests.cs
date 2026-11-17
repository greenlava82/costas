// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Spectrum;

namespace Ft8Client.Core.Tests;

public class WaterfallLevelsTests
{
    [Theory]
    [InlineData(-20, 0)]
    [InlineData(-5, 0)]
    [InlineData(12.5, 128)]
    [InlineData(30, 255)]
    [InlineData(45, 255)]
    public void Step_DefaultScale_MapsFloorToDarkAndTopToBright(double relative, int step)
    {
        WaterfallLevels.Default.Step(relative, 256).Should().Be(step);
    }

    [Fact]
    public void Step_NarrowerRange_GivesWeakSignalsMoreColour()
    {
        var wide = WaterfallLevels.Default.Step(5, 256);
        var narrow = (WaterfallLevels.Default with { RangeDb = 15 }).Step(5, 256);
        narrow.Should().BeGreaterThan(wide);
    }

    [Fact]
    public void Step_NotANumber_IsDarkest()
    {
        WaterfallLevels.Default.Step(double.NaN, 256).Should().Be(0);
    }

    [Fact]
    public void Clamped_OutOfRange_BroughtInsideTheLimits()
    {
        new WaterfallLevels(-99, 500).Clamped().Should().Be(new WaterfallLevels(WaterfallLevels.MinFloorDb, WaterfallLevels.MaxRangeDb));
    }
}
