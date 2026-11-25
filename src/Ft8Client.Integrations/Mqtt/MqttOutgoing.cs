// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Integrations.Mqtt;

/// <summary>One message to publish.</summary>
/// <param name="Topic">The topic.</param>
/// <param name="Payload">UTF-8 text payload.</param>
/// <param name="Retain">Retained state (only the latest per topic is kept while offline) or a one-off event (dropped
/// while offline, never replayed late).</param>
public sealed record MqttOutgoing(string Topic, string Payload, bool Retain);
