// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Mqtt;

namespace Ft8Client.Core.Tests.Mqtt;

public class DashboardStatsBuilderTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Today = new(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyDictionary<int, string> Adif = new Dictionary<int, string>
    {
        [291] = "K", [339] = "JA", [230] = "DL", [150] = "VK", [202] = "KP4", [1] = "VE",
    };

    private static DashboardStatsBuilder Builder() => new(TestData.Countries, Adif);

    private static StatsQso Q(long id, string call, DateTime on, string band = "20m", string? grid = null, string? key = null, int? dxcc = null) =>
        new(id, call, on, on.AddSeconds(60), band, "FT8", 14_074_500, null, dxcc, key, null, grid, "-10", "-12");

    // Every row is also a "first" candidate, as the repository's per-call rows would be.
    private static DashboardInput Input(IReadOnlyList<StatsQso> all, DateTime? now = null, string? myGrid = "DN40")
    {
        var at = now ?? Now;
        var weekStart = at.Date.AddDays(-6);
        return new DashboardInput(at, myGrid, all.Count, [.. all.Where(q => q.TimeOnUtc >= weekStart)],
            [.. all.OrderByDescending(q => q.TimeOnUtc).Take(10)], all);
    }

    [Fact]
    public void Build_ContactsAroundMidnight_CountForTheirUtcDay()
    {
        var all = new[]
        {
            Q(1, "K1ABC", Today.AddSeconds(-1)),
            Q(2, "K2ABC", Today),
            Q(3, "K3ABC", Today.AddHours(17)),
        };

        var s = Builder().Build(Input(all));

        s.QsosToday.Should().Be(2);
        s.QsosWeek.Should().Be(3);
        s.QsosTotal.Should().Be(3);
        s.TodayUtc.Should().Be(Today);
    }

    [Fact]
    public void Build_WeekWindow_SevenUtcDaysIncludingToday()
    {
        var all = new[]
        {
            Q(1, "K1ABC", Today.AddDays(-7).AddHours(23)), // eight days back: outside
            Q(2, "K2ABC", Today.AddDays(-6)),               // first second of the week
            Q(3, "K3ABC", Today.AddHours(1)),
        };

        var s = Builder().Build(Input(all));

        s.QsosWeek.Should().Be(2);
        s.Days.Should().HaveCount(7);
        s.Days[0].Date.Should().Be(Today.AddDays(-6));
        s.Days[^1].Date.Should().Be(Today);
        s.Days.Sum(d => d.Total).Should().Be(2);
    }

    [Fact]
    public void Build_PastUtcMidnight_TodayCountsReset()
    {
        var all = new[] { Q(1, "JA1ABC", Today.AddHours(10), grid: "PM95", key: "JA") };

        var before = Builder().Build(Input(all, Today.AddDays(1).AddSeconds(-1)));
        var after = Builder().Build(Input(all, Today.AddDays(1)));

        before.QsosToday.Should().Be(1);
        before.NewDxccToday.Should().ContainSingle();
        before.FarthestToday.Should().NotBeNull();
        after.QsosToday.Should().Be(0);
        after.NewDxccToday.Should().BeEmpty();
        after.FarthestToday.Should().BeNull();
        after.QsosWeek.Should().Be(1, "the contact is still in the rolling week");
        after.Days[^2].Total.Should().Be(1);
        after.Days[^1].Total.Should().Be(0);
    }

    [Fact]
    public void Build_EntityWorkedBefore_NotNewToday()
    {
        var all = new[]
        {
            Q(1, "JA1ABC", Today.AddDays(-30), key: "JA"),
            Q(2, "JA2XYZ", Today.AddHours(2), key: "JA"),
            Q(3, "DL1ABC", Today.AddHours(3), key: "DL"),
            Q(4, "DL2ABC", Today.AddHours(4), key: "DL"),
        };

        var s = Builder().Build(Input(all));

        s.DxccWorkedTotal.Should().Be(2);
        s.NewDxccToday.Should().ContainSingle().Which.Should().Be(new NewEntity(230, "Fed. Rep. of Germany", "DL1ABC", Today.AddHours(3).AddSeconds(60)));
        s.Recent.Single(c => c.Call == "DL1ABC").IsNewDxcc.Should().BeTrue();
        s.Recent.Single(c => c.Call == "DL2ABC").IsNewDxcc.Should().BeFalse();
        s.Recent.Single(c => c.Call == "JA2XYZ").IsNewDxcc.Should().BeFalse();
    }

    [Fact]
    public void Build_SameTimeFirstContacts_LowerIdIsTheNewOne()
    {
        var t = Today.AddHours(5);
        var all = new[] { Q(8, "DL2ABC", t, key: "DL"), Q(7, "DL1ABC", t, key: "DL") };

        var s = Builder().Build(Input(all));

        s.NewDxccToday.Should().ContainSingle().Which.Call.Should().Be("DL1ABC");
        s.Recent.Count(c => c.IsNewDxcc).Should().Be(1);
    }

    [Fact]
    public void Build_NoEntityKey_UsesAdifCodeThenCall()
    {
        var all = new[]
        {
            Q(1, "AA1AA", Today.AddHours(1), dxcc: 339),                // odd call, code says Japan
            Q(2, "VK2ABC", Today.AddHours(2)),                          // nothing logged: the call says Australia
            Q(3, "ZL1ABC", Today.AddHours(3), key: "NOT-A-KEY"),        // unknown key: the call decides
        };

        var s = Builder().Build(Input(all));

        s.Recent.Single(c => c.Call == "AA1AA").Country.Should().Be("Japan");
        s.Recent.Single(c => c.Call == "AA1AA").Dxcc.Should().Be(339);
        s.Recent.Single(c => c.Call == "VK2ABC").Dxcc.Should().Be(150);
        s.Recent.Single(c => c.Call == "ZL1ABC").Country.Should().Be("New Zealand");
        s.DxccWorkedTotal.Should().Be(3);
    }

    [Fact]
    public void Build_Farthest_LargestDistanceToday()
    {
        var all = new[]
        {
            Q(1, "VK2ABC", Today.AddDays(-1), grid: "QF56"),    // farther, but yesterday
            Q(2, "JA1ABC", Today.AddHours(1), grid: "PM95"),
            Q(3, "K1ABC", Today.AddHours(2), grid: "FN42"),
            Q(4, "DL1ABC", Today.AddHours(3)),                  // no grid: entity centre
        };

        var s = Builder().Build(Input(all));

        s.FarthestToday!.Call.Should().Be("JA1ABC");
        s.FarthestToday.DistanceKm.Should().BeInRange(8000, 9500);
        s.FarthestToday.Grid.Should().Be("PM95");
        s.FarthestToday.Country.Should().Be("Japan");
    }

    [Fact]
    public void Build_NoGridOfMine_NoDistances()
    {
        var all = new[] { Q(1, "JA1ABC", Today.AddHours(1), grid: "PM95") };

        var s = Builder().Build(Input(all, myGrid: ""));

        s.FarthestToday.Should().BeNull();
        s.Recent[0].DistanceKm.Should().BeNull();
        s.Recent[0].Lat.Should().NotBeNull("the position does not need my grid");
    }

    [Fact]
    public void Describe_NoGrid_EntityPosition()
    {
        var b = Builder();
        var c = b.Describe(Q(1, "JA1ABC", Today), "DN40", new Dictionary<string, long>());

        c.Grid.Should().BeNull();
        c.Lat.Should().BeInRange(30, 40);
        c.Lon.Should().BeInRange(130, 145);
        c.DistanceKm.Should().BeGreaterThan(7000);
    }

    [Fact]
    public void Describe_UnknownCallNoGrid_NoPosition()
    {
        var c = Builder().Describe(Q(1, "Q0QQQ", Today), "DN40", new Dictionary<string, long>());

        c.Country.Should().BeNull();
        c.Lat.Should().BeNull();
        c.DistanceKm.Should().BeNull();
        c.IsNewDxcc.Should().BeFalse();
    }

    [Fact]
    public void Build_DailyByBand_EveryWeekBandOnEveryDayInBandOrder()
    {
        var all = new[]
        {
            Q(1, "K1ABC", Today.AddDays(-2), band: "40m"),
            Q(2, "K2ABC", Today.AddHours(1), band: "20m"),
            Q(3, "K3ABC", Today.AddHours(2), band: "20M"),
            Q(4, "K4ABC", Today.AddHours(3), band: "160m"),
        };

        var s = Builder().Build(Input(all));

        s.Days[^1].ByBand.Select(p => p.Key).Should().Equal("160m", "40m", "20m");
        s.Days[^1].ByBand.Select(p => p.Value).Should().Equal(1, 0, 2);
        s.Days[^3].ByBand.Select(p => p.Value).Should().Equal(0, 1, 0);
        s.Days[0].Total.Should().Be(0);
        s.Days[0].ByBand.Should().OnlyContain(p => p.Value == 0);
    }

    [Fact]
    public void Build_Map_OnePointPerStationLatestContactSkipsUnknownPosition()
    {
        var all = new[]
        {
            Q(1, "JA1ABC", Today.AddDays(-3), band: "40m", grid: "PM95"),
            Q(2, "ja1abc", Today.AddHours(1), band: "20m", grid: "PM95"),
            Q(3, "K1ABC", Today.AddHours(2), grid: "FN42"),
            Q(4, "K2ABC", Today.AddHours(3), grid: "FN42"),
            Q(5, "Q0QQQ", Today.AddHours(4)),
        };

        var s = Builder().Build(Input(all));

        s.GridsWeek.Should().Be(2);
        s.MapPoints.Select(p => p.Call).Should().Equal("K2ABC", "K1ABC", "ja1abc");
        s.MapPoints.Single(p => p.Call == "ja1abc").Band.Should().Be("20m");
    }

    [Fact]
    public void Build_Recent_TenNewestFirst()
    {
        var all = Enumerable.Range(1, 15).Select(i => Q(i, $"K{i}ABC", Today.AddMinutes(i))).ToList();

        var s = Builder().Build(Input(all));

        s.Recent.Should().HaveCount(10);
        s.Recent[0].Call.Should().Be("K15ABC");
        s.Recent[^1].Call.Should().Be("K6ABC");
        s.Recent[0].RstSent.Should().Be("-10");
        s.Recent[0].FrequencyHz.Should().Be(14_074_500);
    }

    [Fact]
    public void Build_EmptyLog_ZerosEverywhere()
    {
        var s = Builder().Build(Input([]));

        s.QsosToday.Should().Be(0);
        s.QsosTotal.Should().Be(0);
        s.DxccWorkedTotal.Should().Be(0);
        s.Recent.Should().BeEmpty();
        s.MapPoints.Should().BeEmpty();
        s.Days.Should().HaveCount(7).And.OnlyContain(d => d.Total == 0 && d.ByBand.Count == 0);
    }
}
