// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Text.Json.Nodes;

namespace Ft8Client.Core.Mqtt;

/// <summary>
/// Builds Home Assistant's device-based MQTT discovery message, so a "Costas &lt;callsign&gt;" device with its sensors
/// appears in Home Assistant with no YAML. Discovery is publish only, like everything else Costas sends: the device
/// has no buttons, switches or other controls.
/// </summary>
public static class HaDiscovery
{
    /// <summary>
    /// Seconds without a republish after which Home Assistant shows the session and transmitting sensors as
    /// unavailable. Costas republishes every <see cref="HeartbeatSeconds"/>, so a frozen Costas cannot leave a session
    /// on.
    /// </summary>
    public const int ExpireAfterSeconds = 90;

    /// <summary>How often Costas republishes the session, tx and state topics.</summary>
    public const int HeartbeatSeconds = 30;

    /// <summary>The discovery payload for <paramref name="topics"/>.</summary>
    /// <param name="topics">The station's topics.</param>
    /// <param name="callsign">Shown in the device name.</param>
    /// <param name="version">Costas version, shown as the device software.</param>
    public static string Build(MqttTopics topics, string callsign, string version)
    {
        ArgumentNullException.ThrowIfNull(topics);
        var dev = topics.DeviceId;
        var cmps = new JsonObject
        {
            ["session_active"] = Binary(dev, "session_active", "On air session", "running", topics.Session,
                "{{ value_json.state }}", "on", "off", expires: true),
            ["transmitting"] = Binary(dev, "transmitting", "Transmitting", "running", topics.Tx, null, "ON", "OFF", expires: true),
            ["rig_connected"] = Binary(dev, "rig_connected", "Radio connected", "connectivity", topics.State,
                "{{ 'ON' if value_json.rig_connected else 'OFF' }}", "ON", "OFF", expires: false),
            ["state"] = Enum(dev, "state", "State", topics.State, "{{ value_json.state }}",
                ["idle", "listening", "calling_cq", "in_qso"]),
            ["session_end_reason"] = Enum(dev, "session_end_reason", "Session end reason", topics.Session,
                "{{ value_json.reason if value_json.reason else 'none' }}",
                ["none", "idle_timeout", "halted", "max_length", "rig_lost", "app_closed"]),
            ["band"] = Sensor(dev, "band", "Band", topics.State, "{{ value_json.band }}"),
            ["mode"] = Sensor(dev, "mode", "Mode", topics.State, "{{ value_json.mode }}"),
            ["frequency"] = Frequency(dev, topics.State),
            ["session_event"] = Event(dev, "session_event", "Session", topics.SessionEvent, ["session_started", "session_ended"]),
            ["qso_event"] = Event(dev, "qso_event", "Contact", topics.QsoEvent, ["qso_logged"]),
        };
        return new JsonObject
        {
            ["dev"] = new JsonObject
            {
                ["ids"] = new JsonArray(dev),
                ["name"] = $"Costas {callsign}".TrimEnd(),
                ["mf"] = "Costas",
                ["mdl"] = "Costas",
                ["sw"] = version,
            },
            ["o"] = new JsonObject
            {
                ["name"] = "Costas",
                ["sw"] = version,
                ["url"] = "https://somedudenamedtony.github.io/costas/",
            },
            ["availability_topic"] = topics.Availability,
            ["payload_available"] = MqttPayloads.Online,
            ["payload_not_available"] = MqttPayloads.Offline,
            ["qos"] = 1,
            ["cmps"] = cmps,
        }.ToJsonString();
    }

    private static JsonObject Base(string dev, string platform, string key, string name, string stateTopic, string? template)
    {
        var o = new JsonObject
        {
            ["p"] = platform,
            ["unique_id"] = $"{dev}_{key}",
            ["default_entity_id"] = $"{platform}.{dev}_{key}",
            ["name"] = name,
            ["state_topic"] = stateTopic,
        };
        if (template is not null) o["value_template"] = template;
        return o;
    }

    private static JsonObject Binary(string dev, string key, string name, string deviceClass, string topic, string? template,
        string on, string off, bool expires)
    {
        var o = Base(dev, "binary_sensor", key, name, topic, template);
        o["device_class"] = deviceClass;
        o["payload_on"] = on;
        o["payload_off"] = off;
        if (expires) o["expire_after"] = ExpireAfterSeconds;
        return o;
    }

    private static JsonObject Sensor(string dev, string key, string name, string topic, string template) =>
        Base(dev, "sensor", key, name, topic, template);

    private static JsonObject Enum(string dev, string key, string name, string topic, string template, string[] options)
    {
        var o = Base(dev, "sensor", key, name, topic, template);
        o["device_class"] = "enum";
        o["options"] = new JsonArray([.. options.Select(x => (JsonNode)x)]);
        return o;
    }

    private static JsonObject Frequency(string dev, string topic)
    {
        var o = Base(dev, "sensor", "frequency", "Frequency", topic, "{{ value_json.frequency }}");
        o["device_class"] = "frequency";
        o["unit_of_measurement"] = "Hz";
        o["suggested_unit_of_measurement"] = "MHz";
        o["suggested_display_precision"] = 3;
        return o;
    }

    private static JsonObject Event(string dev, string key, string name, string topic, string[] types)
    {
        var o = Base(dev, "event", key, name, topic, null);
        o["event_types"] = new JsonArray([.. types.Select(x => (JsonNode)x)]);
        return o;
    }
}
