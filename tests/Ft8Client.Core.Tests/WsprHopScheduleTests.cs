// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Wspr;

namespace Ft8Client.Core.Tests;

public class WsprHopScheduleTests
{
    private static readonly DateTime Day = new(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime Slot(int n) => Day.AddMinutes(2 * n);

    [Fact]
    public void BandFor_FewerThanTwoBands_DoesNotHop()
    {
        WsprHopSchedule.BandFor(Slot(0), [], false, 5).Should().BeNull();
        WsprHopSchedule.BandFor(Slot(0), ["20m"], false, 5).Should().BeNull();
        WsprHopSchedule.BandFor(Slot(0), ["20m"], true, 5).Should().BeNull();
    }

    [Fact]
    public void BandFor_ListWithDwell_WalksTheListEveryFiveSlots()
    {
        string[] bands = ["20m", "40m", "30m"];
        var seq = Enumerable.Range(0, 30).Select(i => WsprHopSchedule.BandFor(Slot(i), bands, false, 5)).ToList();

        // Slot numbers count from the epoch, so which band comes first depends on the day; the runs of five and the order do not.
        seq.Chunk(5).Select(c => c.Distinct().Count()).Should().OnlyContain(n => n <= 2, "at most one hop inside any five slots");
        var changes = Enumerable.Range(1, 29).Count(i => seq[i] != seq[i - 1]);
        changes.Should().BeInRange(5, 6, "30 slots at 5 per band is 6 runs, 5 or 6 changes");
        var order = seq.Zip(seq.Skip(1), (a, b) => (a, b)).Where(p => p.a != p.b).Select(p => (Array.IndexOf(bands, p.a), Array.IndexOf(bands, p.b))).ToList();
        order.Should().OnlyContain(p => p.Item2 == (p.Item1 + 1) % 3, "the list is walked in order and wraps");
    }

    [Fact]
    public void BandFor_SameSlot_SameBandWhoeverAsks()
    {
        string[] bands = ["20m", "40m"];
        WsprHopSchedule.BandFor(Slot(77), bands, false, 3).Should().Be(WsprHopSchedule.BandFor(Slot(77), bands, false, 3));
    }

    [Fact]
    public void BandFor_DwellOfOne_ChangesEverySlot()
    {
        string[] bands = ["20m", "40m"];
        var seq = Enumerable.Range(0, 6).Select(i => WsprHopSchedule.BandFor(Slot(i), bands, false, 1)).ToList();

        seq.Zip(seq.Skip(1), (a, b) => a != b).Should().OnlyContain(c => c);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    public void BandFor_NonsenseDwell_IsTreatedAsOne(int dwell, int _)
    {
        string[] bands = ["20m", "40m"];
        WsprHopSchedule.BandFor(Slot(0), bands, false, dwell).Should().Be(WsprHopSchedule.BandFor(Slot(0), bands, false, 1));
    }

    [Theory]
    [InlineData(0, "160m")]
    [InlineData(1, "80m")]
    [InlineData(3, "40m")]
    [InlineData(5, "20m")]
    [InlineData(9, "10m")]
    [InlineData(10, "160m")]
    public void BandFor_Coordinated_FollowsTheTwoMinuteSlotInTwentyMinutes(int slotInDay, string expected)
    {
        var all = WsprHopSchedule.CoordinatedBands;

        WsprHopSchedule.BandFor(Slot(slotInDay), all, true, 5).Should().Be(expected);
    }

    [Fact]
    public void BandFor_CoordinatedBandNotInTheList_FallsBackToTheListWalk()
    {
        string[] bands = ["20m", "40m"];
        // Slot 0 names 160 m, which is not in the list, so the plain list schedule decides.
        WsprHopSchedule.BandFor(Slot(0), bands, true, 5).Should().Be(WsprHopSchedule.BandFor(Slot(0), bands, false, 5));
        WsprHopSchedule.BandFor(Slot(5), bands, true, 5).Should().Be("20m");
    }

    [Fact]
    public void Clean_List_NormalisedDedupedAndOnlyBandsWithWspr()
    {
        var clean = WsprHopSchedule.Clean([" 20M ", "40 m", "20m", "", "60m", "bogus"], b => b is "20m" or "40m");

        clean.Should().Equal("20m", "40m");
    }

    [Fact]
    public void CoordinatedBands_AreTheTen() => WsprHopSchedule.CoordinatedBands.Should().HaveCount(10).And.StartWith("160m").And.EndWith("10m");
}
