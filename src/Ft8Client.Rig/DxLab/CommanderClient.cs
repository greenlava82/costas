// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Net.Sockets;
using System.Text;

namespace Ft8Client.Rig.DxLab;

/// <summary>
/// TCP client for DXLab Commander's command port. One command at a time, each with a timeout; a lost connection is
/// opened again by the next command. Throws <see cref="RigException"/> on any failure.
/// </summary>
public sealed class CommanderClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _closed = new();
    private readonly TimeSpan _timeout;
    private readonly StringBuilder _pending = new();
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private bool _disposed;

    /// <summary>Creates a client for a host and port.</summary>
    public CommanderClient(string host, int port, TimeSpan? timeout = null)
    {
        Host = host;
        Port = port;
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>Host.</summary>
    public string Host { get; }

    /// <summary>Port.</summary>
    public int Port { get; }

    /// <summary>Sends a command that has no reply.</summary>
    public Task SendAsync(string command, CancellationToken ct) => RunAsync(command, null, ct);

    /// <summary>Sends a command and returns the value of its reply tagged <paramref name="replyTag"/> (<c>CmdFreq</c>).</summary>
    public async Task<string> QueryAsync(string command, string replyTag, CancellationToken ct) =>
        (await RunAsync(command, replyTag, ct).ConfigureAwait(false))!;

    private async Task<string?> RunAsync(string command, string? replyTag, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed) throw new RigException("The connection to DXLab Commander was closed.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _closed.Token);
            cts.CancelAfter(_timeout);
            try
            {
                var stream = await EnsureConnectedAsync(cts.Token).ConfigureAwait(false);
                // A reply that arrived after an earlier command gave up would be read as this one's.
                if (replyTag is not null) await DrainAsync(stream).ConfigureAwait(false);
                var bytes = Encoding.Latin1.GetBytes(command);
                await stream.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(cts.Token).ConfigureAwait(false);
                return replyTag is null ? null : await ReadReplyAsync(stream, replyTag, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                Disconnect();
                ct.ThrowIfCancellationRequested();
                throw new RigException(ex is OperationCanceledException
                    ? $"DXLab Commander at {Host}:{Port} did not answer in time."
                    : $"Lost the connection to DXLab Commander at {Host}:{Port}: {ex.Message}", ex);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<NetworkStream> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_stream is not null && _tcp is { Connected: true }) return _stream;
        Disconnect();
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(Host, Port, ct).ConfigureAwait(false);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
        _tcp = tcp;
        _stream = tcp.GetStream();
        _pending.Clear();
        return _stream;
    }

    private async Task DrainAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        while (stream.DataAvailable)
        {
            var n = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (n <= 0) throw new IOException("Commander closed the connection.");
        }
        _pending.Clear();
    }

    private async Task<string> ReadReplyAsync(NetworkStream stream, string replyTag, CancellationToken ct)
    {
        var buffer = new byte[1024];
        while (true)
        {
            while (CommanderProtocol.ReadReply(_pending.ToString(), out var used) is { } reply)
            {
                _pending.Remove(0, used);
                if (string.Equals(reply.Name, replyTag, StringComparison.OrdinalIgnoreCase)) return reply.Value;
            }
            var n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n <= 0) throw new IOException("Commander closed the connection.");
            // Latin-1 keeps one character per byte, so a tag's length counts what arrived, and a stray non-UTF-8 byte
            // (WSJT-X notes Commander can send one as a group separator) cannot break the reply.
            _pending.Append(Encoding.Latin1.GetString(buffer, 0, n));
        }
    }

    private void Disconnect()
    {
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
        _pending.Clear();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _closed.CancelAsync().ConfigureAwait(false);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            Disconnect();
        }
        finally
        {
            _lock.Release();
        }
        _closed.Dispose();
    }
}
