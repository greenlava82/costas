// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.Audio.Backends;
using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Mqtt;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Data.Reference;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.Tests.Mqtt;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

/// <summary>
/// The MQTT service end to end against an in-process broker: what reaches the broker when publishing is turned on, a
/// transmission keys and releases, Home Assistant discovery is switched off, and Costas closes.
/// </summary>
public sealed class MqttServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 24, 20, 15, 0, DateTimeKind.Utc);
    private readonly string _dir = Directory.CreateTempSubdirectory("costas-mqtt-").FullName;

    private sealed class NoAudio : ISlotSource
    {
        public void BeginSlot(DateTime slotStartUtc, Mode mode)
        {
        }

        public void CancelSlot()
        {
        }

        public SlotAudio? Cutoff(DateTime slotStartUtc, Mode mode) => null;
    }

    private sealed class NoDecoder : IDecoder
    {
        public Task<DecodeResult> DecodeAsync(SlotAudio slot, DecodeContext ctx, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed record Station(MqttService Service, Session Session, Transmitter Tx, ManualClock Clock, AppSettingsAccessor Settings, MqttTopics Topics);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Station Create(MqttTestBroker? broker, bool allowed = true, bool discovery = true)
    {
        var clock = new ManualClock(Now);
        var freqs = ReferenceData.LoadFrequencies();
        var tx = new Transmitter(new SimulatedRig(clock), new NullAudioOutput { Now = () => clock.UtcNow }, clock, freqs);
        var session = new Session(new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", Band = "20m", Mode = Mode.Ft8 }, new NoDecoder(),
            new NoAudio(), tx, clock, ReferenceData.LoadCountries(), freqs);
        var s = new AppSettings();
        s.Profile.Callsign = "W7LIT";
        s.Mqtt.Enabled = broker is not null;
        s.Mqtt.Host = broker is null ? string.Empty : "127.0.0.1";
        s.Mqtt.Port = broker?.Port ?? 1883;
        s.Mqtt.StationId = "w7lit";
        s.Mqtt.ClientId = "costas-test";
        s.Mqtt.HomeAssistantDiscovery = discovery;
        var settings = new AppSettingsAccessor(new SettingsStore(Path.Combine(_dir, "settings.json")), s);
        var service = new MqttService(session, tx, settings, new InMemorySecretStore(), clock, allowed);
        return new Station(service, session, tx, clock, settings, new MqttTopics("w7lit"));
    }

    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(20, TestContext.Current.CancellationToken);
        condition().Should().BeTrue("the condition should come true within 15 s");
    }

    [Fact]
    public async Task Start_Enabled_PublishesDiscoveryStateAndOnline()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        var st = Create(broker);
        await using var service = st.Service;
        service.Start();

        await broker.WaitForAsync(m => m.Topic == st.Topics.Availability && m.Payload == MqttPayloads.Online);
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!(await broker.RetainedAsync()).ContainsKey(st.Topics.Availability) && DateTime.UtcNow < until) await Task.Delay(20, TestContext.Current.CancellationToken);

        var retained = await broker.RetainedAsync();
        retained.Should().ContainKey(st.Topics.Discovery);
        retained[st.Topics.Tx].Should().Be("OFF");
        retained[st.Topics.Session].Should().Contain("\"state\":\"off\"");
        st.Session.Snapshot.Services[ServiceNames.Mqtt].Health.Should().Be(ServiceHealth.Ok);
    }

    [Fact]
    public async Task Start_Simulating_StaysOffAndPublishesNothing()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        var st = Create(broker, allowed: false);
        await using var service = st.Service;
        service.Start();

        await Until(() => st.Session.Snapshot.Services.TryGetValue(ServiceNames.Mqtt, out var m) && m.Health == ServiceHealth.Off);
        st.Session.Snapshot.Services[ServiceNames.Mqtt].Message.Should().Contain("simulation");
        broker.Published.Should().BeEmpty("replayed recordings never switch anything in the house");
    }

    [Fact]
    public async Task Tune_KeysAndReleases_PublishesSessionAndTxOnThenOff()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        var st = Create(broker);
        await using var service = st.Service;
        service.Start();
        await broker.WaitForAsync(m => m.Topic == st.Topics.Availability && m.Payload == MqttPayloads.Online);

        (await st.Tx.TuneAsync(1500, 1.0, new TxGuardInput("20m", 14_074_000, null, 2.0, null, true))).Should().BeNull();

        await broker.WaitForAsync(m => m.Topic == st.Topics.Tx && m.Payload == "ON");
        await broker.WaitForAsync(m => m.Topic == st.Topics.SessionEvent && m.Payload.Contains("session_started", StringComparison.Ordinal));
        (await broker.WaitForAsync(m => m.Topic == st.Topics.Session && m.Payload.Contains("\"state\":\"on\"", StringComparison.Ordinal))).Retain
            .Should().BeTrue();

        for (var i = 0; i < 300 && st.Tx.Transmitting; i++)
        {
            st.Clock.Advance(TimeSpan.FromMilliseconds(10));
            st.Tx.Poll();
        }
        st.Tx.Transmitting.Should().BeFalse("the tone ends after a second");
        var lastOn = broker.Published.ToList().FindLastIndex(m => m.Topic == st.Topics.Tx && m.Payload == "ON");
        await Until(() => broker.Published.Skip(lastOn + 1).Any(m => m.Topic == st.Topics.Tx && m.Payload == "OFF"));
    }

    [Fact]
    public async Task DiscoveryTurnedOff_PublishesEmptyRetainedConfigToRemoveTheDevice()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        var st = Create(broker);
        await using var service = st.Service;
        service.Start();
        await broker.WaitForAsync(m => m.Topic == st.Topics.Discovery && !string.IsNullOrEmpty(m.Payload));

        st.Settings.Update(s => s.Mqtt.HomeAssistantDiscovery = false);

        // The broker hands an empty payload over as null.
        (await broker.WaitForAsync(m => m.Topic == st.Topics.Discovery && string.IsNullOrEmpty(m.Payload))).Retain.Should().BeTrue();
        // The broker stores retained messages after the publish is seen: wait for it.
        var until = DateTime.UtcNow.AddSeconds(15);
        while ((await broker.RetainedAsync()).ContainsKey(st.Topics.Discovery) && DateTime.UtcNow < until) await Task.Delay(20, TestContext.Current.CancellationToken);
        (await broker.RetainedAsync()).Should().NotContainKey(st.Topics.Discovery, "an empty retained message clears the device");
    }

    [Fact]
    public async Task DisposeAsync_DuringSession_PublishesAppClosedAndOffline()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        var st = Create(broker);
        st.Service.Start();
        await broker.WaitForAsync(m => m.Topic == st.Topics.Availability && m.Payload == MqttPayloads.Online);
        (await st.Tx.TuneAsync(1500, 1.0, new TxGuardInput("20m", 14_074_000, null, 2.0, null, true))).Should().BeNull();
        await broker.WaitForAsync(m => m.Topic == st.Topics.Session && m.Payload.Contains("\"state\":\"on\"", StringComparison.Ordinal));

        await st.Service.DisposeAsync();

        await broker.WaitForAsync(m => m.Topic == st.Topics.SessionEvent && m.Payload.Contains("app_closed", StringComparison.Ordinal));
        await broker.WaitForAsync(m => m.Topic == st.Topics.Availability && m.Payload == MqttPayloads.Offline);
        // The broker stores retained messages after the publish is seen: wait for the last one.
        var until = DateTime.UtcNow.AddSeconds(15);
        while ((await broker.RetainedAsync()).GetValueOrDefault(st.Topics.Availability) != MqttPayloads.Offline && DateTime.UtcNow < until) await Task.Delay(20, TestContext.Current.CancellationToken);
        var retained = await broker.RetainedAsync();
        retained[st.Topics.Availability].Should().Be(MqttPayloads.Offline);
        retained[st.Topics.Tx].Should().Be("OFF");
        retained[st.Topics.Session].Should().Contain("\"state\":\"off\"").And.Contain("app_closed");
    }

    [Fact]
    public async Task Start_Off_StatusOffAndNoConnection()
    {
        var st = Create(broker: null);
        await using var service = st.Service;
        service.Start();

        await Until(() => st.Session.Snapshot.Services.TryGetValue(ServiceNames.Mqtt, out var m) && m.Health == ServiceHealth.Off);
        service.LastPublishUtc.Should().BeNull();
    }
}
