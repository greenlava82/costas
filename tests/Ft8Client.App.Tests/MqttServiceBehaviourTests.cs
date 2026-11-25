// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.App.Engine;
using Ft8Client.Core;
using Ft8Client.Core.Mqtt;
using Ft8Client.Core.Services;
using Ft8Client.Data.Secrets;
using Ft8Client.Integrations.Tests.Mqtt;
using Ft8Client.Modems.Psk;

namespace Ft8Client.App.Tests;

/// <summary>
/// The MQTT service as the operator meets it: sessions that end by themselves, the heartbeat, the radio dropping out,
/// logged contacts, settings changed while it runs, a broker that is down or refuses the password, and closing.
/// </summary>
public sealed class MqttServiceBehaviourTests
{
    [Fact]
    public async Task Heartbeat_ThirtySecondsPass_RepublishesSessionTxAndState()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port);
        st.Service.Start();
        st.Radio(ServiceHealth.Ok);
        await st.OnlineAsync(broker);
        await MqttStation.RetainedAsync(broker, st.Topics.State, p => p.Contains("\"rig_connected\":true", StringComparison.Ordinal));
        var before = broker.Published.Count;

        st.Clock.Advance(TimeSpan.FromSeconds(HaDiscovery.HeartbeatSeconds + 1));

        // Home Assistant marks the session and transmitting sensors unavailable 90 s after the last of these.
        await broker.WaitForAsync(m => m.Topic == st.Topics.Session && broker.Published.Skip(before).Any(x => x.Topic == st.Topics.Session));
        await MqttStation.Until(() =>
        {
            var since = broker.Published.Skip(before).ToList();
            return since.Any(m => m.Topic == st.Topics.Tx) && since.Any(m => m.Topic == st.Topics.State);
        }, "the heartbeat republishes tx and state too");
        broker.Published.Skip(before).Should().OnlyContain(m => m.Retain, "a heartbeat is state, never an event");
        HaDiscovery.HeartbeatSeconds.Should().BeLessThan(HaDiscovery.ExpireAfterSeconds / 2, "two heartbeats can be lost before Home Assistant gives up");
    }

    [Fact]
    public async Task IdleTimeout_NoTransmitForFiveMinutes_SessionEndsWithIdleTimeout()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port);
        st.Service.Start();
        await st.OnlineAsync(broker);
        await st.TuneAsync();
        await MqttStation.RetainedAsync(broker, st.Topics.Session, p => p.Contains("\"state\":\"on\"", StringComparison.Ordinal));

        st.Clock.Advance(TimeSpan.FromMinutes(4));
        st.Clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        var ended = await broker.WaitForAsync(m => m.Topic == st.Topics.SessionEvent && m.Payload.Contains("session_ended", StringComparison.Ordinal));
        ended.Payload.Should().Contain("\"reason\":\"idle_timeout\"");
        ended.Retain.Should().BeFalse("events are never retained");
        var session = await MqttStation.RetainedAsync(broker, st.Topics.Session, p => p.Contains("\"state\":\"off\"", StringComparison.Ordinal));
        session.Should().Contain("idle_timeout");
        st.Service.OnAir.Active.Should().BeFalse();
    }

    [Fact]
    public async Task IdleMinutesChangedWhileRunning_NewTimeoutApplies()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port);
        st.Service.Start();
        await st.OnlineAsync(broker);

        st.Settings.Update(s => s.Mqtt.SessionIdleMinutes = 1);
        await MqttStation.Until(() => st.Service.OnAir.Active is false && st.Settings.Current.Mqtt.SessionIdleMinutes == 1, "saved");
        // The change is applied by the service on its own task; give it a known point to have done so.
        await MqttStation.Until(() => st.Status?.Health == ServiceHealth.Ok, "still connected after the change");
        await st.TuneAsync();
        await MqttStation.RetainedAsync(broker, st.Topics.Session, p => p.Contains("\"state\":\"on\"", StringComparison.Ordinal));
        st.Clock.Advance(TimeSpan.FromSeconds(61));

        var ended = await broker.WaitForAsync(m => m.Topic == st.Topics.SessionEvent && m.Payload.Contains("session_ended", StringComparison.Ordinal));
        ended.Payload.Should().Contain("idle_timeout");
        broker.Published.Count(m => m.Topic == st.Topics.Availability && m.Payload == MqttPayloads.Online)
            .Should().Be(1, "a timeout change does not reconnect");
    }

    [Fact]
    public async Task RadioLost_DuringSession_EndsItWithRigLostAndSaysTheRadioIsGone()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port);
        st.Service.Start();
        st.Radio(ServiceHealth.Ok);
        await st.OnlineAsync(broker);
        var state = await MqttStation.RetainedAsync(broker, st.Topics.State, p => p.Contains("\"rig_connected\":true", StringComparison.Ordinal));
        state.Should().Contain("\"state\":\"listening\"").And.Contain("\"band\":\"20m\"").And.Contain("\"callsign\":\"W7LIT\"").And.Contain("\"mode\":\"FT8\"");
        await st.TuneAsync();
        await MqttStation.RetainedAsync(broker, st.Topics.Session, p => p.Contains("\"state\":\"on\"", StringComparison.Ordinal));

        st.Radio(ServiceHealth.Down);

        var ended = await broker.WaitForAsync(m => m.Topic == st.Topics.SessionEvent && m.Payload.Contains("session_ended", StringComparison.Ordinal));
        ended.Payload.Should().Contain("rig_lost");
        await MqttStation.RetainedAsync(broker, st.Topics.State, p => p.Contains("\"rig_connected\":false", StringComparison.Ordinal)
                                                                       && p.Contains("\"state\":\"idle\"", StringComparison.Ordinal));
        await MqttStation.RetainedAsync(broker, st.Topics.Session, p => p.Contains("rig_lost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RadioNeverConnected_GoesDown_NoSessionEvent()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port);
        st.Service.Start();
        await st.OnlineAsync(broker);

        st.Radio(ServiceHealth.Down);
        st.Radio(ServiceHealth.Ok);

        await MqttStation.RetainedAsync(broker, st.Topics.State, p => p.Contains("\"rig_connected\":true", StringComparison.Ordinal));
        broker.Published.Should().NotContain(m => m.Topic == st.Topics.SessionEvent, "no session was running");
    }

    [Fact]
    public async Task ContactLogged_PublishesQsoLoggedEvent_NotRetained()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, Mode.Psk31);
        using var keyboard = new KeyboardSession(st.Session, st.Clock);
        st.Service.Start();
        await st.OnlineAsync(broker);

        WorkKp4jrs(st, keyboard);

        var qso = await broker.WaitForAsync(m => m.Topic == st.Topics.QsoEvent);
        qso.Retain.Should().BeFalse();
        qso.Payload.Should().Contain("\"event_type\":\"qso_logged\"").And.Contain("\"call\":\"KP4JRS\"").And.Contain("\"band\":\"20m\"")
            .And.Contain("\"mode\":\"PSK31\"");
        (await broker.RetainedAsync()).Should().NotContainKey(st.Topics.QsoEvent);
    }

    [Fact]
    public async Task ContactLogged_ContactsTurnedOff_NoQsoEvent()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, Mode.Psk31, s => s.Mqtt.PublishContacts = false);
        using var keyboard = new KeyboardSession(st.Session, st.Clock);
        st.Service.Start();
        await st.OnlineAsync(broker);

        WorkKp4jrs(st, keyboard);
        // Anything published after the contact was logged shows the contact's turn has passed.
        st.Radio(ServiceHealth.Ok);
        await broker.WaitForAsync(m => m.Topic == st.Topics.State && m.Payload.Contains("\"rig_connected\":true", StringComparison.Ordinal));

        broker.Published.Should().NotContain(m => m.Topic == st.Topics.QsoEvent);
    }

    [Fact]
    public async Task BrokerMoved_SettingsChanged_LeavesTheOldOneCleanlyAndPublishesToTheNew()
    {
        await using var oldBroker = await MqttTestBroker.StartAsync();
        await using var newBroker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(oldBroker.Port);
        st.Service.Start();
        await st.OnlineAsync(oldBroker);

        st.Settings.Update(s => s.Mqtt.Port = newBroker.Port);

        await st.OnlineAsync(newBroker);
        await MqttStation.RetainedAsync(oldBroker, st.Topics.Availability, p => p == MqttPayloads.Offline);
        (await newBroker.RetainedAsync()).Should().ContainKeys(st.Topics.Discovery, st.Topics.Session, st.Topics.Tx);
        await MqttStation.Until(() => st.Status?.Health == ServiceHealth.Ok && st.Status.Message.Contains(newBroker.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal),
            "the status names the new broker");
    }

    [Fact]
    public async Task WrongPassword_StatusSaysSo_FixingItConnects()
    {
        await using var broker = await MqttTestBroker.StartAsync("tony", "secret");
        await using var st = new MqttStation(broker.Port, configure: s => s.Mqtt.Username = "tony", password: "wrong");
        st.Service.Start();

        await MqttStation.Until(() => st.Status is { Health: ServiceHealth.Down } s && s.Message.Contains("user name or password", StringComparison.Ordinal),
            "the status says the broker refused the password");
        broker.Published.Should().BeEmpty();

        // Settings saves the password to the credential store, then the settings.
        st.Secrets.Set(SecretNames.MqttPassword, "secret");
        st.Settings.Update(s => s.Mqtt.Username = "tony");

        await st.OnlineAsync(broker);
        await MqttStation.Until(() => st.Status?.Health == ServiceHealth.Ok, "connected with the new password");
        st.Status!.Message.Should().NotContain("secret");
    }

    [Fact]
    public async Task BrokerDownAtStart_StatusSaysSo_ConnectsWhenItComesUp()
    {
        var port = MqttTestBroker.FreePort();
        await using var st = new MqttStation(port);
        st.Service.Start();

        await MqttStation.Until(() => st.Status is { Health: ServiceHealth.Down } s && s.Message.Contains("Is the broker running?", StringComparison.Ordinal),
            "the status says nothing answered");
        await using var broker = await MqttTestBroker.StartAsync(port: port);

        await st.OnlineAsync(broker);
        await MqttStation.Until(() => st.Status?.Health == ServiceHealth.Ok, "the status follows");
    }

    [Fact]
    public async Task TurnedOff_WhileRunning_SaysOfflineAndStops()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port);
        st.Service.Start();
        await st.OnlineAsync(broker);

        st.Settings.Update(s => s.Mqtt.Enabled = false);

        await MqttStation.RetainedAsync(broker, st.Topics.Availability, p => p == MqttPayloads.Offline);
        await MqttStation.Until(() => st.Status is { Health: ServiceHealth.Off } s && s.Message == "MQTT publishing off", "the status says off");
        await MqttStation.Until(() => broker.ConnectedClientsAsync().Result == 0, "disconnected");
        var count = broker.Published.Count;
        await st.TuneAsync();
        st.Service.LastPublishUtc.Should().BeNull("nothing publishes while off");
        broker.Published.Count.Should().Be(count);
    }

    [Fact]
    public async Task TurnedOn_FirstTime_MakesStationAndClientIdsAndConnects()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(null, configure: s =>
        {
            s.Mqtt.StationId = string.Empty;
            s.Mqtt.ClientId = string.Empty;
            s.Profile.Callsign = "W7LIT/P";
        });
        st.Service.Start();
        await MqttStation.Until(() => st.Status?.Health == ServiceHealth.Off, "off to begin with");

        st.Settings.Update(s =>
        {
            s.Mqtt.Enabled = true;
            s.Mqtt.Host = " 127.0.0.1 ";
            s.Mqtt.Port = broker.Port;
        });

        var topics = new MqttTopics("w7lit_p");
        await MqttStation.RetainedAsync(broker, topics.Availability, p => p == MqttPayloads.Online);
        st.Settings.Current.Mqtt.StationId.Should().Be("w7lit_p", "kept from now on, so a profile switch does not move the topics");
        st.Settings.Current.Mqtt.ClientId.Should().MatchRegex("^costas-[0-9a-f]{12}$");
        (await broker.RetainedAsync()).Should().ContainKey(topics.Discovery);
    }

    [Fact]
    public async Task CallsignChanged_DiscoveryRepublishedWithTheNewName_TopicsKept()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port);
        st.Service.Start();
        await MqttStation.RetainedAsync(broker, st.Topics.Discovery, p => p.Contains("Costas W7LIT", StringComparison.Ordinal));

        st.Settings.Update(s => s.Profile.Callsign = "K7ABC");

        await MqttStation.RetainedAsync(broker, st.Topics.Discovery, p => p.Contains("Costas K7ABC", StringComparison.Ordinal));
        broker.Published.Count(m => m.Topic == st.Topics.Availability && m.Payload == MqttPayloads.Online).Should().Be(1, "no reconnect");
    }

    [Fact]
    public async Task DiscoveryTurnedOffWhileBrokerUnreachable_RemovedWhenItIsBack()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var proxy = new TcpProxy(broker.Port);
        await using var st = new MqttStation(proxy.Port);
        st.Service.Start();
        await MqttStation.RetainedAsync(broker, st.Topics.Discovery, p => p.Length > 0);
        await st.OnlineAsync(broker);

        proxy.Target = MqttTestBroker.FreePort();
        proxy.Cut();
        await MqttStation.Until(() => st.Status?.Health != ServiceHealth.Ok, "the connection is lost");
        st.Settings.Update(s => s.Mqtt.HomeAssistantDiscovery = false);
        await MqttStation.Until(() => st.Settings.Current.Mqtt.HomeAssistantDiscovery == false, "saved");
        proxy.Target = broker.Port;

        await MqttStation.Until(() => broker.RetainedAsync().Result.ContainsKey(st.Topics.Discovery) == false,
            "the device is removed from Home Assistant once the broker is reachable again");
        await st.OnlineAsync(broker);
    }

    [Fact]
    public async Task DisposeAsync_BrokerDown_ReturnsPromptly()
    {
        var st = new MqttStation(MqttTestBroker.FreePort());
        st.Service.Start();
        await MqttStation.Until(() => st.Status?.Health == ServiceHealth.Down, "trying to connect");

        var close = st.DisposeAsync().AsTask();

        (await Task.WhenAny(close, Task.Delay(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken))).Should().BeSameAs(close,
            "closing Costas must not wait on a broker that is not there");
        await close;
    }

    [Fact]
    public async Task AfterDispose_TransmittingAndSettingsChanges_StillWorkAndPublishNothing()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        var st = new MqttStation(broker.Port);
        st.Service.Start();
        await st.OnlineAsync(broker);
        await st.DisposeAsync();
        var count = broker.Published.Count;

        var change = () => st.Settings.Update(s => s.Mqtt.Port = 1);
        change.Should().NotThrow();
        await st.TuneAsync();
        await st.Service.DisposeAsync(); // twice is harmless

        broker.Published.Count.Should().Be(count);
        st.Rig.PttLog.Should().NotBeEmpty("the transmitter keyed and released as usual");
    }

    [Fact]
    public async Task TestAsync_UsesThePageValues_WithoutDisturbingTheRunningConnection()
    {
        await using var running = await MqttTestBroker.StartAsync();
        await using var other = await MqttTestBroker.StartAsync("tony", "secret");
        await using var st = new MqttStation(running.Port);
        st.Service.Start();
        await st.OnlineAsync(running);

        var ok = await st.Service.TestAsync(new Ft8Client.Data.Settings.MqttOptions { Host = " 127.0.0.1 ", Port = other.Port, Username = "tony" }, "secret",
            TestContext.Current.CancellationToken);
        var refused = await st.Service.TestAsync(new Ft8Client.Data.Settings.MqttOptions { Host = "127.0.0.1", Port = other.Port, Username = "tony" }, "nope",
            TestContext.Current.CancellationToken);

        ok.Ok.Should().BeTrue(ok.Message);
        refused.Message.Should().Be("The broker refused the user name or password.");
        (await other.RetainedAsync()).Should().BeEmpty("a test publishes nothing retained");
        running.Published.Count(m => m.Topic == st.Topics.Availability).Should().Be(1, "the running connection was not touched");
        st.Status!.Health.Should().Be(ServiceHealth.Ok);
    }

    // A PSK31 contact with KP4JRS, logged: what the Logged notice and the qso_logged event come from.
    private static void WorkKp4jrs(MqttStation st, KeyboardSession keyboard)
    {
        const int partnerHz = 1180;
        void Hear(string text)
        {
            var random = new Random(1);
            var audio = new PskModulator(Mode.Psk31, ModeInfo.DecoderSampleRate, partnerHz, 0.3).Modulate(text)
                .Select(s => s + (float)((random.NextDouble() - 0.5) * 0.02)).ToArray();
            for (var i = 0; i < audio.Length; i += 1200)
            {
                keyboard.Input.Write(audio.AsSpan(i, Math.Min(1200, audio.Length - i)));
                st.Clock.Advance(TimeSpan.FromMilliseconds(100));
                keyboard.Drain();
            }
            st.Clock.Advance(TimeSpan.FromSeconds(4));
            keyboard.Drain();
        }

        Hear("CQ CQ CQ DE KP4JRS KP4JRS KP4JRS PSE K\n");
        keyboard.Listen(partnerHz);
        st.Session.CallStation("KP4JRS").Should().BeTrue();
        var end = st.Clock.UtcNow + TimeSpan.FromSeconds(60);
        while (st.Tx.Transmitting && st.Clock.UtcNow < end)
        {
            for (var i = 0; i < 10; i++)
            {
                st.Clock.Advance(TimeSpan.FromMilliseconds(1));
                st.Tx.Poll();
            }
            st.Session.Keys.Pump();
            st.Output.Pull(st.Tx.OutputSampleRate / 100);
        }
        st.Tx.Transmitting.Should().BeFalse();
        st.Session.LogNow();
    }
}
