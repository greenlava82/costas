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
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Data.Reference;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.Tests.Mqtt;
using Ft8Client.Modems.Psk;
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
    /// <param name="port">Broker port, or null for publishing off.</param>
    /// <param name="mode">Operating mode.</param>
    /// <param name="configure">Changes to the settings.</param>
    /// <param name="password">MQTT password.</param>
    /// <param name="stats">Log to a real (in-memory) database and publish the dashboard statistics from it.</param>
    /// <param name="now">Start time of the clock.</param>
    public MqttStation(int? port, Mode mode = Mode.Ft8, Action<AppSettings>? configure = null, string? password = null, bool stats = false,
                       DateTime? now = null)
    {
        Clock = new ManualClock(now ?? Now);
        ISessionLog log = new NoLog();
        if (stats)
        {
            Database = SqliteDatabase.InMemory(out var keepAlive);
            _keepAlive = keepAlive;
            Qsos = new QsoRepository(Database);
            Uploads = new UploadQueueRepository(Database);
            Sync = new SyncStateRepository(Database);
            Spots = new SpotRepository(Database);
            log = new DatabaseSessionLog(Qsos, Uploads, () => "home", () => QrzUploads);
        }
        var freqs = ReferenceData.LoadFrequencies();
        Rig = new SimulatedRig(Clock);
        Output = new NullAudioOutput { Now = () => Clock.UtcNow };
        Tx = new Transmitter(Rig, Output, Clock, freqs);
        Session = new Session(new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", Band = "20m", Mode = mode }, new NoDecoder(),
            new NoAudio(), Tx, Clock, ReferenceData.LoadCountries(), freqs, log);
        var s = new AppSettings();
        s.Profile.Callsign = "W7LIT";
        s.Profile.Grid = "DN40";
        s.Mqtt.Enabled = port is not null;
        s.Mqtt.Host = port is null ? string.Empty : "127.0.0.1";
        s.Mqtt.Port = port ?? 1883;
        s.Mqtt.StationId = "w7lit";
        s.Mqtt.ClientId = $"costas-{Guid.NewGuid():N}"[..19];
        configure?.Invoke(s);
        Settings = new AppSettingsAccessor(new SettingsStore(Path.Combine(_dir, "settings.json")), s);
        Secrets = new InMemorySecretStore();
        if (password is not null) Secrets.Set(SecretNames.MqttPassword, password);
        Stats = stats
            ? new DashboardStatsSource(Qsos!, Uploads!, Sync!, Spots, ReferenceData.LoadCountries(), ReferenceData.LoadAdifTable(), Settings, Secrets)
            : null;
        Service = new MqttService(Session, Tx, Settings, Secrets, Clock, allowed: true, Stats);
    }

    private readonly IDisposable? _keepAlive;

    /// <summary>The log database (with <c>stats</c>), else null.</summary>
    public SqliteDatabase? Database { get; }

    /// <summary>The log (with <c>stats</c>), else null.</summary>
    public QsoRepository? Qsos { get; }

    /// <summary>The upload queue (with <c>stats</c>), else null.</summary>
    public UploadQueueRepository? Uploads { get; }

    /// <summary>The sync state (with <c>stats</c>), else null.</summary>
    public SyncStateRepository? Sync { get; }

    /// <summary>The stored spots (with <c>stats</c>), else null.</summary>
    public SpotRepository? Spots { get; }

    /// <summary>The statistics source (with <c>stats</c>), else null.</summary>
    public DashboardStatsSource? Stats { get; }

    /// <summary>Whether a contact logged through the session is queued for QRZ.</summary>
    public bool QrzUploads { get; set; }

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

    /// <summary>Hears KP4JRS call CQ in PSK31, answers and logs the contact.</summary>
    public void WorkKp4jrs(KeyboardSession keyboard)
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
                Clock.Advance(TimeSpan.FromMilliseconds(100));
                keyboard.Drain();
            }
            Clock.Advance(TimeSpan.FromSeconds(4));
            keyboard.Drain();
        }

        Hear("CQ CQ CQ DE KP4JRS KP4JRS KP4JRS PSE K\n");
        keyboard.Listen(partnerHz);
        Session.CallStation("KP4JRS").Should().BeTrue();
        var end = Clock.UtcNow + TimeSpan.FromSeconds(60);
        while (Tx.Transmitting && Clock.UtcNow < end)
        {
            for (var i = 0; i < 10; i++)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(1));
                Tx.Poll();
            }
            Session.Keys.Pump();
            Output.Pull(Tx.OutputSampleRate / 100);
        }
        Tx.Transmitting.Should().BeFalse();
        Session.LogNow();
    }

    /// <summary>Marks the radio connected (or lost), as the rig service does.</summary>
    public void Radio(ServiceHealth health) => Session.SetService(ServiceNames.Radio, new ServiceStatus(health, health.ToString(), Clock.UtcNow));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync();
        _keepAlive?.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
