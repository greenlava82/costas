// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Integrations.Mqtt;

/// <summary>The outcome of Settings' Test connection.</summary>
/// <param name="Ok">Connected and a test message was accepted.</param>
/// <param name="Message">Plain words for the operator.</param>
/// <param name="RoundTrip">Connect plus publish time when it worked.</param>
public sealed record MqttTestResult(bool Ok, string Message, TimeSpan? RoundTrip);
