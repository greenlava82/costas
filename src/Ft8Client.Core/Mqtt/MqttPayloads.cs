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

/// <summary>JSON and text payloads for the topics in <see cref="MqttTopics"/>. Times are ISO-8601 UTC.</summary>
public static class MqttPayloads
{
    /// <summary>Availability payload while Costas is connected.</summary>
    public const string Online = "online";

    /// <summary>Availability payload when Costas leaves, and its last will.</summary>
    public const string Offline = "offline";

    /// <summary>The tx topic payload.</summary>
    public static string Tx(bool keyed) => keyed ? "ON" : "OFF";

    /// <summary>
    /// The session topic: <c>{"state":"on","id":"…","since":"…","reason":null,"transmitting":false}</c>. The reason is
    /// the last session's end reason while no session runs.
    /// </summary>
    public static string Session(OnAirSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new JsonObject
        {
            ["state"] = s.Active ? "on" : "off",
            ["id"] = s.SessionId,
            ["since"] = Time(s.SinceUtc),
            ["reason"] = s.EndReason is { } r ? ReasonName(r) : null,
            ["transmitting"] = s.Transmitting,
        }.ToJsonString();
    }

    /// <summary>The state topic: callsign, rig_connected, state, band, frequency (Hz) and mode.</summary>
    public static string State(StationState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new JsonObject
        {
            ["callsign"] = s.Callsign,
            ["rig_connected"] = s.RigConnected,
            ["state"] = ActivityName(s.Activity),
            ["band"] = s.Band,
            ["frequency"] = s.FrequencyHz,
            ["mode"] = s.Mode,
        }.ToJsonString();
    }

    /// <summary>A session_started or session_ended event, from the snapshot right after the change.</summary>
    public static string SessionEvent(OnAirSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new JsonObject
        {
            ["event_type"] = s.Active ? "session_started" : "session_ended",
            ["session_id"] = s.SessionId,
            ["reason"] = s.Active || s.EndReason is null ? null : ReasonName(s.EndReason.Value),
            ["ts"] = Time(s.SinceUtc),
        }.ToJsonString();
    }

    /// <summary>A qso_logged event.</summary>
    public static string QsoEvent(LoggedQso q)
    {
        ArgumentNullException.ThrowIfNull(q);
        return new JsonObject
        {
            ["event_type"] = "qso_logged",
            ["call"] = q.Call,
            ["band"] = q.Band,
            ["mode"] = q.Mode,
            ["frequency"] = q.FrequencyHz,
            ["ts"] = Time(q.TimeOffUtc),
        }.ToJsonString();
    }

    /// <summary>The published name of an end reason: idle_timeout, halted, max_length, rig_lost, app_closed.</summary>
    public static string ReasonName(SessionEndReason r) => r switch
    {
        SessionEndReason.IdleTimeout => "idle_timeout",
        SessionEndReason.Halted => "halted",
        SessionEndReason.MaxLength => "max_length",
        SessionEndReason.RigLost => "rig_lost",
        SessionEndReason.AppClosed => "app_closed",
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    /// <summary>The published name of an activity: idle, listening, calling_cq, in_qso.</summary>
    public static string ActivityName(StationActivity a) => a switch
    {
        StationActivity.Idle => "idle",
        StationActivity.Listening => "listening",
        StationActivity.CallingCq => "calling_cq",
        StationActivity.InQso => "in_qso",
        _ => throw new ArgumentOutOfRangeException(nameof(a)),
    };

    private static string? Time(DateTime? utc) =>
        utc?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
