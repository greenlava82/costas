// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Globalization;
using System.Text.Json.Nodes;

namespace Ft8Client.Core.Mqtt;

/// <summary>
/// Payloads of the dashboard statistics topics. Each sensor with attributes gets <c>{"state":…,"attributes":{…}}</c> on
/// its own retained topic; lists in attributes are cut (newest kept) so the attributes stay under
/// <see cref="AttributeLimitBytes"/>, well inside Home Assistant's 16 KB recorder limit. Times are ISO-8601 UTC.
/// </summary>
public static class StatsPayloads
{
    /// <summary>Largest attributes object Costas publishes for one entity, in UTF-8 bytes.</summary>
    public const int AttributeLimitBytes = 8000;

    /// <summary>Points at most in worked_week_map.</summary>
    public const int MaxMapPoints = 200;

    /// <summary>Longest last_error published.</summary>
    public const int MaxErrorLength = 300;

    /// <summary>The plain counts: qsos_today, qsos_week, qsos_total, dxcc_worked_total.</summary>
    public static string Counts(DashboardStats s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new JsonObject
        {
            ["qsos_today"] = s.QsosToday,
            ["qsos_week"] = s.QsosWeek,
            ["qsos_total"] = s.QsosTotal,
            ["dxcc_worked_total"] = s.DxccWorkedTotal,
        }.ToJsonString();
    }

    /// <summary>new_dxcc_today: the number of entities first worked today; attribute "entities" lists them.</summary>
    public static string NewDxccToday(DashboardStats s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var items = s.NewDxccToday.Select(e => (JsonNode)new JsonObject
        {
            ["dxcc"] = e.Dxcc,
            ["country"] = e.Country,
            ["call"] = e.Call,
            ["ts"] = Time(e.TimeUtc),
        }).Reverse().ToList(); // newest first while cutting, then back to oldest first
        var kept = Fit(items, "entities", new JsonObject());
        kept.Reverse();
        return Wrap(s.NewDxccToday.Count, new JsonObject { ["entities"] = new JsonArray([.. kept]) });
    }

    /// <summary>farthest_today: kilometres, or null with no distance today; attributes call, country, grid, ts.</summary>
    public static string FarthestToday(DashboardStats s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var f = s.FarthestToday;
        return Wrap(f?.DistanceKm, new JsonObject
        {
            ["call"] = f?.Call,
            ["country"] = f?.Country,
            ["grid"] = f?.Grid,
            ["ts"] = Time(f?.TimeUtc),
        });
    }

    /// <summary>session_duration: whole minutes of the running session, 0 with none.</summary>
    public static string SessionDuration(OnAirSnapshot s, DateTime nowUtc) => Wrap(SessionMinutes(s, nowUtc), null);

