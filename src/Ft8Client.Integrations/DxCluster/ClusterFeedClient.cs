// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Net.Sockets;
using System.Text;
using Ft8Client.Core.Cluster;
using Ft8Client.Core.Messages;
using Ft8Client.Core.Services;

namespace Ft8Client.Integrations.DxCluster;

/// <summary>
/// One DX cluster node over telnet (docs/05-integrations.md 5b): logs in with my callsign at the node's prompt and raises
/// every spot line it sends. Nothing else is ever sent: no filters, no spots, no commands, except <c>bye</c> when it
/// stops, as the DXSpider manual asks. The connection is kept open; after a failure it waits 30 s, doubling to 10
/// minutes, so a node that is down is not hammered. A node that asks for a password is not for Costas: it waits an hour
/// before trying again.
/// </summary>
public sealed class ClusterFeedClient : IAsyncDisposable
{
    /// <summary>The usual telnet port of CC Cluster and AR-Cluster nodes, the default in Settings.</summary>
    public const int DefaultPort = 7300;

    /// <summary>How long a node that asked for a password is left alone.</summary>
    public static readonly TimeSpan PasswordWait = TimeSpan.FromHours(1);

    private static readonly TimeSpan HealthyAfter = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LoginWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly string _host;
    private readonly int _port;
    private readonly Func<DateTime> _now;
    private readonly Backoff _backoff;
    private readonly TimeSpan _idle;
    private readonly CancellationTokenSource _cts = new();
    private Task _loop = Task.CompletedTask;
    private volatile bool _connected;
    private volatile NetworkStream? _stream;
    private bool _password;

    /// <summary>
    /// Creates a client for one node. <paramref name="idle"/> is how long a silent connection is trusted before it is
    /// dropped and made again (a public node passes on spots every few seconds).
    /// </summary>
    public ClusterFeedClient(string host, int port, Func<DateTime>? now = null, Backoff? backoff = null, TimeSpan? idle = null)
    {
        _host = host;
        _port = port;
        _now = now ?? (() => DateTime.UtcNow);
        _backoff = backoff ?? new Backoff(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10));
        _idle = idle ?? TimeSpan.FromMinutes(15);
    }

    /// <summary>Raised for each spot, on the reading thread.</summary>
    public event Action<ClusterSpot>? Spot;

    /// <summary>Raised when the connection comes up, drops or fails.</summary>
    public event Action<ServiceStatus>? StatusChanged;

    /// <summary>True while logged in to the node.</summary>
    public bool Connected => _connected;

    /// <summary>Spot lines read since the start.</summary>
    public long LinesRead { get; private set; }

    /// <summary>Starts with <paramref name="myCall"/> as the login; call once.</summary>
    public void Start(string myCall)
    {
        var call = Callsign.Normalize(myCall);
        _loop = Task.Run(() => RunAsync(call, _cts.Token));
    }

    private async Task RunAsync(string myCall, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var started = _now();
            _password = false;
            try
            {
                using var tcp = new TcpClient();
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connect.CancelAfter(ConnectTimeout);
                    await tcp.ConnectAsync(_host, _port, connect.Token).ConfigureAwait(false);
                }
                await using var stream = tcp.GetStream();
                _stream = stream;
                await ReadAsync(stream, myCall, ct).ConfigureAwait(false);
                if (_password)
                    StatusChanged?.Invoke(new ServiceStatus(ServiceHealth.Down, "This node asks for a password; Costas can only use open nodes", _now()));
                else if (!ct.IsCancellationRequested)
                    StatusChanged?.Invoke(new ServiceStatus(ServiceHealth.Degraded, "Connection lost; reconnecting", _now()));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
            {
                var why = ex is OperationCanceledException ? "no answer" : ex.Message;
                StatusChanged?.Invoke(new ServiceStatus(_connected ? ServiceHealth.Degraded : ServiceHealth.Down,
                    _connected ? "Connection lost; reconnecting" : $"Unreachable: {why}", _now()));
            }
            finally
            {
                _connected = false;
                _stream = null;
            }
            if (_now() - started >= HealthyAfter) _backoff.Reset();
            try
            {
                await Task.Delay(_password ? PasswordWait : _backoff.NextDelay(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Reads until the node closes, the connection goes quiet for too long, or the client stops. The login prompt
    // (DXSpider "login: ", others "Please enter your call:") ends without a new line, so it is looked for in the
    // unfinished line too; a node that sends no prompt gets the callsign after a few seconds anyway.
    private async Task ReadAsync(NetworkStream stream, string myCall, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var line = new StringBuilder();
        var loggedIn = false;
        var skip = 0;
        while (!ct.IsCancellationRequested)
        {
            int n;
            using (var read = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                read.CancelAfter(loggedIn ? _idle : LoginWait);
                try
                {
                    n = await stream.ReadAsync(buffer, read.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    if (loggedIn) return;
                    await LoginAsync(stream, myCall, ct).ConfigureAwait(false);
                    loggedIn = true;
                    continue;
                }
            }
            if (n == 0) return;
            for (var i = 0; i < n; i++)
            {
                var b = buffer[i];
                // Telnet negotiation (IAC, a command and for WILL/WONT/DO/DONT an option) is not text; nothing is agreed to.
                if (skip > 0)
                {
                    skip = skip == 2 && b is >= 251 and <= 254 ? 1 : 0;
                    continue;
                }
                if (b == 255)
                {
                    skip = 2;
                    continue;
                }
                if (b == '\n')
                {
                    OnLine(line.ToString());
                    line.Clear();
                }
                else if (b != '\r' && b != 7 && b < 128 && line.Length < 512)
                {
                    line.Append((char)b);
                }
            }
            var pending = line.ToString();
            if (!loggedIn && IsLoginPrompt(pending))
            {
                line.Clear();
                await LoginAsync(stream, myCall, ct).ConfigureAwait(false);
                loggedIn = true;
            }
            else if (loggedIn && pending.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                _password = true;
                return;
            }
        }
    }

    /// <summary>True for the text a node shows when it wants the callsign (it ends the unfinished line).</summary>
    public static bool IsLoginPrompt(string pending)
    {
        var t = pending.TrimEnd();
        return t.EndsWith("login:", StringComparison.OrdinalIgnoreCase) || t.EndsWith("call:", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("callsign:", StringComparison.OrdinalIgnoreCase) || (t.Contains("call", StringComparison.OrdinalIgnoreCase) && t.EndsWith(':'));
    }

    private async Task LoginAsync(NetworkStream stream, string myCall, CancellationToken ct)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(myCall + "\r\n"), ct).ConfigureAwait(false);
        _connected = true;
        _backoff.Reset();
        StatusChanged?.Invoke(new ServiceStatus(ServiceHealth.Ok, "Connected", _now()));
    }

    private void OnLine(string line)
    {
        if (!line.StartsWith("DX de ", StringComparison.Ordinal)) return;
        LinesRead++;
        if (ClusterSpotParser.Parse(line, _now()) is { } spot) Spot?.Invoke(spot);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Say goodbye so the node does not keep a stale session under my callsign (DXSpider manual, "Logouts").
        if (_connected && _stream is { } s)
        {
            try
            {
                using var bye = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await s.WriteAsync(Encoding.ASCII.GetBytes("bye\r\n"), bye.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
            {
                // Already gone.
            }
        }
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _cts.Dispose();
    }
}
