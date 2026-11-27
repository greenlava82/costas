// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>The topics Costas publishes to. Costas never subscribes: everything goes one way, out.</summary>
/// <param name="StationId">From <see cref="MqttStationId"/>.</param>
/// <param name="DiscoveryPrefix">Home Assistant's discovery prefix, normally "homeassistant".</param>
public sealed record MqttTopics(string StationId, string DiscoveryPrefix = "homeassistant")
{
    /// <summary>Base of every Costas topic: costas/&lt;station id&gt;.</summary>
    public string Base => $"costas/{StationId}";

    /// <summary>"online" or "offline" (the last will), retained.</summary>
    public string Availability => $"{Base}/availability";

    /// <summary>"ON" while PTT is keyed, "OFF" otherwise, retained.</summary>
    public string Tx => $"{Base}/tx";

    /// <summary>The on-air session as JSON, retained.</summary>
    public string Session => $"{Base}/session";

    /// <summary>Station state as JSON, retained.</summary>
    public string State => $"{Base}/state";

    /// <summary>session_started and session_ended events, not retained.</summary>
    public string SessionEvent => $"{Base}/event/session";

    /// <summary>qso_logged and new_dxcc events, not retained.</summary>
    public string QsoEvent => $"{Base}/event/qso";

    /// <summary>Logbook counts (qsos_today, qsos_week, qsos_total, dxcc_worked_total) as JSON, retained.</summary>
    public string Stats => $"{Base}/stats";

    /// <summary>Entities first worked today, retained.</summary>
    public string NewDxccToday => $"{Base}/stats/new_dxcc_today";

    /// <summary>Today's farthest contact, retained.</summary>
    public string FarthestToday => $"{Base}/stats/farthest_today";

    /// <summary>Minutes in the running on-air session, retained.</summary>
    public string SessionDuration => $"{Base}/stats/session_duration";

    /// <summary>The last ten contacts, retained.</summary>
    public string RecentQsos => $"{Base}/stats/recent_qsos";

    /// <summary>Contacts per UTC day over the last week, by band, retained.</summary>
    public string DailyQsos => $"{Base}/stats/daily_qsos";

    /// <summary>Stations worked this week with positions, retained.</summary>
    public string WorkedWeekMap => $"{Base}/stats/worked_week_map";

    /// <summary>The QRZ logbook sync, retained.</summary>
    public string QrzSync => $"{Base}/stats/qrz_sync";

    /// <summary>PSK Reporter spots of my signal over the last 15 minutes, retained.</summary>
    public string PskSpots => $"{Base}/stats/psk_spots_15m";

    /// <summary>Every statistics topic.</summary>
    public IReadOnlyList<string> StatsTopics =>
        [Stats, NewDxccToday, FarthestToday, SessionDuration, RecentQsos, DailyQsos, WorkedWeekMap, QrzSync, PskSpots];

    /// <summary>Home Assistant device-based discovery, retained.</summary>
    public string Discovery => $"{DiscoveryPrefix}/device/costas_{StationId}/config";

    /// <summary>The device id in Home Assistant, also the prefix of every unique id.</summary>
    public string DeviceId => $"costas_{StationId}";
}
