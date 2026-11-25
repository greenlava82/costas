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

    /// <summary>qso_logged events, not retained.</summary>
    public string QsoEvent => $"{Base}/event/qso";

    /// <summary>Home Assistant device-based discovery, retained.</summary>
    public string Discovery => $"{DiscoveryPrefix}/device/costas_{StationId}/config";

    /// <summary>The device id in Home Assistant, also the prefix of every unique id.</summary>
    public string DeviceId => $"costas_{StationId}";
}
