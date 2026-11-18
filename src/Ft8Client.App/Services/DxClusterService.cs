// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using Ft8Client.App.Engine;
using Ft8Client.Core.Bands;
using Ft8Client.Core.Cluster;
using Ft8Client.Core.Messages;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Data.Log;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.DxCluster;
using Serilog;

namespace Ft8Client.App.Services;

/// <summary>
/// The DX cluster in the background (docs/05-integrations.md 5b): one node the operator picked in Settings, logged in
/// with the operator's callsign, its spots kept for the Spotted line on station rows and details and the cluster list
/// on the Awards view. Display only: spots never change the ranking, who is called or the radio's frequency. Off by
/// default; nothing connects until a node is set. While replaying recordings (--simulate), the spots come from
/// <c>samples/dxcluster/spots.txt</c> instead of the network, re-timed to now, and nothing is stored.
/// </summary>
public sealed class DxClusterService : IAsyncDisposable
{
    /// <summary>The <see cref="SpotRecord.Source"/> of stored cluster spots.</summary>
    public const string Source = "cluster";

    private readonly Session _session;
    private readonly AppSettingsAccessor _settings;
    private readonly SpotRepository _spots;
    private readonly IClock _clock;
    private readonly Func<string, int, ClusterFeedClient> _newFeed;
    private readonly Func<IReadOnlyList<string>>? _replay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private ClusterSpots _current = ClusterSpots.Empty;
    private ClusterFeedClient? _feed;
    private Task _identity = Task.CompletedTask;
    private (string Call, string Host, int Port) _following = (string.Empty, string.Empty, 0);
    private bool _disposed;
    private bool _running;
    private bool _applied;

    /// <summary>
    /// Creates the service. <paramref name="newFeed"/> (given host and port) replaces the network in tests;
    /// <paramref name="replay"/>, when given, supplies recorded spot lines instead of a node (simulation).
    /// </summary>
    public DxClusterService(Session session, AppSettingsAccessor settings, SpotRepository spots, IClock clock,
                            Func<string, int, ClusterFeedClient>? newFeed = null, Func<IReadOnlyList<string>>? replay = null)
    {
        _session = session;
        _settings = settings;
        _spots = spots;
        _clock = clock;
        _newFeed = newFeed ?? ((host, port) => new ClusterFeedClient(host, port, () => clock.UtcNow));
        _replay = replay;
    }

    /// <summary>The spots kept.</summary>
    public ClusterSpots Current
    {
        get { lock (_lock) return _current; }
    }

    /// <summary>Raised when <see cref="Current"/> changes, on a background thread.</summary>
    public event Action<ClusterSpots>? Changed;

    /// <summary>The callsign logged in with (empty when off).</summary>
    internal string FollowedCall => _following.Call;

    /// <summary>Waits until the latest callsign or setting change has been applied (tests).</summary>
    internal Task Settled => _identity;

    /// <summary>Connects when the setting is on and a node is set; later changes of callsign, node or setting restart it.</summary>
    public void Start()
    {
        _settings.Changed += OnSettingsChanged;
        _identity = Task.Run(() => FollowAsync(_settings.Current));
        _identity.Wait();
    }

    private void OnSettingsChanged(AppSettings s) =>
        _identity = _identity.ContinueWith(_ => FollowAsync(s), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();

    private async Task FollowAsync(AppSettings s)
    {
        var o = s.DxCluster;
        var call = Callsign.Normalize(s.Profile.Callsign);
        var host = o.Host.Trim();
        var want = o.Enabled && host.Length > 0 && Callsign.IsValid(Callsign.BaseCall(call)) ? (call, host, o.Port) : (string.Empty, string.Empty, 0);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || (_applied && want == _following && _running == (want.Item1.Length > 0))) return;
            _applied = true;
            await StopAsync().ConfigureAwait(false);
            _following = want;
            if (want.Item1.Length == 0)
            {
                _session.SetService(ServiceNames.Cluster, ServiceStatus.Off(!o.Enabled ? "DX cluster off"
                    : host.Length == 0 ? "No DX cluster node set in Settings" : "Set your callsign in Settings to log in to the DX cluster"));
                return;
            }
            if (_replay is not null)
            {
                Use(c => c.With(Retime(_replay(), _clock.UtcNow), _clock.UtcNow));
                _running = true;
                _session.SetService(ServiceNames.Cluster, new ServiceStatus(ServiceHealth.Ok, "Simulated: spots replayed from samples", _clock.UtcNow));
                return;
            }
            LoadStored();
            _session.SetService(ServiceNames.Cluster, new ServiceStatus(ServiceHealth.Degraded, $"Connecting to {host}", _clock.UtcNow));
            var feed = _newFeed(host, o.Port);
            feed.Spot += OnSpot;
            feed.StatusChanged += st =>
            {
                if (ReferenceEquals(feed, _feed))
                    _session.SetService(ServiceNames.Cluster, st.Health == ServiceHealth.Ok ? st with { Message = $"Connected to {host}" } : st);
            };
            _feed = feed;
            _running = true;
            feed.Start(want.Item1);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopAsync()
    {
        var feed = _feed;
        _feed = null;
        _running = false;
        if (feed is not null) await feed.DisposeAsync().ConfigureAwait(false);
        lock (_lock)
        {
            if (_current.Count == 0) return;
            _current = ClusterSpots.Empty;
        }
        Changed?.Invoke(ClusterSpots.Empty);
    }

    private void OnSpot(ClusterSpot spot)
    {
        var now = _clock.UtcNow;
        if (BandPlan.BandOf(spot.FrequencyHz) is not { } band) return;
        try
        {
            _spots.Add(new SpotRecord
            {
                MyCall = spot.DxCall, RxCall = spot.Spotter, Band = band, Mode = ClusterModeFilter.ModeNamed(spot.Comment) ?? string.Empty,
                FreqHz = spot.FrequencyHz, TimeUtc = spot.TimeUtc, Source = Source,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not store a DX cluster spot");
        }
        Use(c => c.With([spot], now));
    }

    // Spots of the last half hour survive a restart; their comment is not kept, only the mode it named.
    private void LoadStored()
    {
        try
        {
            var now = _clock.UtcNow;
            var stored = _spots.Since(now - ClusterSpots.MaxAge)
                .Where(r => r.Source == Source && r.FreqHz is > 0)
                .Select(r => new ClusterSpot(r.MyCall, r.FreqHz!.Value, r.RxCall, r.Mode, r.TimeUtc));
            Use(c => c.With(stored, now));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load stored DX cluster spots");
        }
    }

    /// <summary>
    /// Parses recorded spot lines and moves them in time so the newest is a minute old, keeping their spacing (the
    /// recording's own times are hours or days ago). Lines that are not spots are skipped.
    /// </summary>
    internal static IReadOnlyList<ClusterSpot> Retime(IReadOnlyList<string> lines, DateTime nowUtc)
    {
        var parsed = lines.Select(l => ClusterSpotParser.Parse(l, nowUtc)).OfType<ClusterSpot>().ToList();
        if (parsed.Count == 0) return parsed;
        var shift = nowUtc.AddMinutes(-1) - parsed.Max(p => p.TimeUtc);
        return [.. parsed.Select(p => p with { TimeUtc = p.TimeUtc + shift })];
    }

    private void Use(Func<ClusterSpots, ClusterSpots> change)
    {
        ClusterSpots next;
        lock (_lock)
        {
            next = change(_current);
            if (ReferenceEquals(next, _current)) return;
            _current = next;
        }
        Changed?.Invoke(next);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _settings.Changed -= OnSettingsChanged;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
