// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net.Sockets;
using System.Text;

namespace Ft8Client.Rig.Flex;

/// <summary>
/// Asks a FlexRadio which slices are open, over its SmartSDR TCP API (<c>sub slice all</c>), so Setup can choose the
/// SmartSDR Slice model for the slice being operated. Read only: it subscribes, listens briefly and disconnects.
/// </summary>
public static class FlexSliceProbe
{
    /// <summary>The radio's slices, or an empty list when it cannot be reached in time.</summary>
    public static async Task<IReadOnlyList<FlexSlice>> ProbeAsync(string host, int port, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(TimeSpan.FromSeconds(4));
        var lines = new List<string>();
        try
        {
            using var tcp = new TcpClient { NoDelay = true };
            await tcp.ConnectAsync(host, port, stop.Token).ConfigureAwait(false);
            await using var stream = tcp.GetStream();
            var command = Encoding.ASCII.GetBytes(FlexCommands.Frame(1, "sub slice all"));
            await stream.WriteAsync(command, stop.Token).ConfigureAwait(false);
            // The radio answers with one status line per slice straight away; give it a moment, then stop.
            var listen = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            listen.CancelAfter(TimeSpan.FromSeconds(1.5));
            var buffer = new byte[8192];
            var pending = new StringBuilder();
            try
            {
                while (true)
                {
                    var n = await stream.ReadAsync(buffer, listen.Token).ConfigureAwait(false);
                    if (n == 0) break;
                    pending.Append(Encoding.ASCII.GetString(buffer, 0, n));
                    var text = pending.ToString();
                    var last = text.LastIndexOf('\n');
                    if (last < 0) continue;
                    lines.AddRange(text[..last].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    pending.Clear().Append(text[(last + 1)..]);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Listened long enough.
            }
            finally
            {
                listen.Dispose();
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return [];
        }
        return FlexSlice.FromLines(lines);
    }
}
