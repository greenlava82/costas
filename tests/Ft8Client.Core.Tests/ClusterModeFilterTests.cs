// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using FluentAssertions;
using Ft8Client.Core.Bands;
using Ft8Client.Core.Cluster;

namespace Ft8Client.Core.Tests;

public class ClusterModeFilterTests
{
    private static readonly FrequencyTable Table = new(
    [
        new FrequencyEntry("20m", Mode.Ft8, 14_074_000, 14_000_000, 14_350_000),
        new FrequencyEntry("20m", Mode.Ft4, 14_080_000, 14_000_000, 14_350_000),
        new FrequencyEntry("20m", Mode.Psk31, 14_070_000, 14_000_000, 14_350_000),
        new FrequencyEntry("20m", Mode.Rtty, 14_080_000, 14_000_000, 14_350_000),
        new FrequencyEntry("80m", Mode.Ft8, 3_573_000, 3_500_000, 4_000_000),
    ]);

    private static ClusterSpot Spot(long hz, string comment) => new("K1ABC", hz, "DL1ABC", comment, DateTime.UnixEpoch);

    [Theory]
    [InlineData("FT8 -05dB from JO46 814Hz", "FT8")]
    [InlineData("ft4 -06dB from KN98 2038Hz", "FT4")]
    [InlineData("BPSK31 cq", "PSK31")]
    [InlineData("psk63", "PSK63")]
    [InlineData("PSK", "PSK")]
    [InlineData("RTTY 599", "RTTY")]
    [InlineData("CW 22 WPM", "OTHER")]
    [InlineData("tnx QSO, SSB", "OTHER")]
    [InlineData("TNX QSO", null)]
    [InlineData("", null)]
    public void ModeNamed_Comment_ReturnsMode(string comment, string? expected)
    {
        ClusterModeFilter.ModeNamed(comment).Should().Be(expected);
    }

    [Fact]
    public void Matches_CommentNamesMode_FollowsCommentNotFrequency()
    {
        // A DXpedition's Fox frequency is off the usual dial: the comment decides.
        ClusterModeFilter.Matches(Spot(14_090_000, "FT8 F/H"), Mode.Ft8, Table).Should().BeTrue();
        ClusterModeFilter.Matches(Spot(14_074_500, "CW"), Mode.Ft8, Table).Should().BeFalse();
        ClusterModeFilter.Matches(Spot(14_074_500, "FT4"), Mode.Ft8, Table).Should().BeFalse();
    }

    [Theory]
    [InlineData(14_074_000, true)]
    [InlineData(14_073_600, true)]
    [InlineData(14_076_268, true)]
    [InlineData(14_077_500, true)]
    [InlineData(14_078_000, false)]
    [InlineData(14_025_000, false)]
    public void Matches_NoModeInComment_JudgedByDialWindow(long hz, bool expected)
    {
        ClusterModeFilter.Matches(Spot(hz, "tnx"), Mode.Ft8, Table).Should().Be(expected);
    }

    [Fact]
    public void Matches_PskWithoutSpeed_CountsForBothPskModes()
    {
        ClusterModeFilter.Matches(Spot(14_070_500, "PSK"), Mode.Psk31, Table).Should().BeTrue();
        ClusterModeFilter.Matches(Spot(14_070_500, "PSK"), Mode.Psk63, Table).Should().BeTrue();
    }

    [Fact]
    public void Matches_RttyWithoutModeWord_Refused()
    {
        ClusterModeFilter.Matches(Spot(14_081_000, ""), Mode.Rtty, Table).Should().BeFalse();
        ClusterModeFilter.Matches(Spot(14_081_000, "RTTY"), Mode.Rtty, Table).Should().BeTrue();
    }
}
