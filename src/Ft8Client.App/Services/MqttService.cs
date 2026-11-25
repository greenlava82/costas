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
using Ft8Client.Core.Time;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.Mqtt;

namespace Ft8Client.App.Services;

/// <summary>
/// Publishes station events to the operator's MQTT broker (Settings › MQTT): the on-air session, PTT, station state and
/// logged contacts, plus Home Assistant discovery when wanted. One way only: nothing is read back from the broker, so
/// it can never affect transmitting. Off in simulation, so replayed recordings never switch anything in the house.
/// </summary>
public sealed class MqttService : IAsyncDisposable
{
    private readonly Session _session;
    private readonly Transmitter _tx;
    private readonly AppSettingsAccessor _settings;
    private readonly ISecretStore _secrets;
    private readonly IClock _clock;
    private readonly bool _allowed;
    private readonly OnAirSession _onAir;
    private readonly SemaphoreSlim _configure = new(1, 1);
    private readonly object _gate = new();
    private MqttPublisher? _publisher;
    private string? _publisherKey;
    private MqttTopics? _topics;
    private bool _discovery;
    private StationState? _state;
    private LoggedContact? _lastLogged;
    private bool _seenSnapshot;
    private bool _rigWasUp;
    private bool _publishContacts;
    private Timer? _timer;
    private long _lastHeartbeatTicks;
    private volatile bool _disposed;
    // The discovery payload last queued, so a change of callsign (the device name) or version is sent again.
    private string? _discoveryPayload;
    // Discovery was turned off while running: the empty config that removes the device goes out on every connect, in
    // case the broker was unreachable when it was turned off.
    private bool _removeDiscovery;
    private bool _started;

    /// <summary>Creates the service; nothing connects until <see cref="Start"/> and only when turned on.</summary>
    public MqttService(Session session, Transmitter tx, AppSettingsAccessor settings, ISecretStore secrets, IClock clock, bool allowed)
    {
        _session = session;
        _tx = tx;
        _settings = settings;
        _secrets = secrets;
        _clock = clock;
        _allowed = allowed;
        var o = settings.Current.Mqtt;
        _onAir = new OnAirSession(clock, OnAirSessionOptions.FromMinutes(o.SessionIdleMinutes, o.SessionMaxMinutes));
    }

    /// <summary>The on-air session as it stands (for the settings page).</summary>
    public OnAirSnapshot OnAir => _onAir.Snapshot;

    /// <summary>When the broker last accepted a message, or null.</summary>
    public DateTime? LastPublishUtc => _publisher?.LastPublishUtc;

    /// <summary>Hooks up the transmitter and session and connects when publishing is on.</summary>
    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _onAir.Changed += OnAirChanged;
        _tx.Committed += OnCommitted;
        _tx.PttChanged += OnPtt;
        // Belt and braces: an over that ends or faults without a clean release must not leave "transmitting" on.
        _tx.Ended += OnTxEnded;
        _tx.StreamEnded += OnStreamEnded;
        _tx.Fault += OnTxFault;
        _session.Changed += OnSessionChanged;
        _settings.Changed += OnSettingsChanged;
        _timer = new Timer(_ => OnTick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _ = ConfigureAsync();
    }

    // The transmitter raises these on its keying path, which must never fail because of MQTT: nothing escapes them.
    private void OnCommitted() => Safely(_onAir.Commit);

    private void OnPtt(bool keyed) => Safely(() => _onAir.Ptt(keyed));

    private void OnTxEnded(PreparedTx tx, bool completed) => OnPtt(false);

    private void OnStreamEnded(bool completed) => OnPtt(false);

    private void OnTxFault(string why) => OnPtt(false);

