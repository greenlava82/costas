// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Collections.Concurrent;
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

    // Dashboard statistics (null: not published). Counted from the log on a worker of their own, never on the session's
    // or transmitter's threads.
    private readonly DashboardStatsSource? _stats;
    private readonly SemaphoreSlim _statsSignal = new(0);
    private readonly CancellationTokenSource _statsCts = new();
    private readonly ConcurrentQueue<(LoggedContact Contact, long DialHz)> _pendingContacts = new();
    private readonly Dictionary<string, string> _statsPayloads = new(StringComparer.Ordinal);
    private Task? _statsLoop;
    private volatile bool _statsDirty = true;
    private volatile bool _qrzDirty = true;
    private volatile bool _pskDirty = true;
    private DateTime _statsDay;
    private DashboardStats? _lastStats;
    private (ServiceHealth Health, string Message)? _qrzShown;
    private long _lastPskTicks;
    private int? _sessionMinutes;

    /// <summary>
    /// How long the statistics worker waits after a change before counting, so a burst (an import, a QRZ sync) is
    /// counted once. A logged contact still reaches the broker well inside 5 seconds.
    /// </summary>
    internal static readonly TimeSpan StatsSettle = TimeSpan.FromMilliseconds(400);

    /// <summary>How often psk_spots_15m is counted again.</summary>
    internal static readonly TimeSpan PskInterval = TimeSpan.FromMinutes(1);

    /// <summary>Creates the service; nothing connects until <see cref="Start"/> and only when turned on.</summary>
    /// <param name="stats">Where the dashboard statistics come from, or null to publish none.</param>
    public MqttService(Session session, Transmitter tx, AppSettingsAccessor settings, ISecretStore secrets, IClock clock, bool allowed,
                       DashboardStatsSource? stats = null)
    {
        _stats = stats;
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
        if (_stats is not null)
        {
            _stats.LogChanged += OnLogChanged;
            _statsLoop = Task.Run(StatsLoopAsync);
        }
        _timer = new Timer(_ => OnTick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _ = ConfigureAsync();
    }

    private bool StatsOn => _stats is not null && _publishContacts;

    private void OnLogChanged()
    {
        _statsDirty = true;
        SignalStats();
    }

    private void SignalStats()
    {
        // One wake-up is enough however many changes arrive before the worker runs.
        if (_statsSignal.CurrentCount == 0) _statsSignal.Release();
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
        PublishSessionDuration(s);
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

    // session_duration changes once a minute while a session runs; it is published only when the minute changes.
    private void PublishSessionDuration(OnAirSnapshot s)
    {
        var p = _publisher;
        var t = _topics;
        if (p is null || t is null || !StatsOn) return;
        var minutes = StatsPayloads.SessionMinutes(s, _clock.UtcNow);
        lock (_gate)
        {
            if (!StatsOn || _sessionMinutes == minutes) return;
            _sessionMinutes = minutes;
            var payload = StatsPayloads.SessionDuration(s, _clock.UtcNow);
            _statsPayloads[t.SessionDuration] = payload;
            // Queued under the lock, so it can never land after the empty message that clears it (see StatsSwitched).
            p.Publish(new MqttOutgoing(t.SessionDuration, payload, true));
        }
    }

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
        if (_stats is not null)
        {
            var qrz = s.Services.TryGetValue(ServiceNames.Qrz, out var q) ? (q.Health, q.Message) : ((ServiceHealth, string)?)null;
            bool qrzChanged;
            lock (_gate)
            {
                qrzChanged = qrz != _qrzShown;
                _qrzShown = qrz;
            }
            if (qrzChanged)
            {
                _qrzDirty = true;
                SignalStats();
            }
        }
        if (p is null || t is null) return;
        if (state is not null) p.Publish(new MqttOutgoing(t.State, MqttPayloads.State(state), true));
        if (logged is not null && _publishContacts)
        {
            if (_stats is not null)
            {
                // The event carries the contact's country, distance and whether it is a new one, which take a look at
                // the log: the statistics worker sends it, followed by the counts it changed.
                _pendingContacts.Enqueue((logged, s.DialHz));
                _statsDirty = true;
                SignalStats();
            }
            else
            {
                p.Publish(new MqttOutgoing(t.QsoEvent, MqttPayloads.QsoEvent(Basic(logged, s.DialHz)), false));
            }
        }
    }

    private static LoggedQso Basic(LoggedContact c, long dialHz) => new(c.Call, c.Band, ModeInfo.Name(c.Mode), dialHz, c.LoggedUtc);

    private async Task StatsLoopAsync()
    {
        var ct = _statsCts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _statsSignal.WaitAsync(ct).ConfigureAwait(false);
                await Task.Delay(StatsSettle, ct).ConfigureAwait(false);
                while (_statsSignal.CurrentCount > 0) _statsSignal.Wait(0, CancellationToken.None);
                RunStats();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The database busy or a bad row: log it, and count again on the next change.
                Serilog.Log.Warning(ex, "MQTT dashboard statistics could not be counted");
            }
        }
    }

    /// <summary>
    /// Counts and publishes the statistics that changed, and sends any logged-contact events waiting. Runs on the
    /// statistics worker only.
    /// </summary>
    internal void RunStats()
    {
        var source = _stats;
        var p = _publisher;
        var t = _topics;
        var contacts = new List<(LoggedContact Contact, long DialHz)>();
        while (_pendingContacts.TryDequeue(out var c)) contacts.Add(c);
        if (source is null || p is null || t is null || !StatsOn) return; // events with nowhere to go are dropped, as offline ones are
        var now = _clock.UtcNow;
        DashboardStats? stats = null;
        var countsFailed = false;
        if (_statsDirty || contacts.Count > 0 || _lastStats is null || now.Date != _statsDay)
        {
            _statsDirty = false;
            try
            {
                stats = source.Build(now);
                _lastStats = stats;
                _statsDay = stats.TodayUtc;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The contact events still go out, without the details that needed the log.
                Serilog.Log.Warning(ex, "MQTT dashboard statistics could not be counted");
                _statsDirty = true;
                countsFailed = true;
            }
        }
        foreach (var (contact, dial) in contacts)
        {
            StatsContact? described = null;
            try
            {
                if (!countsFailed && contact.QsoId is { } id && _lastStats is { } last) described = source.Describe(id, last);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Serilog.Log.Warning(ex, "MQTT contact event details could not be read");
            }
            var basic = Basic(contact, dial);
            p.Publish(new MqttOutgoing(t.QsoEvent, StatsPayloads.ContactEvent("qso_logged", basic, described), false));
            if (described is { IsNewDxcc: true })
                p.Publish(new MqttOutgoing(t.QsoEvent, StatsPayloads.ContactEvent("new_dxcc", basic, described), false));
        }
        if (stats is not null)
        {
            PublishStat(p, t.Stats, StatsPayloads.Counts(stats));
            PublishStat(p, t.NewDxccToday, StatsPayloads.NewDxccToday(stats));
            PublishStat(p, t.FarthestToday, StatsPayloads.FarthestToday(stats));
            PublishStat(p, t.RecentQsos, StatsPayloads.RecentQsos(stats));
            PublishStat(p, t.DailyQsos, StatsPayloads.DailyQsos(stats));
            PublishStat(p, t.WorkedWeekMap, StatsPayloads.WorkedWeekMap(stats));
        }
        if (_qrzDirty || stats is not null)
        {
            _qrzDirty = false;
            ServiceStatus? qrz = _session.Snapshot.Services.GetValueOrDefault(ServiceNames.Qrz);
            PublishStat(p, t.QrzSync, StatsPayloads.QrzSync(source.Qrz(qrz)));
        }
        if (_pskDirty || stats is not null)
        {
            _pskDirty = false;
            PublishStat(p, t.PskSpots, StatsPayloads.PskSpots(source.Psk(now)));
        }
        PublishSessionDuration(_onAir.Snapshot);
    }

    // Retained statistics go out only when they change; the latest of each is sent again on every connect. Checked and
    // queued under the lock: statistics turned off while the worker counts must not be published after they were cleared.
    private void PublishStat(MqttPublisher p, string topic, string payload)
    {
        lock (_gate)
        {
            if (!StatsOn || (_statsPayloads.TryGetValue(topic, out var old) && old == payload)) return;
            _statsPayloads[topic] = payload;
            p.Publish(new MqttOutgoing(topic, payload, true));
        }
    }

    // A timer callback: an exception here would end the process.
    private void OnTick() => Safely(Tick);

    private void Tick()
    {
        if (_disposed) return;
        _onAir.Tick();
        if (StatsOn)
        {
            var utc = _clock.UtcNow;
            // UTC midnight: "today" and the rolling week move on.
            if (_lastStats is not null && utc.Date != _statsDay)
            {
                _statsDirty = true;
                SignalStats();
            }
            if (_clock.Ticks - Interlocked.Read(ref _lastPskTicks) >= PskInterval.Ticks)
            {
                Interlocked.Exchange(ref _lastPskTicks, _clock.Ticks);
                _pskDirty = true;
                SignalStats();
            }
            PublishSessionDuration(_onAir.Snapshot);
        }
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
        if (StatsOn)
        {
            lock (_gate) list.AddRange(_statsPayloads.Select(x => new MqttOutgoing(x.Key, x.Value, true)));
        }
        else if (_stats is not null)
        {
            // Statistics off: clear any left from when they were on (an empty retained message deletes one).
            list.AddRange(t.StatsTopics.Select(x => new MqttOutgoing(x, string.Empty, true)));
        }
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
            var statsWere = StatsOn;
            _publishContacts = o.PublishContacts;
            if (_stats is not null && statsWere != StatsOn) StatsSwitched();
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
            if (StatsOn)
            {
                lock (_gate)
                {
                    _statsPayloads.Clear(); // a new broker or station id holds none of them yet
                    _sessionMinutes = null;
                }
                _statsDirty = _qrzDirty = _pskDirty = true;
                SignalStats();
            }
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

    private string Discovery(MqttTopics t) => HaDiscovery.Build(t, _settings.Current.Profile.Callsign, AppInfo.Version, StatsOn);

    // Statistics turned on or off with Publish contacts: on counts everything afresh, off deletes what was published.
    private void StatsSwitched()
    {
        _lastStats = null;
        lock (_gate)
        {
            _statsPayloads.Clear();
            _sessionMinutes = null;
            // Under the same lock as every statistics publish, so none queued before this can follow the clearing.
            if (!StatsOn && _publisher is { } p && _topics is { } t)
                foreach (var topic in t.StatsTopics) p.Publish(new MqttOutgoing(topic, string.Empty, true));
        }
        if (StatsOn)
        {
            _statsDirty = _qrzDirty = _pskDirty = true;
            SignalStats();
        }
    }

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
            await _statsCts.CancelAsync().ConfigureAwait(false);
            if (_statsLoop is not null) await _statsLoop.ConfigureAwait(false);
            if (_stats is not null) _stats.LogChanged -= OnLogChanged;
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
                List<MqttOutgoing> last =
                [
                    new MqttOutgoing(t.Session, MqttPayloads.Session(s), true),
                    new MqttOutgoing(t.Tx, MqttPayloads.Tx(false), true),
                    new MqttOutgoing(t.SessionEvent, MqttPayloads.SessionEvent(s), false),
                ];
                if (StatsOn) last.Add(new MqttOutgoing(t.SessionDuration, StatsPayloads.SessionDuration(s, _clock.UtcNow), true));
                await p.StopAsync(last, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
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
