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
using Ft8Client.Data.Log;
using Ft8Client.Data.Reference;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.Tests.Mqtt;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

/// <summary>
/// A station for MQTT service tests: a session and transmitter on the simulated radio, settings in a temporary folder,
/// and the MQTT service publishing to a test broker. Time is a <see cref="ManualClock"/>; the service's one-second timer
/// still runs, so a test moves the clock and then waits for what the timer does.
/// </summary>
public sealed class MqttStation : IAsyncDisposable
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

    private sealed class NoLog : ISessionLog
    {
        public QsoRecord? Write(Ft8Client.Core.Contacts.ContactLogEntry entry, LogContext context) => null;
    }

    /// <summary>A station whose MQTT settings point at <paramref name="port"/> (null: publishing off).</summary>
    public MqttStation(int? port, Mode mode = Mode.Ft8, Action<AppSettings>? configure = null, string? password = null)
    {
        Clock = new ManualClock(Now);
        var freqs = ReferenceData.LoadFrequencies();
        Rig = new SimulatedRig(Clock);
        Output = new NullAudioOutput { Now = () => Clock.UtcNow };
        Tx = new Transmitter(Rig, Output, Clock, freqs);
        Session = new Session(new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", Band = "20m", Mode = mode }, new NoDecoder(),
            new NoAudio(), Tx, Clock, ReferenceData.LoadCountries(), freqs, new NoLog());
        var s = new AppSettings();
        s.Profile.Callsign = "W7LIT";
        s.Mqtt.Enabled = port is not null;
        s.Mqtt.Host = port is null ? string.Empty : "127.0.0.1";
        s.Mqtt.Port = port ?? 1883;
        s.Mqtt.StationId = "w7lit";
        s.Mqtt.ClientId = $"costas-{Guid.NewGuid():N}"[..19];
        configure?.Invoke(s);
        Settings = new AppSettingsAccessor(new SettingsStore(Path.Combine(_dir, "settings.json")), s);
        Secrets = new InMemorySecretStore();
        if (password is not null) Secrets.Set(SecretNames.MqttPassword, password);
        Service = new MqttService(Session, Tx, Settings, Secrets, Clock, allowed: true);
    }

    public ManualClock Clock { get; }

    public SimulatedRig Rig { get; }

    public NullAudioOutput Output { get; }

    public Transmitter Tx { get; }

    public Session Session { get; }

    public AppSettingsAccessor Settings { get; }

    public InMemorySecretStore Secrets { get; }

    public MqttService Service { get; }

    /// <summary>The topics for station id w7lit.</summary>
    public MqttTopics Topics { get; } = new("w7lit");

    /// <summary>The MQTT service's status as the session shows it, or null before it has one.</summary>
    public ServiceStatus? Status => Session.Snapshot.Services.GetValueOrDefault(ServiceNames.Mqtt);

    /// <summary>Waits (up to 30 s) for <paramref name="condition"/>.</summary>
    public static async Task Until(Func<bool> condition, string because)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(20);
        condition().Should().BeTrue(because);
    }

    /// <summary>Waits (up to 30 s) until the broker holds <paramref name="topic"/> retained with a payload matching <paramref name="match"/>.</summary>
    public static async Task<string> RetainedAsync(MqttTestBroker broker, string topic, Func<string, bool> match)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        string? last = null;
        while (DateTime.UtcNow < until)
        {
            if ((await broker.RetainedAsync()).TryGetValue(topic, out last) && match(last)) return last;
            await Task.Delay(20);
        }
        throw new TimeoutException($"{topic} was not retained as expected within 30 s; last \"{last}\".");
    }

    /// <summary>Waits for Costas to say online, after its current state.</summary>
    public Task OnlineAsync(MqttTestBroker broker) =>
        RetainedAsync(broker, Topics.Availability, p => p == MqttPayloads.Online);

    /// <summary>Keys a one-second tune and plays it to the end, as the audio device would.</summary>
    public async Task TuneAsync()
    {
        await KeyTuneAsync();
        FinishTune();
    }

    /// <summary>Keys a one-second tune and leaves it keyed until <see cref="FinishTune"/>.</summary>
    public async Task KeyTuneAsync() =>
        (await Tx.TuneAsync(1500, 1.0, new TxGuardInput("20m", 14_074_000, null, 2.0, null, true))).Should().BeNull();

    /// <summary>
    /// Plays a keyed tune to its end. Retained state is coalesced (only the latest per topic is sent), so a test that
    /// wants to see "ON" waits for it before calling this.
    /// </summary>
    public void FinishTune()
    {
        for (var i = 0; i < 300 && Tx.Transmitting; i++)
        {
            Clock.Advance(TimeSpan.FromMilliseconds(10));
            Tx.Poll();
        }
        Tx.Transmitting.Should().BeFalse("the tone ends after a second");
    }

    /// <summary>Marks the radio connected (or lost), as the rig service does.</summary>
    public void Radio(ServiceHealth health) => Session.SetService(ServiceNames.Radio, new ServiceStatus(health, health.ToString(), Clock.UtcNow));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
