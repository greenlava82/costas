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
            .Should().BeEquivalentTo("qso_logged");
    }
}