    private static void Safely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Serilog.Log.Warning(ex, "MQTT publishing failed to queue a change");
        }
    }

    /// <summary>Connects once to test the settings, without disturbing the running connection.</summary>
    public Task<MqttTestResult> TestAsync(MqttOptions options, string? password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var stationId = MqttStationId.From(string.IsNullOrWhiteSpace(options.StationId) ? _settings.Current.Profile.Callsign : options.StationId);
        var broker = new MqttBrokerOptions(options.Host.Trim(), options.Port, options.Tls, NullIfEmpty(options.Username), NullIfEmpty(password),
            string.IsNullOrEmpty(options.ClientId) ? NewClientId() : options.ClientId);
        return MqttPublisher.TestAsync(broker, new MqttTopics(stationId, DiscoveryPrefix(options)), ct);
    }

    /// <summary>A client id for a new install: "costas-" and 12 random characters.</summary>
    public static string NewClientId() => $"costas-{Guid.NewGuid():N}"[..19];

    private static string DiscoveryPrefix(MqttOptions o) => string.IsNullOrWhiteSpace(o.DiscoveryPrefix) ? "homeassistant" : o.DiscoveryPrefix.Trim();

    private void OnSettingsChanged(AppSettings changed)
    {
        if (!_disposed) _ = ConfigureAsync();
    }

    private void OnAirChanged(OnAirSnapshot s) => Safely(() => QueueOnAir(s));

    private void QueueOnAir(OnAirSnapshot s)
    {
        // Raised under the session's lock: only queue.
        var p = _publisher;
        var t = _topics;
        if (p is null || t is null) return;
        p.Publish(new MqttOutgoing(t.Session, MqttPayloads.Session(s), true));
        p.Publish(new MqttOutgoing(t.Tx, MqttPayloads.Tx(s.Transmitting), true));
        lock (_gate)
        {
            if (s.Active != _lastActive)
            {
                _lastActive = s.Active;
                p.Publish(new MqttOutgoing(t.SessionEvent, MqttPayloads.SessionEvent(s), false));
            }
        }
    }

    private bool _lastActive;

    private void OnSessionChanged(SessionSnapshot s) => Safely(() => QueueSession(s));

    private void QueueSession(SessionSnapshot s)
    {
        var rigUp = s.Services.TryGetValue(ServiceNames.Radio, out var radio) && radio.Health is ServiceHealth.Ok or ServiceHealth.Degraded;
        var activity = !rigUp ? StationActivity.Idle
            : s.Contact is not null ? StationActivity.InQso
            : s.CallingCq ? StationActivity.CallingCq
            : StationActivity.Listening;
        var state = new StationState(s.MyCall, rigUp, activity, s.Band, s.DialHz, ModeInfo.Name(s.Mode));
        MqttPublisher? p;
        MqttTopics? t;
        LoggedContact? logged = null;
        bool rigLost;
        lock (_gate)
        {
            p = _publisher;
            t = _topics;
            rigLost = _rigWasUp && !rigUp;
            _rigWasUp = rigUp;
            var changed = state != _state;
            _state = state;
            if (!changed) state = null!;
            if (s.LastLogged is { } l && l != _lastLogged)
            {
                if (_seenSnapshot) logged = l; // not one already there when the service started
                _lastLogged = l;
            }
            _seenSnapshot = true;
        }
        if (rigLost) _onAir.End(SessionEndReason.RigLost);
        if (p is null || t is null) return;
        if (state is not null) p.Publish(new MqttOutgoing(t.State, MqttPayloads.State(state), true));
        if (logged is not null && _publishContacts)
            p.Publish(new MqttOutgoing(t.QsoEvent, MqttPayloads.QsoEvent(new LoggedQso(logged.Call, logged.Band, ModeInfo.Name(logged.Mode), s.DialHz, logged.LoggedUtc)), false));
    }

    // A timer callback: an exception here would end the process.
    private void OnTick() => Safely(Tick);

    private void Tick()
    {
        if (_disposed) return;
        _onAir.Tick();
        var now = _clock.Ticks;
        if (now - Interlocked.Read(ref _lastHeartbeatTicks) < TimeSpan.FromSeconds(HaDiscovery.HeartbeatSeconds).Ticks) return;
        Interlocked.Exchange(ref _lastHeartbeatTicks, now);
        var p = _publisher;
        if (p is null || !p.Connected) return;
        // Republished on a timer so Home Assistant's expire_after notices a Costas that has frozen.
        foreach (var m in Current()) p.Publish(m);
    }

    // The retained state as it stands: discovery first, so Home Assistant has the entities before their state.
    private IReadOnlyList<MqttOutgoing> CurrentWithDiscovery()
    {
        var t = _topics!;
        var list = new List<MqttOutgoing>();
        if (_discovery) list.Add(new MqttOutgoing(t.Discovery, _discoveryPayload = Discovery(t), true));
        else if (_removeDiscovery) list.Add(new MqttOutgoing(t.Discovery, string.Empty, true));
        list.AddRange(Current());
        return list;
    }

    private IReadOnlyList<MqttOutgoing> Current()
    {
        var t = _topics;
        if (t is null) return [];
        var s = _onAir.Snapshot;
        var list = new List<MqttOutgoing>
        {
            new(t.Session, MqttPayloads.Session(s), true),
            new(t.Tx, MqttPayloads.Tx(s.Transmitting), true),
        };
        StationState? state;
        lock (_gate) state = _state;
        if (state is not null) list.Add(new MqttOutgoing(t.State, MqttPayloads.State(state), true));
        return list;
    }

    private async Task ConfigureAsync()
    {
        await _configure.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            var o = _settings.Current.Mqtt;
            _onAir.Options = OnAirSessionOptions.FromMinutes(o.SessionIdleMinutes, o.SessionMaxMinutes);
            _publishContacts = o.PublishContacts;
            if (!_allowed || !o.Enabled || string.IsNullOrWhiteSpace(o.Host))
            {
                await StopPublisherAsync().ConfigureAwait(false);
                _session.SetService(ServiceNames.Mqtt, ServiceStatus.Off(!_allowed ? "MQTT off in simulation" : "MQTT publishing off"));
                return;
            }
            // The station id and client id are fixed the first time publishing is turned on, then kept.
            if (string.IsNullOrWhiteSpace(o.StationId) || string.IsNullOrWhiteSpace(o.ClientId))
            {
                var call = _settings.Current.Profile.Callsign;
                _settings.Update(s =>
                {
                    if (string.IsNullOrWhiteSpace(s.Mqtt.StationId)) s.Mqtt.StationId = MqttStationId.From(call);
                    if (string.IsNullOrWhiteSpace(s.Mqtt.ClientId)) s.Mqtt.ClientId = NewClientId();
                });
                return; // the settings change runs this again with the new ids
            }
            var password = _secrets.Get(SecretNames.MqttPassword);
            var broker = new MqttBrokerOptions(o.Host.Trim(), o.Port, o.Tls, NullIfEmpty(o.Username), NullIfEmpty(password), o.ClientId);
            var topics = new MqttTopics(MqttStationId.From(o.StationId), DiscoveryPrefix(o));
            var key = $"{broker}|{broker.Username}|{password?.GetHashCode(StringComparison.Ordinal)}|{broker.ClientId}|{topics}";
            if (key == _publisherKey && _publisher is not null)
            {
                if (_discovery != o.HomeAssistantDiscovery)
                {
                    _discovery = o.HomeAssistantDiscovery;
                    _removeDiscovery = !_discovery;
                    // An empty retained message removes the device from Home Assistant; a new one adds it back.
                    _discoveryPayload = _discovery ? Discovery(topics) : null;
                    _publisher.Publish(new MqttOutgoing(topics.Discovery, _discoveryPayload ?? string.Empty, true));
                }
                else if (_discovery && Discovery(topics) is var payload && payload != _discoveryPayload)
                {
                    // The callsign (in the device name) changed: Home Assistant renames the device.
                    _discoveryPayload = payload;
                    _publisher.Publish(new MqttOutgoing(topics.Discovery, payload, true));
                }
                return;
            }
            await StopPublisherAsync().ConfigureAwait(false);
            if (_topics is not null && _topics != topics) _removeDiscovery = false; // a different device now
            _topics = topics;
            _discovery = o.HomeAssistantDiscovery;
            _session.SetService(ServiceNames.Mqtt, new ServiceStatus(ServiceHealth.Degraded, $"Connecting to {broker}", _clock.UtcNow));
            var publisher = new MqttPublisher(broker, topics, CurrentWithDiscovery);
            publisher.StatusChanged += status => _session.SetService(ServiceNames.Mqtt, status);
            _publisher = publisher;
            _publisherKey = key;
            publisher.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Serilog.Log.Warning("MQTT publishing could not be set up: {Reason}", ex.Message);
            _session.SetService(ServiceNames.Mqtt, new ServiceStatus(ServiceHealth.Down, "MQTT publishing could not start", _clock.UtcNow));
        }
        finally
        {
            _configure.Release();
        }
    }

    private string Discovery(MqttTopics t) => HaDiscovery.Build(t, _settings.Current.Profile.Callsign, AppInfo.Version);

    private async Task StopPublisherAsync()
    {
        var p = _publisher;
        _publisher = null;
        _publisherKey = null;
        if (p is null) return;
        await p.StopAsync([], TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        await p.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Ends any session (reason app_closed), publishes it and "offline", and disconnects.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _configure.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_timer is not null) await _timer.DisposeAsync().ConfigureAwait(false);
            _session.Changed -= OnSessionChanged;
            _settings.Changed -= OnSettingsChanged;
            _tx.Committed -= OnCommitted;
            _tx.PttChanged -= OnPtt;
            _tx.Ended -= OnTxEnded;
            _tx.StreamEnded -= OnStreamEnded;
            _tx.Fault -= OnTxFault;
            _onAir.Changed -= OnAirChanged;
            _onAir.Ptt(false);
            _onAir.End(SessionEndReason.AppClosed);
            var p = _publisher;
            _publisher = null;
            if (p is not null)
            {
                var t = _topics!;
                var s = _onAir.Snapshot;
                await p.StopAsync(
                [
                    new MqttOutgoing(t.Session, MqttPayloads.Session(s), true),
                    new MqttOutgoing(t.Tx, MqttPayloads.Tx(false), true),
                    new MqttOutgoing(t.SessionEvent, MqttPayloads.SessionEvent(s), false),
                ], TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                await p.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // Not disposed: a settings change already on its way waits on it, finds the service disposed and returns.
            _configure.Release();
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
