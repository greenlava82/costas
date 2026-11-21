// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ft8Client.Rig.Flex;

/// <summary>
/// Finds FlexRadio 6000 and 8000 series radios on the local network. Each radio broadcasts a VITA-49 discovery packet
/// to UDP port 4992 about once a second, whether or not a client is connected; its payload is ASCII
/// <c>key=value</c> pairs separated by spaces (<c>model=FLEX-8600 serial=… nickname=… ip=192.168.1.20 port=4992 …</c>,
/// FlexLib <c>Discovery.cs</c>). The socket shares the port with SmartSDR or other clients listening on it.
/// </summary>
public static class FlexDiscovery
{
    /// <summary>The UDP port FlexRadio discovery packets are broadcast to.</summary>
    public const int Port = 4992;

    /// <summary>Reads one discovery packet, or returns null when it is not one (no model or no valid IP address).</summary>
    public static FlexRadioInfo? Parse(ReadOnlySpan<byte> packet)
    {
        var text = Encoding.Latin1.GetString(packet);
        var start = text.IndexOf("discovery_protocol_version=", StringComparison.Ordinal);
        if (start < 0) start = text.IndexOf("model=", StringComparison.Ordinal);
        if (start < 0) return null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in text[start..].Split([' ', '\0', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = word.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0) values[word[..eq]] = word[(eq + 1)..];
        }
        string Get(string key) => values.TryGetValue(key, out var v) ? v : string.Empty;
        var model = Get("model");
        var ip = Get("ip");
        if (model.Length == 0 || !IPAddress.TryParse(ip, out _)) return null;
        var port = int.TryParse(Get("port"), NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is > 0 and < 65536 ? p : Port;
        // FlexLib turns the underscores it sends in place of spaces back into spaces.
        return new FlexRadioInfo(model, Get("serial"), Get("nickname").Replace('_', ' '), Get("callsign"), ip, port);
    }

    /// <summary>
    /// Listens for <paramref name="duration"/> and returns each radio heard once, in the order heard. Returns what it
    /// has when cancelled, and an empty list when the port cannot be opened.
    /// </summary>
    public static async Task<IReadOnlyList<FlexRadioInfo>> ListenAsync(TimeSpan duration, CancellationToken ct)
    {
        var found = new List<FlexRadioInfo>();
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
        }
        catch (SocketException)
        {
            return found;
        }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(duration);
        try
        {
            while (true)
            {
                var r = await udp.ReceiveAsync(stop.Token).ConfigureAwait(false);
                if (Parse(r.Buffer) is { } radio && !found.Any(f => f.Ip == radio.Ip && f.Port == radio.Port)) found.Add(radio);
            }
        }
        catch (OperationCanceledException)
        {
            return found;
        }
        catch (SocketException)
        {
            return found;
        }
    }
}
