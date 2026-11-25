// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Mqtt;
using Ft8Client.Core.Services;
using Ft8Client.Integrations.Mqtt;

namespace Ft8Client.Integrations.Tests.Mqtt;

/// <summary>
/// The publisher against a real Mosquitto broker (what most Home Assistant installs run), so what the in-process test
/// broker allows is also what the real one does: retained state, the last will, keep-alive, passwords and restarts.
/// Runs in Linux CI, where Mosquitto is installed; skipped where it is not.
/// </summary>
public class MosquittoTests
{
    private static readonly MqttTopics Topics = new("w7lit");

    private static MqttBrokerOptions Options(int port, string? user = null, string? password = null) =>
        new("127.0.0.1", port, false, user, password, $"costas-test-{Guid.NewGuid():N}"[..23]);

    private static IReadOnlyList<MqttOutgoing> State(string session) =>
        [new(Topics.Session, session, true), new(Topics.Tx, MqttPayloads.Tx(false), true)];

    [Fact]
    public async Task Start_StateRetainedBeforeOnline_EventsNotRetained()
    {
        await using var broker = await MosquittoBroker.StartAsync();
        await using var p = new MqttPublisher(Options(broker.Port), Topics, () => State("{\"state\":\"off\"}"));
        p.Start();

        await broker.WaitForAsync(m => m.Topic == Topics.Availability && m.Payload == MqttPayloads.Online);
        p.Publish(new MqttOutgoing(Topics.QsoEvent, "{\"event_type\":\"qso_logged\"}", false));
        p.Publish(new MqttOutgoing(Topics.Tx, "ON", true));

        var qso = await broker.WaitForAsync(m => m.Topic == Topics.QsoEvent);
        qso.Retain.Should().BeFalse();
        var seen = broker.Published.ToList();
        seen.FindIndex(m => m.Topic == Topics.Session).Should().BeLessThan(seen.FindIndex(m => m.Topic == Topics.Availability));
        seen.First(m => m.Topic == Topics.Availability).Retain.Should().BeTrue();
        (await broker.RetainedAsync(Topics.Tx, v => v == "ON")).Should().Be("ON");
        (await broker.RetainedAsync(Topics.Session, v => v.Contains("off", StringComparison.Ordinal))).Should().NotBeNull();
        (await broker.RetainedAsync(Topics.QsoEvent, null)).Should().BeNull("events are never retained");
    }

