// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Net;
using System.Net.Sockets;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Ft8Client.Integrations.Tests.Mqtt;

/// <summary>An in-process MQTT broker on a free port that records every publish and subscribe.</summary>
public sealed class MqttTestBroker : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<(string Topic, string Payload, bool Retain)> _published = [];
    private readonly MqttServer _server;

    private MqttTestBroker(int port, string? user, string? password)
    {
        Port = port;
        var factory = new MqttServerFactory();
        var options = factory.CreateServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointPort(port)
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointBoundIPV6Address(IPAddress.None).Build();
        _server = factory.CreateMqttServer(options);
        _server.ValidatingConnectionAsync += e =>
        {
            if (user is not null && (e.UserName != user || e.Password != password)) e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            return Task.CompletedTask;
        };
        _server.InterceptingPublishAsync += e =>
        {
            lock (_gate) _published.Add((e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString(), e.ApplicationMessage.Retain));
            return Task.CompletedTask;
        };
        _server.InterceptingSubscriptionAsync += _ =>
        {
            Interlocked.Increment(ref _subscriptions);
            return Task.CompletedTask;
        };
    }

    private int _subscriptions;

    /// <summary>The port it listens on.</summary>
    public int Port { get; }

    /// <summary>Subscribe requests received.</summary>
    public int Subscriptions => Volatile.Read(ref _subscriptions);

    /// <summary>Everything published so far, in order.</summary>
    public IReadOnlyList<(string Topic, string Payload, bool Retain)> Published
    {
        get { lock (_gate) return [.. _published]; }
    }

    /// <summary>Starts a broker; with <paramref name="user"/> it refuses other credentials.</summary>
    public static async Task<MqttTestBroker> StartAsync(string? user = null, string? password = null, int? port = null)
    {
        var broker = new MqttTestBroker(port ?? FreePort(), user, password);
        await broker._server.StartAsync();
        return broker;
    }

    /// <summary>A port nothing listens on right now.</summary>
    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Waits (up to 15 s) until a message matching <paramref name="match"/> has been published.</summary>
    public async Task<(string Topic, string Payload, bool Retain)> WaitForAsync(Func<(string Topic, string Payload, bool Retain), bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var m in Published)
                if (match(m)) return m;
            await Task.Delay(20);
        }
        throw new TimeoutException("The expected MQTT message was not published within 15 s.");
    }

    /// <summary>The retained messages the broker holds now.</summary>
    public async Task<IReadOnlyDictionary<string, string>> RetainedAsync()
    {
        var all = await _server.GetRetainedMessagesAsync();
        return all.ToDictionary(m => m.Topic, m => m.ConvertPayloadToString(), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _server.StopAsync();
        _server.Dispose();
    }
}
