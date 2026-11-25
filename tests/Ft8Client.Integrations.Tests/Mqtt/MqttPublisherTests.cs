// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Mqtt;
using Ft8Client.Integrations.Mqtt;

namespace Ft8Client.Integrations.Tests.Mqtt;

/// <summary>Publishing station events to a broker: one way, state before "online", nothing stale left behind.</summary>
public class MqttPublisherTests
{
    private static readonly MqttTopics Topics = new("w7lit");

    private static MqttBrokerOptions Options(int port, string? user = null, string? password = null) =>
        new("127.0.0.1", port, false, user, password, $"costas-test-{Guid.NewGuid():N}"[..23]);

    private static IReadOnlyList<MqttOutgoing> State(string session = "{\"state\":\"off\"}") =>
        [new(Topics.Session, session, true), new(Topics.Tx, MqttPayloads.Tx(false), true)];

    [Fact]
    public async Task Start_Connects_StateBeforeOnline()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var p = new MqttPublisher(Options(broker.Port), Topics, () => State());

        p.Start();
        await broker.WaitForAsync(m => m.Topic == Topics.Availability && m.Payload == MqttPayloads.Online);

        var topics = broker.Published.Select(m => m.Topic).ToList();
        topics.IndexOf(Topics.Session).Should().BeLessThan(topics.IndexOf(Topics.Availability));
        topics.IndexOf(Topics.Tx).Should().BeLessThan(topics.IndexOf(Topics.Availability));
        broker.Published.Should().OnlyContain(m => m.Retain);
    }

    [Fact]
    public async Task Start_Connected_NeverSubscribes()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var p = new MqttPublisher(Options(broker.Port), Topics, () => State());

        p.Start();
        await broker.WaitForAsync(m => m.Payload == MqttPayloads.Online);
        p.Publish(new MqttOutgoing(Topics.SessionEvent, "{}", false));
        await broker.WaitForAsync(m => m.Topic == Topics.SessionEvent);

        broker.Subscriptions.Should().Be(0);
    }

    [Fact]
    public async Task Publish_EventWhileConnected_ArrivesNotRetained()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var p = new MqttPublisher(Options(broker.Port), Topics, () => State());
        p.Start();
        await broker.WaitForAsync(m => m.Payload == MqttPayloads.Online);

        p.Publish(new MqttOutgoing(Topics.QsoEvent, "{\"event_type\":\"qso_logged\"}", false));

        var got = await broker.WaitForAsync(m => m.Topic == Topics.QsoEvent);
        got.Retain.Should().BeFalse();
        (await broker.RetainedAsync()).Should().NotContainKey(Topics.QsoEvent);
    }

    [Fact]
    public async Task Publish_RetainedChange_LatestHeldByBroker()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var p = new MqttPublisher(Options(broker.Port), Topics, () => State());
        p.Start();
        await broker.WaitForAsync(m => m.Payload == MqttPayloads.Online);

        p.Publish(new MqttOutgoing(Topics.Tx, "ON", true));
        p.Publish(new MqttOutgoing(Topics.Tx, "OFF", true));
        p.Publish(new MqttOutgoing(Topics.State, "{\"band\":\"20m\"}", true));
        await broker.WaitForAsync(m => m.Topic == Topics.State);

        (await broker.RetainedAsync())[Topics.Tx].Should().Be("OFF");
    }

    [Fact]
    public async Task Publish_EventWhileOffline_Dropped()
    {
        var port = MqttTestBroker.FreePort();
        await using var p = new MqttPublisher(Options(port), Topics, () => State("{\"state\":\"on\"}"));
        p.Start();
        p.Publish(new MqttOutgoing(Topics.SessionEvent, "{\"event_type\":\"session_started\"}", false));

        await using var broker = await MqttTestBroker.StartAsync(port: port);
        await broker.WaitForAsync(m => m.Payload == MqttPayloads.Online);

        broker.Published.Should().NotContain(m => m.Topic == Topics.SessionEvent);
        (await broker.RetainedAsync())[Topics.Session].Should().Be("{\"state\":\"on\"}");
    }

    [Fact]
    public async Task StopAsync_Connected_FinalStateThenOffline()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        var p = new MqttPublisher(Options(broker.Port), Topics, () => State("{\"state\":\"on\"}"));
        p.Start();
        await broker.WaitForAsync(m => m.Payload == MqttPayloads.Online);

        await p.StopAsync([new(Topics.Session, "{\"state\":\"off\",\"reason\":\"app_closed\"}", true)], TimeSpan.FromSeconds(10));

        var retained = await broker.RetainedAsync();
        retained[Topics.Session].Should().Contain("app_closed");
        retained[Topics.Availability].Should().Be(MqttPayloads.Offline);
        var topics = broker.Published.Select(m => m.Topic).ToList();
        topics.LastIndexOf(Topics.Session).Should().BeLessThan(topics.LastIndexOf(Topics.Availability));
        p.Connected.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_WhileAPublishAwaitsItsAcknowledgement_StillSendsTheFinalMessages()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        var p = new MqttPublisher(Options(broker.Port), Topics, () => State("{\"state\":\"on\"}"));
        p.Start();
        await broker.WaitForAsync(m => m.Payload == MqttPayloads.Online);
        // Stop while the publisher is waiting for the broker to acknowledge a message: cancelling that wait used to
        // throw the connection away, so the session's end was never sent.
        broker.AcknowledgeSlowly(Topics.Tx, TimeSpan.FromSeconds(1));
        p.Publish(new(Topics.Tx, "ON", true));
        await broker.WaitForAsync(m => m.Topic == Topics.Tx && m.Payload == "ON");

        await p.StopAsync([new(Topics.SessionEvent, "{\"event\":\"session_ended\",\"reason\":\"app_closed\"}", false)], TimeSpan.FromSeconds(10));

        await broker.WaitForAsync(m => m.Topic == Topics.SessionEvent && m.Payload.Contains("app_closed", StringComparison.Ordinal));
        var events = broker.Published.ToList();
        events.FindIndex(m => m.Topic == Topics.SessionEvent).Should().BeLessThan(
            events.FindLastIndex(m => m.Topic == Topics.Availability && m.Payload == MqttPayloads.Offline), "Costas says offline itself after the final messages");
    }

    [Fact]
    public async Task Publish_EventAsOnlineGoesOut_SentAfterIt()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var p = new MqttPublisher(Options(broker.Port), Topics, () => State());
        // The broker has "online" but the publisher is still waiting for its acknowledgement: an event raised now used
        // to be dropped as if Costas were offline.
        broker.AcknowledgeSlowly(Topics.Availability, TimeSpan.FromSeconds(1));
        p.Start();
        await broker.WaitForAsync(m => m.Topic == Topics.Availability && m.Payload == MqttPayloads.Online);

        p.Publish(new MqttOutgoing(Topics.QsoEvent, "{\"event_type\":\"qso_logged\"}", false));

        await broker.WaitForAsync(m => m.Topic == Topics.QsoEvent);
    }

    [Fact]
    public async Task Start_BrokerStartsLate_ConnectsAfterBackoff()
    {
        var port = MqttTestBroker.FreePort();
        await using var p = new MqttPublisher(Options(port), Topics, () => State());
        p.Start();

        await using var broker = await MqttTestBroker.StartAsync(port: port);

        await broker.WaitForAsync(m => m.Topic == Topics.Availability && m.Payload == MqttPayloads.Online);
    }

    [Fact]
    public async Task TestAsync_GoodBroker_Ok()
    {
        await using var broker = await MqttTestBroker.StartAsync("tony", "secret");

        var r = await MqttPublisher.TestAsync(Options(broker.Port, "tony", "secret"), Topics, CancellationToken.None);

        r.Ok.Should().BeTrue(r.Message);
        r.RoundTrip.Should().NotBeNull();
        (await broker.RetainedAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task TestAsync_WrongPassword_SaysSo()
    {
        await using var broker = await MqttTestBroker.StartAsync("tony", "secret");

        var r = await MqttPublisher.TestAsync(Options(broker.Port, "tony", "wrong"), Topics, CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Message.Should().Be("The broker refused the user name or password.");
    }

    [Fact]
    public async Task TestAsync_NothingListening_SaysSo()
    {
        var port = MqttTestBroker.FreePort();

        var r = await MqttPublisher.TestAsync(Options(port), Topics, CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("127.0.0.1").And.Contain(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ToString_WithPassword_NeverShowsIt()
    {
        Options(1883, "tony", "hunter2").ToString().Should().NotContain("hunter2");
    }
}
