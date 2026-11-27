// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Data.Settings;

/// <summary>
/// Publishing station events to an MQTT broker (owner request 2026-11-24): one way only, for Home Assistant, Node-RED or
/// anything else. The broker password is in the OS credential store, never here.
/// </summary>
public sealed class MqttOptions
{
    /// <summary>Publish. Off until the operator turns it on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Broker host name or address.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Broker port.</summary>
    public int Port { get; set; } = 1883;

    /// <summary>Connect with TLS.</summary>
    public bool Tls { get; set; }

    /// <summary>Broker user name; empty for none.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The id in every topic (costas/&lt;id&gt;/…). Set from the callsign when publishing is first turned on and then
    /// kept, so switching station profile does not move the topics or create a second Home Assistant device.
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>This install's MQTT client id, made once so two copies of Costas never knock each other off.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Also publish Home Assistant's discovery message, so the device appears there with no setup.</summary>
    public bool HomeAssistantDiscovery { get; set; } = true;

    /// <summary>Home Assistant's discovery prefix.</summary>
    public string DiscoveryPrefix { get; set; } = "homeassistant";

    /// <summary>Minutes with no transmission before the on-air session ends.</summary>
    public int SessionIdleMinutes { get; set; } = 5;

    /// <summary>Longest an on-air session may run before it is ended as a failsafe, in minutes.</summary>
    public int SessionMaxMinutes { get; set; } = 240;

    /// <summary>Publish a qso_logged event for each logged contact, and the logbook statistics (dashboard sensors).</summary>
    public bool PublishContacts { get; set; } = true;
}
