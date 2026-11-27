// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Text.Json;
using Ft8Client.Core.Mqtt;

namespace Ft8Client.Core.Tests.Mqtt;

/// <summary>The Home Assistant discovery message: sensors only, no controls, ids pinned.</summary>
public class HaDiscoveryTests
{
    private static JsonElement Build() =>
        JsonDocument.Parse(HaDiscovery.Build(new MqttTopics("w7lit"), "W7LIT", "0.1.800")).RootElement;

    [Fact]
    public void Build_Device_NamedForCallsignWithAvailability()
    {
        var j = Build();

        j.GetProperty("dev").GetProperty("ids")[0].GetString().Should().Be("costas_w7lit");
        j.GetProperty("dev").GetProperty("name").GetString().Should().Be("Costas W7LIT");
        j.GetProperty("dev").GetProperty("sw").GetString().Should().Be("0.1.800");
        j.GetProperty("o").GetProperty("name").GetString().Should().Be("Costas");
        j.GetProperty("availability_topic").GetString().Should().Be("costas/w7lit/availability");
    }

    [Fact]
    public void Build_Components_ReadOnlyPlatformsOnly()
    {
        var platforms = Build().GetProperty("cmps").EnumerateObject().Select(c => c.Value.GetProperty("p").GetString()).Distinct();

        platforms.Should().BeSubsetOf(["binary_sensor", "sensor", "event"]);
    }

    [Fact]
    public void Build_Components_NoCommandTopics()
    {
        var json = HaDiscovery.Build(new MqttTopics("w7lit"), "W7LIT", "1");

        json.Should().NotContain("command_topic");
        json.Should().NotContain("/cmd/");
    }

    [Fact]
    public void Build_EveryComponent_UniqueAndPinnedEntityId()
    {
        foreach (var c in Build().GetProperty("cmps").EnumerateObject())
        {
            var p = c.Value.GetProperty("p").GetString();
            c.Value.GetProperty("unique_id").GetString().Should().Be($"costas_w7lit_{c.Name}");
            c.Value.GetProperty("default_entity_id").GetString().Should().Be($"{p}.costas_w7lit_{c.Name}");
        }
    }

    [Fact]
    public void Build_SessionActive_ExpiresWithoutHeartbeat()
    {
        var s = Build().GetProperty("cmps").GetProperty("session_active");

        s.GetProperty("state_topic").GetString().Should().Be("costas/w7lit/session");
        s.GetProperty("expire_after").GetInt32().Should().BeGreaterThan(2 * HaDiscovery.HeartbeatSeconds);
        s.GetProperty("payload_on").GetString().Should().Be("on");
    }

    [Fact]
    public void Build_Events_DeclareTheirTypes()
    {
        var cmps = Build().GetProperty("cmps");

        cmps.GetProperty("session_event").GetProperty("event_types").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo("session_started", "session_ended");
        cmps.GetProperty("qso_event").GetProperty("event_types").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo("qso_logged", "new_dxcc");
    }

    [Fact]
    public void Build_StatsOff_OnlyTheOriginalEntities()
    {
        var cmps = JsonDocument.Parse(HaDiscovery.Build(new MqttTopics("w7lit"), "W7LIT", "1", stats: false)).RootElement.GetProperty("cmps");

        cmps.EnumerateObject().Select(c => c.Name).Should().BeEquivalentTo("session_active", "transmitting", "rig_connected", "state",
            "session_end_reason", "band", "mode", "frequency", "session_event", "qso_event");
    }

    [Theory]
    [InlineData("qsos_today", null, "QSOs")]
    [InlineData("qsos_week", null, "QSOs")]
    [InlineData("qsos_total", null, "QSOs")]
    [InlineData("dxcc_worked_total", null, "entities")]
    [InlineData("new_dxcc_today", null, "entities")]
    [InlineData("farthest_today", "distance", "km")]
    [InlineData("session_duration", "duration", "min")]
    [InlineData("daily_qsos", null, "QSOs")]
    [InlineData("worked_week_map", null, "grids")]
    [InlineData("psk_spots_15m", null, "spots")]
    public void Build_StatsCounts_UnitDeviceClassAndLongTermStatistics(string key, string? deviceClass, string unit)
    {
        var c = Build().GetProperty("cmps").GetProperty(key);

        c.GetProperty("p").GetString().Should().Be("sensor");
        c.GetProperty("unit_of_measurement").GetString().Should().Be(unit);
        c.GetProperty("state_class").GetString().Should().Be("measurement");
        if (deviceClass is null) c.TryGetProperty("device_class", out _).Should().BeFalse();
        else c.GetProperty("device_class").GetString().Should().Be(deviceClass);
    }

    [Fact]
    public void Build_RecentQsos_TimestampWithoutStateClass()
    {
        var c = Build().GetProperty("cmps").GetProperty("recent_qsos");

        c.GetProperty("device_class").GetString().Should().Be("timestamp");
        c.TryGetProperty("state_class", out _).Should().BeFalse();
        c.TryGetProperty("unit_of_measurement", out _).Should().BeFalse();
    }

    [Fact]
    public void Build_QrzSync_EnumOfItsStates()
    {
        var c = Build().GetProperty("cmps").GetProperty("qrz_sync");

        c.GetProperty("device_class").GetString().Should().Be("enum");
        c.GetProperty("options").EnumerateArray().Select(o => o.GetString()).Should().Equal("ok", "pending", "error", "disabled");
    }

    [Fact]
    public void Build_StatsSensors_StayAvailableWhenTheRigIsDown()
    {
        // Only the device's own availability (Costas running) applies: no expiry, no rig-based availability.
        foreach (var key in new[] { "qsos_today", "qsos_week", "qsos_total", "dxcc_worked_total", "new_dxcc_today", "farthest_today",
                     "session_duration", "recent_qsos", "daily_qsos", "worked_week_map", "qrz_sync", "psk_spots_15m" })
        {
            var c = Build().GetProperty("cmps").GetProperty(key);
            c.TryGetProperty("expire_after", out _).Should().BeFalse(key);
            c.TryGetProperty("availability", out _).Should().BeFalse(key);
            c.TryGetProperty("availability_topic", out _).Should().BeFalse(key);
        }
    }
}
