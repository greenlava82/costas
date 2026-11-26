// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Geo;
using Ft8Client.Core.Messages;
using Ft8Client.Core.Mqtt;
using Ft8Client.Core.Services;
using Ft8Client.Data.Log;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.Qrz;

namespace Ft8Client.App.Services;

/// <summary>
/// Reads the dashboard statistics for MQTT publishing from the log, the upload queue and the stored PSK Reporter spots,
/// so the counts are right after Costas or Home Assistant restarts. Contacts are those of the profile's callsign, as on
/// the Log page; every call here reads the database and belongs off the slot and UI threads.
/// </summary>
public sealed class DashboardStatsSource
{
    /// <summary>The window psk_spots_15m counts over.</summary>
    public static readonly TimeSpan SpotWindow = TimeSpan.FromMinutes(15);

    private readonly QsoRepository _qsos;
    private readonly UploadQueueRepository _uploads;
    private readonly SyncStateRepository _sync;
    private readonly SpotRepository? _spots;
    private readonly AppSettingsAccessor _settings;
    private readonly ISecretStore _secrets;
    private readonly DashboardStatsBuilder _builder;

    /// <summary>Creates the source.</summary>
    public DashboardStatsSource(QsoRepository qsos, UploadQueueRepository uploads, SyncStateRepository sync, SpotRepository? spots,
                                CountryFile countries, IReadOnlyDictionary<int, string> keyByAdif, AppSettingsAccessor settings, ISecretStore secrets)
    {
        _qsos = qsos;
        _uploads = uploads;
        _sync = sync;
        _spots = spots;
        _settings = settings;
        _secrets = secrets;
        _builder = new DashboardStatsBuilder(countries, keyByAdif);
        qsos.Changed += () => LogChanged?.Invoke();
    }

    /// <summary>Raised after any change to the log (on the writing thread; handlers return at once).</summary>
    public event Action? LogChanged;

    private string? Station => _settings.Current.Profile.Callsign.Trim().ToUpperInvariant() is { Length: > 0 } c ? c : null;

    private string MyGrid => _settings.Current.Profile.Grid;

    /// <summary>The statistics as of <paramref name="nowUtc"/>.</summary>
    public DashboardStats Build(DateTime nowUtc)
    {
        var station = Station;
        var all = new LogFilter(StationCallsign: station);
        var weekStart = nowUtc.ToUniversalTime().Date.AddDays(-(DashboardStatsBuilder.WeekDays - 1));
        var input = new DashboardInput(
            nowUtc,
            MyGrid,
            _qsos.Totals(all).Contacts,
            [.. _qsos.List(all with { FromUtc = DateTime.SpecifyKind(weekStart, DateTimeKind.Utc) }).Select(ToStats)],
            [.. _qsos.List(all, DashboardStatsBuilder.RecentCount).Select(ToStats)],
            [.. _qsos.FirstByCallAndEntity(station).Select(ToStats)]);
        return _builder.Build(input);
    }

    /// <summary>The published form of logged contact <paramref name="qsoId"/>, or null when it is not in the log.</summary>
    public StatsContact? Describe(long qsoId, DashboardStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return _qsos.Get(qsoId) is { } q ? _builder.Describe(ToStats(q), MyGrid, stats.FirstContactIds) : null;
    }

    /// <summary>
    /// The QRZ logbook sync: disabled without a logbook key (or with the logbook turned off), error while the QRZ
    /// indicator shows a failure, pending while contacts wait to upload, else ok.
    /// </summary>
    /// <param name="qrz">The QRZ indicator as the session shows it, or null.</param>
    public QrzSyncStatus Qrz(ServiceStatus? qrz)
    {
        var profile = _settings.Current.Profile;
        var pending = _uploads.CountFor("qrz");
        var last = _sync.Get(profile.Id, QrzSyncService.Target).LastSyncUtc;
        if (!_settings.Current.Qrz.LogbookEnabled || string.IsNullOrEmpty(_secrets.Get(SecretNames.QrzLogbookKey(profile.Id))))
            return new QrzSyncStatus(QrzSyncState.Disabled, last, pending, null);
        if (qrz is { Health: ServiceHealth.Down or ServiceHealth.Degraded } bad)
            return new QrzSyncStatus(QrzSyncState.Error, last, pending, bad.Message);
        return new QrzSyncStatus(pending > 0 ? QrzSyncState.Pending : QrzSyncState.Ok, last, pending, null);
    }

    /// <summary>PSK Reporter spots of my callsign over the last 15 minutes (not RBN or cluster spots).</summary>
    public PskSpotSummary Psk(DateTime nowUtc)
    {
        if (_spots is null || Station is not { } me) return PskSpotSummary.None;
        var mine = _spots.Since(nowUtc - SpotWindow)
            .Where(s => s.TimeUtc <= nowUtc && Callsign.EqualsCall(s.MyCall, me)
                        && s.Source != ReverseBeaconService.Source && s.Source != DxClusterService.Source)
            .ToList();
        var home = Grid.ToLatLon(MyGrid);
        var far = home is null
            ? null
            : mine.Select(s => (Spot: s, Pos: Grid.ToLatLon(s.RxGrid)))
                .Where(x => x.Pos is not null)
                .Select(x => (x.Spot, Km: (int)Math.Round(GreatCircle.DistanceKm(home.Value, x.Pos!.Value))))
                .OrderByDescending(x => x.Km).ThenBy(x => x.Spot.TimeUtc)
                .Select(x => ((SpotRecord Spot, int Km)?)x)
                .FirstOrDefault();
        return new PskSpotSummary(mine.Count, far?.Km, far?.Spot.RxCall, far?.Spot.RxGrid);
    }

    /// <summary>A log row in the form the statistics read.</summary>
    public static StatsQso ToStats(QsoRecord q)
    {
        ArgumentNullException.ThrowIfNull(q);
        return new StatsQso(q.Id, q.Call, q.QsoDateOn, q.QsoDateOff, q.Band, string.IsNullOrWhiteSpace(q.Submode) ? q.Mode : q.Submode, q.FreqHz,
            q.Country, q.Dxcc, q.EntityKey, q.State, q.Gridsquare, q.RstSent, q.RstRcvd,
            q.UploadState == UploadState.Uploaded || q.QrzLogid is not null || q.Source == QsoSource.Qrz);
    }
}
