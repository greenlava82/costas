// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using Ft8Client.App.Services;

namespace Ft8Client.App.Tests;

public class DecodeHealthStatsTests
{
    [Fact]
    public void Add_FewerSlotsThanAReport_ReturnsNothing()
    {
        var stats = new DecodeHealthStats();
        for (var i = 0; i < DecodeHealthStats.SlotsPerReport - 1; i++)
            stats.Add("FT8", 10, TimeSpan.FromSeconds(1), true).Should().BeNull();
    }

    [Fact]
    public void Add_AnHourOfSlots_ReportsCountsAndRoundedMedian()
    {
        var stats = new DecodeHealthStats();
        Dictionary<string, object>? report = null;
        for (var i = 0; i < DecodeHealthStats.SlotsPerReport; i++)
        {
            var failed = i % 10 == 0; // 24 of 240 fail
            report = stats.Add("FT8", 12, TimeSpan.FromMilliseconds(1234), !failed);
        }
        report.Should().NotBeNull();
        report!["mode"].Should().Be("FT8");
        report["slots"].Should().Be(240);
        report["failed_pct"].Should().Be(10);
        report["avg_decodes"].Should().Be(12);
        report["median_decode_ms"].Should().Be(1200);
        stats.Add("FT8", 1, TimeSpan.FromSeconds(1), true).Should().BeNull(); // counting starts again
    }

    [Fact]
    public void Add_TwoModes_CountedSeparately()
    {
        var stats = new DecodeHealthStats();
        for (var i = 0; i < DecodeHealthStats.SlotsPerReport - 1; i++) stats.Add("FT8", 1, TimeSpan.Zero, true);
        stats.Add("FT4", 1, TimeSpan.Zero, true).Should().BeNull();
        stats.Add("FT8", 1, TimeSpan.Zero, true).Should().NotBeNull();
    }
}
