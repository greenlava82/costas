// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using Ft8Client.Core.Services;

namespace Ft8Client.Rig.Flex;

/// <summary>
/// A connection to a FlexRadio's SmartSDR TCP API (port 4992) for the amplifier and tuner paired with it
/// (docs/05-integrations.md section 2a). It subscribes to their status and sends a command only when asked; nothing is
/// sent on connecting that changes the radio, the amplifier or the tuner. It never registers as a GUI client and never
/// asks the radio to transmit. The connection is kept open and checked with a ping every few seconds; after a failure
/// it waits 2 s, doubling to a minute.
/// </summary>
public sealed class FlexAccessoryClient : IAsyncDisposable
{
    /// <summary>The radio's API port.</summary>
    public const int DefaultPort = 4992;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HealthyAfter = TimeSpan.FromMinutes(1);

    private readonly string _host;
    private readonly int _port;
    private readonly Func<DateTime> _now;
    private readonly Backoff _backoff;
    private readonly TimeSpan _pingEvery;
    private readonly TimeSpan _replyTimeout;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<FlexReply>> _pending = new();
    private readonly object _lock = new();
    private FlexAccessories _current = FlexAccessories.Empty;
    private Task _loop = Task.CompletedTask;
    private volatile NetworkStream? _stream;
    private volatile bool _connected;
    private int _sequence;
    private int _subscription = -1;

    /// <summary>
    /// Creates a client for the radio at <paramref name="host"/>. <paramref name="pingEvery"/> and
    /// <paramref name="replyTimeout"/> are shortened in tests.
    /// </summary>
    public FlexAccessoryClient(string host, int port = DefaultPort, Func<DateTime>? now = null, Backoff? backoff = null,
                               TimeSpan? pingEvery = null, TimeSpan? replyTimeout = null)
    {
        _host = host;
        _port = port;
        _now = now ?? (() => DateTime.UtcNow);
        _backoff = backoff ?? new Backoff(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));
        _pingEvery = pingEvery ?? TimeSpan.FromSeconds(5);
        _replyTimeout = replyTimeout ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Raised when what is known about the accessories changes, on the reading thread.</summary>
    public event Action<FlexAccessories>? Changed;

    /// <summary>Raised when the connection comes up, drops or fails.</summary>
    public event Action<ServiceStatus>? StatusChanged;

    /// <summary>True while connected and subscribed.</summary>
    public bool Connected => _connected;

    /// <summary>What is known now; empty while not connected.</summary>
    public FlexAccessories Current
    {
        get { lock (_lock) return _current; }
    }

