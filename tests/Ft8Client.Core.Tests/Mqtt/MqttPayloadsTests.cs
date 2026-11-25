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

/// <summary>What Costas publishes on each MQTT topic.</summary>
public class MqttPayloadsTests
{
    private static readonly DateTime T0 = new(2026, 11, 24, 18, 0, 5, DateTimeKind.Utc);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("W7LIT", "w7lit")]
    [InlineData("W7LIT/P", "w7lit_p")]
    [InlineData("VE3/W7LIT/QRP", "ve3_w7lit_qrp")]
    [InlineData("  ", "costas")]
    [InlineData(null, "costas")]
    [InlineData("/W7LIT/", "w7lit")]
    public void From_Callsign_SafeStationId(string? call, string want) =>
        MqttStationId.From(call).Should().Be(want);

    [Fact]
    public void Topics_StationId_AllUnderOneBase()
    {
        var t = new MqttTopics("w7lit");

        t.Availability.Should().Be("costas/w7lit/availability");
        t.Session.Should().Be("costas/w7lit/session");
        t.Tx.Should().Be("costas/w7lit/tx");
        t.State.Should().Be("costas/w7lit/state");
        t.SessionEvent.Should().Be("costas/w7lit/event/session");
        t.QsoEvent.Should().Be("costas/w7lit/event/qso");
        t.Discovery.Should().Be("homeassistant/device/costas_w7lit/config");
    }

    [Fact]
    public void Session_Running_StateOnWithId()
    {
        var j = Parse(MqttPayloads.Session(new OnAirSnapshot(true, "abc", T0, null, true)));

        j.GetProperty("state").GetString().Should().Be("on");
        j.GetProperty("id").GetString().Should().Be("abc");
        j.GetProperty("since").GetString().Should().Be("2026-11-24T18:00:05Z");
        j.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        j.GetProperty("transmitting").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Session_Ended_StateOffWithReason()
    {
        var j = Parse(MqttPayloads.Session(new OnAirSnapshot(false, "abc", T0, SessionEndReason.IdleTimeout, false)));

        j.GetProperty("state").GetString().Should().Be("off");
        j.GetProperty("reason").GetString().Should().Be("idle_timeout");
    }

    [Fact]
    public void SessionEvent_StartAndEnd_EventTypes()
    {
        Parse(MqttPayloads.SessionEvent(new OnAirSnapshot(true, "a", T0, null, false)))
            .GetProperty("event_type").GetString().Should().Be("session_started");
        var end = Parse(MqttPayloads.SessionEvent(new OnAirSnapshot(false, "a", T0, SessionEndReason.RigLost, false)));
        end.GetProperty("event_type").GetString().Should().Be("session_ended");
        end.GetProperty("reason").GetString().Should().Be("rig_lost");
    }

    [Fact]
    public void State_Station_AllFields()
    {
        var j = Parse(MqttPayloads.State(new StationState("W7LIT", true, StationActivity.CallingCq, "20m", 14_074_000, "FT8")));

        j.GetProperty("callsign").GetString().Should().Be("W7LIT");
        j.GetProperty("rig_connected").GetBoolean().Should().BeTrue();
        j.GetProperty("state").GetString().Should().Be("calling_cq");
        j.GetProperty("band").GetString().Should().Be("20m");
        j.GetProperty("frequency").GetInt64().Should().Be(14_074_000);
        j.GetProperty("mode").GetString().Should().Be("FT8");
    }

    [Fact]
    public void QsoEvent_Logged_EventTypeAndDetails()
    {
        var j = Parse(MqttPayloads.QsoEvent(new LoggedQso("K1ABC", "20m", "FT8", 14_074_000, T0)));

        j.GetProperty("event_type").GetString().Should().Be("qso_logged");
        j.GetProperty("call").GetString().Should().Be("K1ABC");
        j.GetProperty("ts").GetString().Should().Be("2026-11-24T18:00:05Z");
    }

    [Fact]
    public void ReasonName_EveryReason_Named()
    {
        foreach (var r in Enum.GetValues<SessionEndReason>()) MqttPayloads.ReasonName(r).Should().NotBeNullOrEmpty();
    }
}
