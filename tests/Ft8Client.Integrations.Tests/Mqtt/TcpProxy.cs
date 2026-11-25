// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Net;
using System.Net.Sockets;

namespace Ft8Client.Integrations.Tests.Mqtt;

/// <summary>
/// A TCP relay between a client and a broker, for network trouble a broker cannot fake: <see cref="Cut"/> drops every
/// connection as a crashed PC or a pulled cable would (no MQTT DISCONNECT), and <see cref="Freeze"/> stops passing bytes
/// while keeping the sockets open, as a frozen PC or a dead Wi-Fi link does.
/// </summary>
public sealed class TcpProxy : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private volatile int _target;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<(TcpClient In, TcpClient Out)> _pairs = [];
    private readonly Task _accept;
    private volatile bool _frozen;
    private int _accepted;

    /// <summary>Relays loopback connections on a free port to <paramref name="targetPort"/>.</summary>
    public TcpProxy(int targetPort)
    {
        _target = targetPort;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = Task.Run(AcceptLoopAsync);
    }

    /// <summary>The port clients connect to.</summary>
    public int Port { get; }

    /// <summary>
    /// Where new connections go. Point it at a port nothing listens on to make the broker unreachable (each connection is
    /// then refused at once), and back to bring it back, without stopping the proxy's own listener.
    /// </summary>
    public int Target
    {
        get => _target;
        set => _target = value;
    }

    /// <summary>Connections accepted so far.</summary>
    public int Accepted => Volatile.Read(ref _accepted);

    /// <summary>Connections open now.</summary>
    public int Open
    {
        get { lock (_gate) return _pairs.Count; }
    }

    /// <summary>Drops every open connection at once (a reset, no MQTT goodbye on either side).</summary>
    public void Cut()
    {
        List<(TcpClient In, TcpClient Out)> pairs;
        lock (_gate)
        {
            pairs = [.. _pairs];
            _pairs.Clear();
        }
        foreach (var (i, o) in pairs)
        {
            Abort(i);
            Abort(o);
        }
    }

    /// <summary>Stops (true) or resumes (false) passing bytes; connections stay open.</summary>
    public void Freeze(bool frozen) => _frozen = frozen;

    private static void Abort(TcpClient c)
    {
        try
        {
            c.Client.LingerState = new LingerOption(true, 0); // close with a reset
            c.Client.Close();
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
        }
        c.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient inbound;
            try
            {
                inbound = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }
            Interlocked.Increment(ref _accepted);
            var outbound = new TcpClient();
            try
            {
                await outbound.ConnectAsync(IPAddress.Loopback, _target, _cts.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException)
            {
                // Nothing behind the proxy: the client sees its connection refused, as it would without the proxy.
                Abort(inbound);
                outbound.Dispose();
                continue;
            }
            lock (_gate) _pairs.Add((inbound, outbound));
            _ = PumpAsync(inbound, outbound);
            _ = PumpAsync(outbound, inbound);
        }
    }

    private async Task PumpAsync(TcpClient from, TcpClient to)
    {
        var buffer = new byte[8192];
        try
        {
            var src = from.GetStream();
            var dst = to.GetStream();
            while (true)
            {
                var n = await src.ReadAsync(buffer, _cts.Token);
                if (n == 0) break;
                // Frozen: bytes that arrive are swallowed, as on a link that has died without closing.
                if (_frozen) continue;
                await dst.WriteAsync(buffer.AsMemory(0, n), _cts.Token);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException
                                      or InvalidOperationException)
        {
        }
        // One side closed: close the other, as a plain connection would.
        lock (_gate)
        {
            var i = _pairs.FindIndex(p => p.In == from || p.Out == from);
            if (i < 0) return;
            _pairs.RemoveAt(i);
        }
        from.Dispose();
        to.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        // The accept loop ends first, then the listener stops.
        try
        {
            await _accept;
        }
        catch (OperationCanceledException)
        {
        }
        _listener.Stop();
        Cut();
        _cts.Dispose();
    }
}