    [Fact]
    public async Task StopAsync_FinalStateThenOfflineRetained()
    {
        await using var broker = await MosquittoBroker.StartAsync();
        var p = new MqttPublisher(Options(broker.Port), Topics, () => State("{\"state\":\"on\"}"));
        p.Start();
        await broker.WaitForAsync(m => m.Topic == Topics.Availability && m.Payload == MqttPayloads.Online);

        await p.StopAsync([new(Topics.Session, "{\"state\":\"off\",\"reason\":\"app_closed\"}", true)], TimeSpan.FromSeconds(5));
        await p.DisposeAsync();

        await broker.RetainedAsync(Topics.Availability, v => v == MqttPayloads.Offline);
        await broker.RetainedAsync(Topics.Session, v => v.Contains("app_closed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Password_WrongRefusedInPlainWords_RightOneConnects()
    {
        await using var broker = await MosquittoBroker.StartAsync("costas", "s3cret");

        var wrong = await MqttPublisher.TestAsync(Options(broker.Port, "costas", "nope"), Topics, TestContext.Current.CancellationToken);
        var none = await MqttPublisher.TestAsync(Options(broker.Port), Topics, TestContext.Current.CancellationToken);
        var right = await MqttPublisher.TestAsync(Options(broker.Port, "costas", "s3cret"), Topics, TestContext.Current.CancellationToken);

        wrong.Ok.Should().BeFalse();
        wrong.Message.Should().Be("The broker refused the user name or password.");
        none.Ok.Should().BeFalse();
        none.Message.Should().Be("The broker refused the user name or password.");
        right.Ok.Should().BeTrue(right.Message);
        (await broker.RetainedAsync($"{Topics.Base}/test", null)).Should().BeNull("the connection test publishes nothing retained");

        await using var p = new MqttPublisher(Options(broker.Port, "costas", "s3cret"), Topics, () => State("{}"));
        p.Start();
        await broker.RetainedAsync(Topics.Availability, v => v == MqttPayloads.Online);
    }

    [Fact]
    public async Task ConnectionCut_MosquittoPublishesTheLastWill()
    {
        await using var broker = await MosquittoBroker.StartAsync();
        await using var proxy = new TcpProxy(broker.Port);
        await using var p = new MqttPublisher(Options(proxy.Port), Topics, () => State("{}"));
        p.Start();
        await broker.RetainedAsync(Topics.Availability, v => v == MqttPayloads.Online);

        proxy.Target = MqttTestBroker.FreePort();
        proxy.Cut();

        var will = await broker.WaitForAsync(m => m.Topic == Topics.Availability && m.Payload == MqttPayloads.Offline);
        will.Retain.Should().BeTrue();
        await broker.RetainedAsync(Topics.Availability, v => v == MqttPayloads.Offline);
    }

    [Fact]
    public async Task Idle_LongerThanTheKeepAlive_MosquittoKeepsTheConnection()
    {
        await using var broker = await MosquittoBroker.StartAsync();
        await using var proxy = new TcpProxy(broker.Port);
        await using var p = new MqttPublisher(Options(proxy.Port), Topics, () => State("{}"));
        p.Start();
        await broker.WaitForAsync(m => m.Topic == Topics.Availability && m.Payload == MqttPayloads.Online);

        await Task.Delay(MqttBrokerOptions.KeepAlive * 2.5, TestContext.Current.CancellationToken);

        proxy.Accepted.Should().Be(1, "Mosquitto enforces the keep-alive; the client's pings must hold the connection");
        broker.Published.Should().NotContain(m => m.Topic == Topics.Availability && m.Payload == MqttPayloads.Offline);
        p.Connected.Should().BeTrue();
    }

    [Fact]
    public async Task LinkFrozen_MosquittoGivesUpAfterTheKeepAlive_CostasReconnectsAfter()
    {
        await using var broker = await MosquittoBroker.StartAsync();
        await using var proxy = new TcpProxy(broker.Port);
        var statuses = new List<ServiceStatus>();
        await using var p = new MqttPublisher(Options(proxy.Port), Topics, () => State("{\"state\":\"off\"}"));
        p.StatusChanged += s =>
        {
            lock (statuses) statuses.Add(s);
        };
        p.Start();
        await broker.RetainedAsync(Topics.Availability, v => v == MqttPayloads.Online);

        proxy.Freeze(true);

        // A frozen PC: Home Assistant must see Costas offline within about 1.5 keep-alives.
        var will = DateTime.UtcNow;
        await broker.WaitForAsync(m => m.Topic == Topics.Availability && m.Payload == MqttPayloads.Offline);
        (DateTime.UtcNow - will).Should().BeLessThan(TimeSpan.FromSeconds(25));

        proxy.Freeze(false);
        proxy.Cut();
        await broker.RetainedAsync(Topics.Availability, v => v == MqttPayloads.Online);
        lock (statuses) statuses.Should().Contain(s => s.Health != ServiceHealth.Ok);
    }

    [Fact]
    public async Task BrokerRestarted_CostasReconnectsAndRestoresItsRetainedState()
    {
        await using var broker = await MosquittoBroker.StartAsync();
        await using var p = new MqttPublisher(Options(broker.Port), Topics, () => State("{\"state\":\"on\"}"));
        p.Start();
        await broker.RetainedAsync(Topics.Availability, v => v == MqttPayloads.Online);

        // Mosquitto without persistence forgets everything retained when it restarts (a Home Assistant add-on update).
        await broker.StopAsync();
        await broker.RestartAsync();

        await broker.RetainedAsync(Topics.Session, v => v.Contains("on", StringComparison.Ordinal));
        await broker.RetainedAsync(Topics.Tx, v => v == "OFF");
        await broker.RetainedAsync(Topics.Availability, v => v == MqttPayloads.Online);
    }

    [Fact]
    public async Task EmptyRetainedMessage_ClearsTheTopic()
    {
        await using var broker = await MosquittoBroker.StartAsync();
        await using var p = new MqttPublisher(Options(broker.Port), Topics, () => [new(Topics.Discovery, "{\"dev\":{}}", true)]);
        p.Start();
        await broker.RetainedAsync(Topics.Discovery, v => v.Length > 0);

        // How Costas removes its Home Assistant device when discovery is turned off.
        p.Publish(new MqttOutgoing(Topics.Discovery, string.Empty, true));

        (await broker.RetainedAsync(Topics.Discovery, null)).Should().BeNull();
    }
}
