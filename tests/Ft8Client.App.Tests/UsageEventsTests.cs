// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Text.Json;
using Ft8Client.App.Services;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

public class UsageEventsTests
{
    [Fact]
    public void Started_StationWithCallGridAndPort_SendsNoneOfThem()
    {
        var s = new AppSettings();
        s.Profile.Callsign = "W7LIT";
        s.Profile.Grid = "DN40";
        s.Profile.Rig.Mode = RigModes.Hamlib;
        s.Profile.Rig.Model = 1049;
        s.Profile.Rig.Port = "COM3";

        var json = JsonSerializer.Serialize(UsageEvents.Started(s, decoderFound: true));

        json.Should().NotContain("W7LIT").And.NotContain("DN40").And.NotContain("COM3");
        json.Should().Contain("\"rig\":\"hamlib\"").And.Contain("\"rig_model\":\"1049\"");
    }
    [Fact]
    public void Started_WithFacts_SendsInstallAndAdoptionButNoStationDetails()
    {
        var s = new AppSettings();
        s.Profile.Callsign = "W7LIT";
        s.Profile.Grid = "DN40";
        var p = UsageEvents.Started(s, decoderFound: true, new StartFacts("velopack", "release", 12, 9, 7));

        p["install"].Should().Be("velopack");
        p["channel"].Should().Be("release");
        p["install_age"].Should().Be("8-30d");
        p["active_days_30"].Should().Be(9);
        p["features_tried"].Should().Be(7);
        JsonSerializer.Serialize(p).Should().NotContain("W7LIT").And.NotContain("DN40");
    }

    [Theory]
    [InlineData(2, "<5m")]
    [InlineData(45, "30-60m")]
    [InlineData(150, "2-4h")]
    [InlineData(2000, "24h+")]
    public void RunLength_Minutes_GivesRange(int minutes, string expected) =>
        UsageEvents.RunLength(TimeSpan.FromMinutes(minutes)).Should().Be(expected);

    [Fact]
    public void Capped_LargeCount_IsCapped() => UsageEvents.Capped(250).Should().Be(100);

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Mobile/15E148 Safari/604.1", "phone")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) Chrome/120 Mobile Safari/537.36", "phone")]
    [InlineData("Mozilla/5.0 (Linux; Android 13; SM-X700) Chrome/120 Safari/537.36", "tablet")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 16_0 like Mac OS X)", "tablet")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120", "computer")]
    [InlineData(null, "computer")]
    public void DeviceKind_UserAgent_GivesKindOnly(string? ua, string expected) => UsageEvents.DeviceKind(ua).Should().Be(expected);
}