    /// <summary>Starts connecting; call once.</summary>
    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token));

    /// <summary>
    /// Sends one command and waits for its reply. Returns null when the radio took it, else one line saying why not.
    /// Never retried: a command that got no answer may still have been carried out.
    /// </summary>
    public async Task<string?> SendAsync(string command, CancellationToken ct)
    {
        if (_stream is null || !Connected) return "Not connected to the radio.";
        try
        {
            var reply = await RequestAsync(command, ct).ConfigureAwait(false);
            return reply.Ok ? null
                : reply.Code == FlexReply.UnknownAmplifier ? "The radio no longer knows that device."
                : $"The radio refused it (code {reply.Code:X8}{(reply.Message.Length > 0 ? ", " + reply.Message : string.Empty)}).";
        }
        catch (OperationCanceledException)
        {
            // No reply in time, or the caller gave up waiting (a write that stalled): either way it may have been done.
            return "The radio did not answer.";
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            return "The connection to the radio dropped.";
        }
    }

    private Task<FlexReply> RequestAsync(string command, CancellationToken ct) =>
        RequestAsync(Interlocked.Increment(ref _sequence), command, ct);

    private async Task<FlexReply> RequestAsync(int seq, string command, CancellationToken ct)
    {
        var stream = _stream ?? throw new InvalidOperationException("Not connected");
        var tcs = new TaskCompletionSource<FlexReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[seq] = tcs;
        try
        {
            var bytes = Encoding.ASCII.GetBytes(FlexCommands.Frame(seq, command));
            await _write.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            }
            finally
            {
                _write.Release();
            }
            return await tcs.Task.WaitAsync(_replyTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new OperationCanceledException("No reply");
        }
        finally
        {
            _pending.TryRemove(seq, out _);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var started = _now();
            var wasUp = false;
            try
            {
                using var tcp = new TcpClient { NoDelay = true };
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connect.CancelAfter(ConnectTimeout);
                    await tcp.ConnectAsync(_host, _port, connect.Token).ConfigureAwait(false);
                }
                await using var stream = tcp.GetStream();
                _stream = stream;
                using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var reading = ReadAsync(stream, session.Token);
                var seq = Interlocked.Increment(ref _sequence);
                Volatile.Write(ref _subscription, seq);
                var sub = await RequestAsync(seq, FlexCommands.SubscribeAmplifiers, session.Token).ConfigureAwait(false);
                if (!sub.Ok) throw new IOException($"The radio refused the subscription (code {sub.Code:X8})");
                wasUp = true;
                StatusChanged?.Invoke(new ServiceStatus(ServiceHealth.Ok, $"Connected to the radio at {_host}", _now()));
                var pinging = PingAsync(session.Token);
                await Task.WhenAny(reading, pinging).ConfigureAwait(false);
                await session.CancelAsync().ConfigureAwait(false);
                await Quietly(reading).ConfigureAwait(false);
                await Quietly(pinging).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                    StatusChanged?.Invoke(new ServiceStatus(ServiceHealth.Degraded, "Connection to the radio lost; reconnecting", _now()));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                var why = ex is OperationCanceledException ? "no answer" : ex.Message;
                StatusChanged?.Invoke(new ServiceStatus(wasUp ? ServiceHealth.Degraded : ServiceHealth.Down,
                    wasUp ? "Connection to the radio lost; reconnecting" : $"Could not reach the radio at {_host}: {why}", _now()));
            }
            finally
            {
                _connected = false;
                Volatile.Write(ref _subscription, -1);
                _stream = null;
                foreach (var p in _pending.Values) p.TrySetCanceled(CancellationToken.None);
                Use(FlexAccessories.Empty);
            }
            if (_now() - started >= HealthyAfter) _backoff.Reset();
            try
            {
                await Task.Delay(_backoff.NextDelay(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task Quietly(Task t)
    {
        try
        {
            await t.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            // The session is over either way.
        }
    }

    // A ping that is not answered in time ends the session, so a radio switched off is noticed within seconds.
    private async Task PingAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_pingEvery, ct).ConfigureAwait(false);
            await RequestAsync(FlexCommands.Ping, ct).ConfigureAwait(false);
        }
    }

    // Lines end in LF, CR or CR LF (SmartSDR API wiki). Reads until the radio closes or the session ends.
    private async Task ReadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var line = new StringBuilder();
        while (!ct.IsCancellationRequested)
        {
            var n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0) return;
            for (var i = 0; i < n; i++)
            {
                var c = (char)buffer[i];
                if (c is '\r' or '\n')
                {
                    if (line.Length > 0) OnLine(line.ToString());
                    line.Clear();
                }
                else if (line.Length < 16_384)
                {
                    line.Append(c);
                }
            }
        }
    }

    private void OnLine(string line)
    {
        if (FlexReply.Parse(line) is { } reply)
        {
            // Connected is set here, on the reading thread, before any status line after the reply is read, so a device is
            // never shown while commands to it would still be refused as "not connected".
            if (reply.Ok && reply.Sequence == Volatile.Read(ref _subscription)) _connected = true;
            if (_pending.TryGetValue(reply.Sequence, out var tcs)) tcs.TrySetResult(reply);
            return;
        }
        if (FlexStatusLine.Parse(line) is { } status) Use(Current.With(status));
    }

    private void Use(FlexAccessories next)
    {
        bool changed;
        lock (_lock)
        {
            changed = !next.SameAs(_current);
            _current = next;
        }
        if (changed) Changed?.Invoke(next);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
        _cts.Dispose();
        _write.Dispose();
    }
}
