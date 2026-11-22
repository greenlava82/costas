// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using System.Net.Sockets;
using Ft8Client.Rig;

namespace Ft8Client.Rig.Tests;

/// <summary>How the rigctld client ends a command when it is closed or cancelled at an awkward moment.</summary>
public class RigctldClientTests
{
    // A rigctld that reads commands and never answers; Received completes when the first command arrives.
    private sealed class SilentServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serve;
        private Socket? _client;

        public SilentServer()
        {
            _listener.Start();
            _serve = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public TaskCompletionSource Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Closes the connection with a reset, so the client's read fails with a connection error.
        public void Reset()
        {
            _client!.LingerState = new LingerOption(true, 0);
            _client.Close();
        }

        private async Task ServeAsync()
        {
            try
            {
                _client = await _listener.AcceptSocketAsync(_cts.Token);
                var buffer = new byte[256];
                if (await _client.ReceiveAsync(buffer, _cts.Token) > 0) Received.TrySetResult();
                await Task.Delay(Timeout.Infinite, _cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            await _serve;
            _listener.Stop();
            _client?.Dispose();
            _cts.Dispose();
        }
    }

    [Fact]
    public async Task DisposeAsync_CommandWaitingForReply_CommandFailsAsRigException()
    {
        // rigctld is restarted while a command waits on the old connection: the rig disposes that client. The command
        // used to fail with ObjectDisposedException from the client's lock (Linux CI), which nothing above expects.
        await using var server = new SilentServer();
        var client = new RigctldClient("127.0.0.1", server.Port, TimeSpan.FromMinutes(5));
        var command = client.SendAsync("f", CancellationToken.None);
        await server.Received.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        await command.Awaiting(c => c).Should().ThrowAsync<RigException>().WithMessage("*closed*");
        await client.Awaiting(c => c.SendAsync("f", CancellationToken.None)).Should().ThrowAsync<RigException>();
    }

    [Fact]
    public async Task SendAsync_ConnectionFailsAfterCallerCancelled_ThrowsCancellation()
    {
        // A start-up that is cancelled while rigctld refuses or drops the connection must see the cancellation (it then
        // stops the rigctld it started), never the connection error. The cancel is made first and the connection reset
        // after, so whichever the client notices first, the caller is told it was cancelled.
        await using var server = new SilentServer();
        await using var client = new RigctldClient("127.0.0.1", server.Port, TimeSpan.FromMinutes(5));
        using var cts = new CancellationTokenSource();
        var command = client.SendAsync("f", cts.Token);
        await server.Received.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        server.Reset();

        await command.Awaiting(c => c).Should().ThrowAsync<OperationCanceledException>();
    }
}