    /// <summary>Whole minutes since the session started, 0 when none is running.</summary>
    public static int SessionMinutes(OnAirSnapshot s, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!s.Active || s.SinceUtc is not { } since) return 0;
        var minutes = (nowUtc.ToUniversalTime() - since.ToUniversalTime()).TotalMinutes;
        return minutes <= 0 ? 0 : (int)Math.Floor(minutes);
    }

    /// <summary>recent_qsos: the latest contact's time, or null; attribute "qsos", newest first.</summary>
    public static string RecentQsos(DashboardStats s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var items = s.Recent.Select(c => (JsonNode)Contact(c)).ToList();
        return Wrap(Time(s.Recent.FirstOrDefault()?.TimeUtc), new JsonObject { ["qsos"] = new JsonArray([.. Fit(items, "qsos", new JsonObject())]) });
    }

    /// <summary>daily_qsos: today's count; attribute "days", oldest first, each {date, total, by_band}.</summary>
    public static string DailyQsos(DashboardStats s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var days = new JsonArray();
        foreach (var d in s.Days)
        {
            var byBand = new JsonObject();
            foreach (var (band, n) in d.ByBand) byBand[band] = n;
            days.Add(new JsonObject
            {
                ["date"] = d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["total"] = d.Total,
                ["by_band"] = byBand,
            });
        }
        return Wrap(s.QsosToday, new JsonObject { ["days"] = days });
    }

    /// <summary>
    /// worked_week_map: unique grids worked this week; attribute "points", newest first, at most
    /// <see cref="MaxMapPoints"/> and only as many as fit in <see cref="AttributeLimitBytes"/>.
    /// </summary>
    public static string WorkedWeekMap(DashboardStats s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var items = s.MapPoints.Take(MaxMapPoints).Select(p => (JsonNode)new JsonObject
        {
            ["call"] = p.Call,
            ["lat"] = p.Lat,
            ["lon"] = p.Lon,
            ["country"] = p.Country,
            ["band"] = p.Band,
            ["ts"] = Time(p.TimeUtc),
        }).ToList();
        return Wrap(s.GridsWeek, new JsonObject { ["points"] = new JsonArray([.. Fit(items, "points", new JsonObject())]) });
    }

    /// <summary>qrz_sync: ok, pending, error or disabled; attributes last_sync_ts, pending_count, last_error.</summary>
    public static string QrzSync(QrzSyncStatus q)
    {
        ArgumentNullException.ThrowIfNull(q);
        var error = q.LastError is { Length: > MaxErrorLength } e ? e[..(MaxErrorLength - 1)] + "…" : q.LastError;
        return Wrap(QrzStateName(q.State), new JsonObject
        {
            ["last_sync_ts"] = Time(q.LastSyncUtc),
            ["pending_count"] = q.PendingCount,
            ["last_error"] = error,
        });
    }

    /// <summary>psk_spots_15m: spots of my signal in the last 15 minutes; attributes farthest_km, farthest_reporter, farthest_grid.</summary>
    public static string PskSpots(PskSpotSummary p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return Wrap(p.Count, new JsonObject
        {
            ["farthest_km"] = p.FarthestKm,
            ["farthest_reporter"] = p.FarthestReporter,
            ["farthest_grid"] = p.FarthestGrid,
        });
    }

    /// <summary>
    /// A qso_logged or new_dxcc event: the original fields (call, band, mode, frequency, ts) plus every recent_qsos field.
    /// With no <paramref name="contact"/> (the log write failed) the added fields are null and is_new_dxcc false.
    /// </summary>
    public static string ContactEvent(string eventType, LoggedQso logged, StatsContact? contact)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(logged);
        var o = new JsonObject
        {
            ["event_type"] = eventType,
            ["call"] = logged.Call,
            ["band"] = logged.Band,
            ["mode"] = logged.Mode,
            ["frequency"] = logged.FrequencyHz,
            ["ts"] = Time(contact?.TimeUtc ?? logged.TimeOffUtc),
        };
        var entry = contact is null ? Contact(new StatsContact(logged.Call, logged.Band, logged.Mode, null, logged.TimeOffUtc, null, null, null,
            null, null, null, null, false, null, null, false)) : Contact(contact);
        foreach (var (k, v) in entry)
            if (!o.ContainsKey(k)) o[k] = v?.DeepClone();
        return o.ToJsonString();
    }

    /// <summary>The published name of a QRZ sync state.</summary>
    public static string QrzStateName(QrzSyncState s) => s switch
    {
        QrzSyncState.Ok => "ok",
        QrzSyncState.Pending => "pending",
        QrzSyncState.Error => "error",
        QrzSyncState.Disabled => "disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    /// <summary>The UTF-8 size of the "attributes" object in a payload from this class (0 when it has none).</summary>
    public static int AttributeBytes(string payload) =>
        JsonNode.Parse(payload)?["attributes"] is { } a ? System.Text.Encoding.UTF8.GetByteCount(a.ToJsonString()) : 0;

    private static JsonObject Contact(StatsContact c) => new()
    {
        ["call"] = c.Call,
        ["band"] = c.Band,
        ["mode"] = c.Mode,
        ["frequency_hz"] = c.FrequencyHz,
        ["ts"] = Time(c.TimeUtc),
        ["country"] = c.Country,
        ["dxcc"] = c.Dxcc,
        ["state_or_region"] = c.StateOrRegion,
        ["grid"] = c.Grid,
        ["lat"] = c.Lat,
        ["lon"] = c.Lon,
        ["distance_km"] = c.DistanceKm,
        ["is_new_dxcc"] = c.IsNewDxcc,
        ["rst_sent"] = c.RstSent,
        ["rst_rcvd"] = c.RstRcvd,
        ["qrz_synced"] = c.QrzSynced,
    };

    // The first items, in order, that fit in the attributes object with the rest of its fields.
    private static List<JsonNode> Fit(List<JsonNode> items, string name, JsonObject others)
    {
        // {"name":[...]} plus the other fields: brackets, quotes and the separators between items.
        var used = System.Text.Encoding.UTF8.GetByteCount(others.ToJsonString()) + System.Text.Encoding.UTF8.GetByteCount(name) + 6;
        var kept = new List<JsonNode>();
        foreach (var item in items)
        {
            var size = System.Text.Encoding.UTF8.GetByteCount(item.ToJsonString()) + (kept.Count == 0 ? 0 : 1);
            if (used + size > AttributeLimitBytes) break;
            used += size;
            kept.Add(item);
        }
        return kept;
    }

    private static string Wrap(JsonNode? state, JsonObject? attributes)
    {
        var o = new JsonObject { ["state"] = state };
        if (attributes is not null) o["attributes"] = attributes;
        return o.ToJsonString();
    }

    private static string? Time(DateTime? utc) =>
        utc?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
