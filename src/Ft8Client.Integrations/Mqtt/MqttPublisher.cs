// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Diagnostics;
using System.Security.Authentication;
using System.Text;
using Ft8Client.Core.Mqtt;
using Ft8Client.Core.Services;
using MQTTnet;
using MQTTnet.Adapter;
using MQTTnet.Exceptions;
using MQTTnet.Protocol;

namespace Ft8Client.Integrations.Mqtt;

/// <summary>
/// Publishes Costas's station events to an MQTT broker. One way only: it never subscribes, so nothing on the broker
/// can affect Costas. Callers never wait on it: <see cref="Publish"/> only queues.
/// <list type="bullet">
/// <item>Connects with a last will of "offline" (retained) on the availability topic, so a crash or a frozen PC shows
/// as offline within about 1.5 keep-alives.</item>
/// <item>On every connect it first publishes the current retained state from the caller, overwriting anything stale
/// left by a crash, and only then "online".</item>
/// <item>While disconnected it keeps only the latest retained message per topic and drops events: nothing is replayed
/// late. Reconnects back off from 1 s to 60 s.</item>
/// </list>
/// </summary>
public sealed class MqttPublisher : IAsyncDisposable
{
    private const int MaxQueuedEvents = 50;

    private readonly MqttBrokerOptions _options;
    private readonly MqttTopics _topics;
    private readonly Func<IReadOnlyList<MqttOutgoing>> _stateOnConnect;
    private readonly Backoff _backoff = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
    private readonly object _gate = new();
    private readonly Dictionary<string, MqttOutgoing> _retained = new(StringComparer.Ordinal);
    private readonly List<string> _retainedOrder = [];
    private readonly Queue<MqttOutgoing> _events = new();
    private readonly SemaphoreSlim _wake = new(0);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private IMqttClient? _client;
    private volatile bool _connected;
    private volatile bool _disposed;

