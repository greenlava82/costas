// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using FluentAssertions;
using Ft8Client.Core.Cluster;

namespace Ft8Client.Core.Tests;

public class ClusterTextTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Details_SpotWithComment_SaysAgeBandSpotterAndComment()
    {
        var s = new ClusterSpot("K1ABC", 21_074_000, "DL1ABC", "FT8 -12dB", T0.AddMinutes(-4));

        ClusterText.Details(s, T0).Should().Be("Spotted 4 min ago on 15 m by DL1ABC · FT8 -12dB");
        ClusterText.Why(s, T0).Should().Be("Spotted 4 min ago");
    }

    [Fact]
    public void Details_NoCommentJustNow_LeavesCommentOut()
    {
        var s = new ClusterSpot("K1ABC", 14_074_000, "DL1ABC", "", T0);

        ClusterText.Details(s, T0).Should().Be("Spotted just now on 20 m by DL1ABC");
    }

    [Fact]
    public void Why_Null_Empty()
    {
        ClusterText.Why(null, T0).Should().BeEmpty();
        ClusterText.Details(null, T0).Should().BeEmpty();
    }

    [Fact]
    public void Khz_Hertz_OneDecimal()
    {
        ClusterText.Khz(21_074_000).Should().Be("21074.0");
    }
}
