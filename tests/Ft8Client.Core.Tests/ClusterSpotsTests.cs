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

public class ClusterSpotsTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);

    private static ClusterSpot Spot(string call, long hz, int minutes = 0, string spotter = "DL1ABC", string comment = "FT8") =>
        new(call, hz, spotter, comment, T0.AddMinutes(minutes));

    [Fact]
    public void SpotFor_SpotOnBand_ReturnsIt()
    {
        var s = ClusterSpots.Empty.With([Spot("K1ABC", 21_074_000)], T0);

        s.SpotFor("K1ABC", "15m", T0)!.Spotter.Should().Be("DL1ABC");
        s.SpotFor("K1ABC", "20m", T0).Should().BeNull();
    }

    [Theory]
    [InlineData("K1ABC/P", "K1ABC")]
    [InlineData("K1ABC", "K1ABC/P")]
    [InlineData("k1abc", "K1ABC")]
    public void SpotFor_PortableOrCase_ComparesBaseCalls(string spotted, string heard)
    {
        var s = ClusterSpots.Empty.With([Spot(spotted, 21_074_000)], T0);

        s.SpotFor(heard, "15m", T0).Should().NotBeNull();
    }

    [Fact]
    public void With_SameCallAndBandTwice_KeepsNewest()
    {
        var s = ClusterSpots.Empty.With([Spot("K1ABC", 21_074_000, -5, "OLD1"), Spot("K1ABC", 21_075_000, -2, "NEW1"), Spot("K1ABC", 21_074_500, -9, "OLD2")], T0);

        s.Count.Should().Be(1);
        s.SpotFor("K1ABC", "15m", T0)!.Spotter.Should().Be("NEW1");
    }

    [Fact]
    public void With_OutsideAmateurBands_Ignored()
    {
        ClusterSpots.Empty.With([Spot("K1ABC", 15_000_000)], T0).Count.Should().Be(0);
    }

    [Fact]
    public void SpotFor_OlderThanMaxAge_ReturnsNull()
    {
        var s = ClusterSpots.Empty.With([Spot("K1ABC", 21_074_000)], T0);

        s.SpotFor("K1ABC", "15m", T0 + ClusterSpots.MaxAge).Should().NotBeNull();
        s.SpotFor("K1ABC", "15m", T0 + ClusterSpots.MaxAge + TimeSpan.FromMinutes(1)).Should().BeNull();
        s.All(T0.AddHours(1)).Should().BeEmpty();
    }

    [Fact]
    public void With_LaterChange_DropsAgedSpots()
    {
        var s = ClusterSpots.Empty.With([Spot("K1ABC", 21_074_000)], T0)
            .With([Spot("K2XYZ", 14_074_000, 40)], T0.AddMinutes(40));

        s.Count.Should().Be(1);
        s.SpotFor("K2XYZ", "20m", T0.AddMinutes(40)).Should().NotBeNull();
    }

    [Fact]
    public void With_NothingNew_ReturnsSameInstance()
    {
        var s = ClusterSpots.Empty.With([Spot("K1ABC", 21_074_000)], T0);

        s.With([Spot("K1ABC", 21_074_000, -3)], T0).Should().BeSameAs(s);
    }

    [Fact]
    public void Latest_SpottedOnTwoBands_ReturnsNewest()
    {
        var s = ClusterSpots.Empty.With([Spot("K1ABC", 21_074_000, -6), Spot("K1ABC", 14_074_000, -1)], T0);

        s.Latest("K1ABC", T0)!.FrequencyHz.Should().Be(14_074_000);
        s.All(T0).Select(a => a.Band).Should().Equal("20m", "15m");
    }
}
