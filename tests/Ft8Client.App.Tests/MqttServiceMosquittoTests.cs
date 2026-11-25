// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Mqtt;
using Ft8Client.Core.Services;
using Ft8Client.Integrations.Tests.Mqtt;

namespace Ft8Client.App.Tests;

/// <summary>
/// The whole MQTT feature against a real Mosquitto broker with a password, as a Home Assistant user runs it: the
/// device appears, a transmission starts a session, the radio and band show, and closing Costas ends the session and
/// says offline. Runs in Linux CI; skipped where Mosquitto is not installed.
/// </summary>
public sealed class MqttServiceMosquittoTests
{
    [Fact]
    public async Task WholeEvening_AgainstMosquitto_WhatHomeAssistantSees()
    {
        await using var broker = await MosquittoBroker.StartAsync("costas", "s3cret");
        var st = new MqttStation(broker.Port, configure: s => s.Mqtt.Username = "costas", password: "s3cret");
        st.Service.Start();
        st.Radio(ServiceHealth.Ok);

        // The device appears in Home Assistant with its entities, then Costas says online.
        var discovery = await broker.RetainedAsync(st.Topics.Discovery, v => v.Length > 0);
        var config = System.Text.Json.Nodes.JsonNode.Parse(discovery!)!;
        config["availability_topic"]!.GetValue<string>().Should().Be(st.Topics.Availability);
        config["cmps"]!.AsObject().Select(c => c.Key).Should().Contain(["session_active", "transmitting", "rig_connected", "state"]);
        await broker.RetainedAsync(st.Topics.Availability, v => v == MqttPayloads.Online);
        await broker.RetainedAsync(st.Topics.State, v => v.Contains("\"rig_connected\":true", StringComparison.Ordinal)
                                                       && v.Contains("\"frequency\"", StringComparison.Ordinal));
        await MqttStation.Until(() => st.Status?.Health == ServiceHealth.Ok, "the status says publishing");

        // An over starts a session: the lamp in the shack comes on.
        await st.TuneAsync();
        await broker.WaitForAsync(m => m.Topic == st.Topics.Tx && m.Payload == "ON" && m.Retain);
        await broker.WaitForAsync(m => m.Topic == st.Topics.SessionEvent && m.Payload.Contains("session_started", StringComparison.Ordinal) && !m.Retain);
        await broker.RetainedAsync(st.Topics.Tx, v => v == "OFF");
        await broker.RetainedAsync(st.Topics.Session, v => v.Contains("\"state\":\"on\"", StringComparison.Ordinal));

        // Closing Costas ends the session and says offline itself.
        await st.DisposeAsync();

        await broker.WaitForAsync(m => m.Topic == st.Topics.SessionEvent && m.Payload.Contains("app_closed", StringComparison.Ordinal));
        await broker.RetainedAsync(st.Topics.Session, v => v.Contains("\"state\":\"off\"", StringComparison.Ordinal) && v.Contains("app_closed", StringComparison.Ordinal));
        await broker.RetainedAsync(st.Topics.Availability, v => v == MqttPayloads.Offline);
    }
}
