// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ft8Client.Rig.Tests.Flex;

/// <summary>
/// A stand-in for a FlexRadio's SmartSDR TCP API on a loopback port: greets with V and H lines, answers every command
/// with <c>R&lt;seq&gt;|0|</c> (or the code set for it), records what was sent, and sends status lines on request.
/// </summary>
internal sealed class FakeFlexRadio : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _accept;
    private volatile NetworkStream? _client;

    public FakeFlexRadio()
    {
        _listener.Start();
        _accept = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Every command received, without its C&lt;seq&gt;| prefix.</summary>
    public ConcurrentQueue<string> Commands { get; } = new();

    /// <summary>Status lines sent as soon as the subscription arrives.</summary>
    public List<string> OnSubscribe { get; } = [];

    /// <summary>Reply codes for commands starting with a given text; others get 0.</summary>
    public ConcurrentDictionary<string, string> Codes { get; } = new();

    /// <summary>When false, commands get no reply at all.</summary>
    public volatile bool Answers = true;

    public int Connections;

    public async Task SendAsync(string line)
    {
        var c = _client ?? throw new InvalidOperationException("No client");
        await c.WriteAsync(Encoding.ASCII.GetBytes(line + "\n"));
    }

    /// <summary>Drops the client's connection.</summary>
    public void Drop() => _client?.Socket.Close();

    private async Task AcceptAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var tcp = await _listener.AcceptTcpClientAsync(_cts.Token);
                Interlocked.Increment(ref Connections);
                _ = Task.Run(() => ServeAsync(tcp));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }

    private async Task ServeAsync(TcpClient tcp)
    {
        using var _ = tcp;
        var stream = tcp.GetStream();
        _client = stream;
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("V1.4.0.0\nH2A7B9C01\n"), _cts.Token);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            while (await reader.ReadLineAsync(_cts.Token) is { } line)
            {
                var bar = line.IndexOf('|');
                if (line[0] != 'C' || bar < 0) continue;
                var seq = line[1..bar];
                var cmd = line[(bar + 1)..];
                Commands.Enqueue(cmd);
                if (!Answers) continue;
                var code = Codes.FirstOrDefault(k => cmd.StartsWith(k.Key, StringComparison.Ordinal)).Value ?? "0";
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"R{seq}|{code}|\n"), _cts.Token);
                if (cmd == "sub amplifier all")
                    foreach (var s in OnSubscribe) await stream.WriteAsync(Encoding.ASCII.GetBytes(s + "\n"), _cts.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        _client?.Socket.Close();
        try { await _accept; } catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
