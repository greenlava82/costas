// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia;
using FluentAssertions;
using Ft8Client.App.Views;

namespace Ft8Client.App.Tests;

public class WaterfallViewTests
{
    [Fact]
    public void VisibleRows_CompactStrip_KeepsTodays32Rows()
    {
        // The compact strip is 52 px with an 18 px scale: 34 px of plot.
        WaterfallView.VisibleRows(34).Should().Be(32);
    }

    [Fact]
    public void VisibleRows_TallerView_ShowsMoreTime()
    {
        WaterfallView.VisibleRows(320).Should().Be(200);
    }

    [Fact]
    public void VisibleRows_VeryTallView_CappedAtHistoryKept()
    {
        WaterfallView.VisibleRows(4000).Should().Be(WaterfallView.MaxRows);
    }

    [Theory]
    [InlineData(2400, 2400)]
    [InlineData(3000, null)]
    [InlineData(3600, null)]
    [InlineData(0, null)]
    [InlineData(null, null)]
    public void FilterEdge_WidthNarrowerThanTheWaterfall_IsDrawn(int? passband, int? edge)
    {
        WaterfallView.FilterEdge(passband).Should().Be(edge);
    }

    [Theory]
    [InlineData(null, 34, 493)]
    [InlineData(2400, 34, 391)]
    [InlineData(3600, 34, 493)]
    public void NoiseRange_NarrowFilter_StopsShortOfItsEdge(int? passband, int first, int last)
    {
        WaterfallView.NoiseRange(512, passband).Should().Be((first, last));
    }

    [Theory]
    [InlineData(34, 1)]
    [InlineData(100, 2)]
    [InlineData(300, 3)]
    public void CallTagLines_PlotHeight_OneLineOnTheCompactStrip(double plot, int lines)
    {
        WaterfallView.CallTagLines(plot).Should().Be(lines);
    }

    [Fact]
    public void LayoutCallTags_FarApart_SideBySideOnTheirRow()
    {
        var placed = WaterfallView.LayoutCallTags([new CallTagBox(100, 20, 40, 12), new CallTagBox(300, 20, 40, 12)], 1000, 400, 3);

        placed.Should().Equal(new Rect(78, 20, 44, 14), new Rect(278, 20, 44, 14));
    }

    [Fact]
    public void LayoutCallTags_Overlapping_SecondMovesDownALine()
    {
        var placed = WaterfallView.LayoutCallTags([new CallTagBox(100, 20, 40, 12), new CallTagBox(120, 20, 40, 12)], 1000, 400, 3);

        placed[0].Should().Be(new Rect(78, 20, 44, 14));
        placed[1].Should().Be(new Rect(98, 35, 44, 14));
    }

    [Fact]
    public void LayoutCallTags_NoLineFree_LeftOut()
    {
        var boxes = Enumerable.Range(0, 4).Select(_ => new CallTagBox(100, 20, 40, 12)).ToList();

        var placed = WaterfallView.LayoutCallTags(boxes, 1000, 400, 3);

        placed.Take(3).Should().OnlyContain(r => r.HasValue);
        placed[3].Should().BeNull("three lines are full");
    }

    [Fact]
    public void LayoutCallTags_CompactStrip_OneLineOnly()
    {
        var placed = WaterfallView.LayoutCallTags([new CallTagBox(100, 18, 40, 12), new CallTagBox(110, 18, 40, 12)], 1000, 52, 1);

        placed[0].Should().NotBeNull();
        placed[1].Should().BeNull();
    }

    [Fact]
    public void LayoutCallTags_NextLineBelowTheBottom_LeftOut()
    {
        var placed = WaterfallView.LayoutCallTags([new CallTagBox(100, 380, 40, 12), new CallTagBox(110, 380, 40, 12)], 1000, 400, 3);

        placed[0].Should().NotBeNull("the first line is always tried; the clip shows what fits");
        placed[1].Should().BeNull("a second line would be cut off");
    }

    [Fact]
    public void LayoutCallTags_AtTheEdges_KeptInside()
    {
        var placed = WaterfallView.LayoutCallTags([new CallTagBox(5, 20, 40, 12), new CallTagBox(995, 20, 40, 12)], 1000, 400, 1);

        placed[0]!.Value.X.Should().Be(0);
        placed[1]!.Value.Right.Should().Be(1000);
    }

    [Fact]
    public void LayoutCallTags_NarrowerThanATag_StartsAtTheLeft()
    {
        var placed = WaterfallView.LayoutCallTags([new CallTagBox(10, 20, 40, 12)], 20, 400, 1);

        placed[0]!.Value.X.Should().Be(0);
    }

    [Fact]
    public void LayoutCallTags_TagsOnDifferentRows_OlderRowGivesWay()
    {
        // Newest row first: a tag on an older row a few pixels lower moves down past the newer one.
        var placed = WaterfallView.LayoutCallTags([new CallTagBox(100, 20, 40, 12), new CallTagBox(100, 25, 40, 12)], 1000, 400, 3);

        placed[1]!.Value.Y.Should().Be(40);
    }

    [Fact]
    public void LayoutCallTags_JustTouching_KeepsAGap()
    {
        // 44 px boxes 45 px apart would be 1 px apart; the 2 px gap pushes the second down.
        var placed = WaterfallView.LayoutCallTags([new CallTagBox(100, 20, 40, 12), new CallTagBox(145, 20, 40, 12)], 1000, 400, 2);

        placed[1]!.Value.Y.Should().Be(35);
    }
}