    /// <summary>Creates a publisher; nothing connects until <see cref="Start"/>.</summary>
    /// <param name="options">Broker and credentials.</param>
    /// <param name="topics">The station's topics (availability is used for the last will).</param>
    /// <param name="stateOnConnect">The current retained state, published right after each connect, before "online".</param>
    public MqttPublisher(MqttBrokerOptions options, MqttTopics topics, Func<IReadOnlyList<MqttOutgoing>> stateOnConnect)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _topics = topics ?? throw new ArgumentNullException(nameof(topics));
        _stateOnConnect = stateOnConnect ?? throw new ArgumentNullException(nameof(stateOnConnect));
    }

    /// <summary>Raised when the connection status changes, on a background thread.</summary>
    public event Action<ServiceStatus>? StatusChanged;

    /// <summary>Connected to the broker now.</summary>
    public bool Connected => _connected;

    /// <summary>When the last message was accepted by the broker, or null.</summary>
    public DateTime? LastPublishUtc { get; private set; }

    /// <summary>Starts connecting in the background.</summary>
    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>
    /// Queues a message. Never blocks and never throws for broker trouble, nor once the publisher is disposed: callers
    /// include the transmitter's PTT events, which must never fail because of MQTT.
    /// </summary>
    public void Publish(MqttOutgoing message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_disposed) return;
        lock (_gate)
        {
            if (message.Retain)
            {
                if (_retained.TryAdd(message.Topic, message)) _retainedOrder.Add(message.Topic);
                else _retained[message.Topic] = message;
            }
            else
            {
                if (!_connected) return; // events are never replayed late
                if (_events.Count >= MaxQueuedEvents) _events.Dequeue();
                _events.Enqueue(message);
            }
        }
        try
        {
            _wake.Release();
        }
        catch (ObjectDisposedException)
        {
            // Disposed while this was queuing: nothing more is sent.
        }
    }

    /// <summary>
    /// Publishes <paramref name="final"/> (such as the session ending) and "offline", then disconnects cleanly. Gives up
    /// after <paramref name="timeout"/>; the last will covers anything not sent.
    /// </summary>
    public async Task StopAsync(IReadOnlyList<MqttOutgoing> final, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(final);
        var cts = _cts;
        if (cts is null) return;
        await cts.CancelAsync().ConfigureAwait(false);
        if (_loop is { } loop)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        var client = _client;
        if (client?.IsConnected == true)
        {
            using var stop = new CancellationTokenSource(timeout);
            try
            {
                foreach (var m in final) await SendAsync(client, m, stop.Token).ConfigureAwait(false);
                await SendAsync(client, Availability(MqttPayloads.Offline), stop.Token).ConfigureAwait(false);
                await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The broker sends the last will ("offline") when the connection drops.
            }
        }
        client?.Dispose();
        _client = null;
        _connected = false;
        _loop = null;
        _cts = null;
        cts.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync([], TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        _disposed = true;
        _wake.Dispose();
    }

    /// <summary>
    /// Connects once, publishes a test message (not retained) and disconnects, for Settings' Test connection. Says what
    /// went wrong in plain words.
    /// </summary>
    public static async Task<MqttTestResult> TestAsync(MqttBrokerOptions options, MqttTopics topics, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(topics);
        using var client = new MqttClientFactory().CreateMqttClient();
        var watch = Stopwatch.StartNew();
        try
        {
            var testOptions = options with { ClientId = $"{options.ClientId}-test" };
            await ConnectAsync(client, BuildOptions(testOptions, topics, withWill: false), ct).ConfigureAwait(false);
            await SendAsync(client, new MqttOutgoing($"{topics.Base}/test", "Costas connection test", false), ct).ConfigureAwait(false);
            var rtt = watch.Elapsed;
            await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), ct).ConfigureAwait(false);
            return new MqttTestResult(true, $"Connected to {options}. A test message was published in {rtt.TotalMilliseconds:0} ms.", rtt);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new MqttTestResult(false, Explain(ex, options), null);
        }
    }

    /// <summary>Plain words for a connection failure.</summary>
    internal static string Explain(Exception ex, MqttBrokerOptions options)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case BrokerRefusedException r when r.Code is MqttClientConnectResultCode.BadUserNameOrPassword
                    or MqttClientConnectResultCode.NotAuthorized:
                    return "The broker refused the user name or password.";
                case BrokerRefusedException r:
                    return $"The broker refused the connection ({r.Code}).";
                // The exception carries the broker's result code only in its message.
                case MqttConnectingFailedException f when f.Message.Contains(nameof(MqttClientConnectResultCode.BadUserNameOrPassword), StringComparison.Ordinal)
                    || f.Message.Contains(nameof(MqttClientConnectResultCode.NotAuthorized), StringComparison.Ordinal):
                    return "The broker refused the user name or password.";
                case MqttConnectingFailedException f:
                    return $"The broker refused the connection: {f.Message}";
                case AuthenticationException:
                    return "The broker's TLS certificate is not trusted by this computer.";
                case System.Net.Sockets.SocketException s when s.SocketErrorCode is System.Net.Sockets.SocketError.HostNotFound
                    or System.Net.Sockets.SocketError.NoData:
                    return $"No computer called \"{options.Host}\" was found on the network.";
                case System.Net.Sockets.SocketException:
                    return $"Nothing answered at {options.Host} port {options.Port}. Is the broker running?";
                case TimeoutException or MqttCommunicationTimedOutException:
                    return $"{options.Host} port {options.Port} did not answer in time.";
            }
        }
        return $"Could not connect to {options}: {ex.Message}";
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            IMqttClient? client = null;
            try
            {
                client = new MqttClientFactory().CreateMqttClient();
                client.DisconnectedAsync += _ =>
                {
                    _connected = false;
                    disconnected.TrySetResult();
                    return Task.CompletedTask;
                };
                _client = client;
                await ConnectAsync(client, BuildOptions(_options, _topics, withWill: true), ct).ConfigureAwait(false);
                // The caller's current state replaces anything queued while offline; changes from here on queue again.
                lock (_gate)
                {
                    _events.Clear();
                    _retained.Clear();
                    _retainedOrder.Clear();
                }
                // Current state first, overwriting anything a crash left retained, and only then "online".
                foreach (var m in _stateOnConnect()) await SendAsync(client, m, ct).ConfigureAwait(false);
                await SendAsync(client, Availability(MqttPayloads.Online), ct).ConfigureAwait(false);
                LastPublishUtc = DateTime.UtcNow;
                _connected = true;
                _backoff.Reset();
                StatusChanged?.Invoke(new ServiceStatus(ServiceHealth.Ok, $"Publishing to {_options}", DateTime.UtcNow));
                await PumpAsync(client, disconnected.Task, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                    StatusChanged?.Invoke(new ServiceStatus(ServiceHealth.Degraded, "Broker connection lost; reconnecting", DateTime.UtcNow));
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                // Stopping. A publish cancelled while it waits for the broker's acknowledgement throws
                // MqttCommunicationTimedOutException, not OperationCanceledException, but the connection is still
                // good: leave the client for StopAsync, which sends the final messages on it.
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _connected = false;
                StatusChanged?.Invoke(new ServiceStatus(ServiceHealth.Down, Explain(ex, _options), DateTime.UtcNow));
            }
            // Not stopping: this connection is finished, the next attempt makes a new client.
            _connected = false;
            _client = null;
            client?.Dispose();
            try
            {
                await Task.Delay(_backoff.NextDelay(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PumpAsync(IMqttClient client, Task disconnected, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !disconnected.IsCompleted)
        {
            var woke = _wake.WaitAsync(ct);
            await Task.WhenAny(woke, disconnected).ConfigureAwait(false);
            if (disconnected.IsCompleted) return;
            await woke.ConfigureAwait(false);
            while (Next() is { } m)
            {
                try
                {
                    await SendAsync(client, m, ct).ConfigureAwait(false);
                    LastPublishUtc = DateTime.UtcNow;
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    if (m.Retain) Publish(m); // keep the latest state for the next connection
                    throw;
                }
            }
        }
        ct.ThrowIfCancellationRequested();
    }

    private MqttOutgoing? Next()
    {
        lock (_gate)
        {
            if (_retainedOrder.Count > 0)
            {
                var topic = _retainedOrder[0];
                _retainedOrder.RemoveAt(0);
                var m = _retained[topic];
                _retained.Remove(topic);
                return m;
            }
            return _events.Count > 0 ? _events.Dequeue() : null;
        }
    }

    private MqttOutgoing Availability(string payload) => new(_topics.Availability, payload, true);

    private static MqttClientOptions BuildOptions(MqttBrokerOptions o, MqttTopics topics, bool withWill)
    {
        var b = new MqttClientOptionsBuilder()
            .WithTcpServer(o.Host, o.Port)
            .WithClientId(o.ClientId)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(MqttBrokerOptions.KeepAlive)
            .WithTimeout(MqttBrokerOptions.ConnectTimeout);
        if (o.Tls) b = b.WithTlsOptions(t => t.UseTls(true));
        if (!string.IsNullOrEmpty(o.Username)) b = b.WithCredentials(o.Username, o.Password ?? string.Empty);
        if (withWill)
        {
            b = b.WithWillTopic(topics.Availability)
                .WithWillPayload(Encoding.UTF8.GetBytes(MqttPayloads.Offline))
                .WithWillRetain(true)
                .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);
        }
        return b.Build();
    }

    private static async Task ConnectAsync(IMqttClient client, MqttClientOptions options, CancellationToken ct)
    {
        var result = await client.ConnectAsync(options, ct).ConfigureAwait(false);
        if (result.ResultCode != MqttClientConnectResultCode.Success) throw new BrokerRefusedException(result.ResultCode);
    }

    private static Task SendAsync(IMqttClient client, MqttOutgoing m, CancellationToken ct) =>
        client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(m.Topic)
            .WithPayload(m.Payload)
            .WithRetainFlag(m.Retain)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), ct);
}
