// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using Ft8Client.App.Services;

namespace Ft8Client.App.Tests;

public class UsageHistoryTests
{
    private static readonly DateTime Day1 = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Touch_FirstRun_SetsFirstDayAndAgeZero()
    {
        var h = new UsageHistory();
        h.Touch(Day1).Should().BeTrue();
        h.FirstDay.Should().Be("2026-11-01");
        h.AgeDays(Day1).Should().Be(0);
        h.Touch(Day1.AddHours(2)).Should().BeFalse();
    }

    [Fact]
    public void ActiveDaysIn30_RunsOnSeveralDays_CountsOnlyTheLast30()
    {
        var h = new UsageHistory();
        h.Touch(Day1);
        h.Touch(Day1.AddDays(3));
        h.Touch(Day1.AddDays(40));
        h.AgeDays(Day1.AddDays(40)).Should().Be(40);
        h.ActiveDaysIn30(Day1.AddDays(40)).Should().Be(1);
        h.ActiveDaysIn30(Day1.AddDays(10)).Should().Be(2);
    }

    [Fact]
    public void Touch_DaysOlderThanKept_AreDropped()
    {
        var h = new UsageHistory();
        h.Touch(Day1);
        h.Touch(Day1.AddDays(UsageHistory.KeptDays + 1));
        h.ActiveDays.Should().Equal("2027-01-01");
        h.FirstDay.Should().Be("2026-11-01");
    }

    [Fact]
    public void FirstUse_SameFeatureTwice_TrueOnlyTheFirstTime()
    {
        var h = new UsageHistory();
        h.FirstUse("auto_call_started").Should().BeTrue();
        h.FirstUse("auto_call_started").Should().BeFalse();
        h.FirstUse("view:Log").Should().BeTrue();
    }

    [Theory]
    [InlineData(0, "0d")]
    [InlineData(5, "1-7d")]
    [InlineData(30, "8-30d")]
    [InlineData(31, "31-90d")]
    [InlineData(200, "91-365d")]
    [InlineData(400, "365d+")]
    public void AgeBucket_Days_GivesRange(int days, string expected) => UsageHistory.AgeBucket(days).Should().Be(expected);

    [Fact]
    public void SaveAndLoad_RoundTrip_KeepsEverything()
    {
        var path = Path.Combine(Path.GetTempPath(), $"usage-history-{Guid.NewGuid():N}.json");
        try
        {
            var h = new UsageHistory();
            h.Touch(Day1);
            h.FirstUse("tune");
            h.Save(path);
            var back = UsageHistory.Load(path);
            back.FirstDay.Should().Be("2026-11-01");
            back.FeaturesUsed.Should().Equal("tune");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_BrokenFile_StartsAgain()
    {
        var path = Path.Combine(Path.GetTempPath(), $"usage-history-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            UsageHistory.Load(path).FirstDay.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
