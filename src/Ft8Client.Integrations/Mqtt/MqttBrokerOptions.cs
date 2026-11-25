// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Integrations.Mqtt;

/// <summary>Where and how to connect to the MQTT broker.</summary>
/// <param name="Host">Broker host name or address.</param>
/// <param name="Port">Broker port, normally 1883 (plain) or 8883 (TLS).</param>
/// <param name="Tls">Connect with TLS.</param>
/// <param name="Username">User name, or null for none.</param>
/// <param name="Password">Password from the OS credential store, or null for none. Never logged.</param>
/// <param name="ClientId">This install's client id, unique so two copies of Costas never knock each other off.</param>
public sealed record MqttBrokerOptions(string Host, int Port, bool Tls, string? Username, string? Password, string ClientId)
{
    /// <summary>Keep-alive: the broker sends the last will about 1.5 times this after Costas goes silent.</summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(5);

    /// <summary>Longest a connection attempt may take.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public override string ToString() => $"{Host}:{Port}{(Tls ? " (TLS)" : "")}"; // never the password
}
