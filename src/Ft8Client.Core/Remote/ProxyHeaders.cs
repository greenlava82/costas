// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;

namespace Ft8Client.Core.Remote;

/// <summary>
/// What a reverse proxy such as nginx says about a request: whether one forwarded it, the address of the browser, the
/// scheme the browser used, and the host it asked for. Only a proxy on the local network is believed; the address
/// taken is the last one in the list, which the nearest proxy added (earlier ones can be made up by the browser).
/// </summary>
public sealed record ProxyHeaders(bool Proxied, IPAddress? Client, string? Proto, string? Host)
{
    /// <summary>A request straight from the browser.</summary>
    public static ProxyHeaders Direct(IPAddress? peer) => new(false, peer, null, null);

    /// <summary>Reads the headers of a request from <paramref name="peer"/>.</summary>
    /// <param name="peer">The address the connection came from.</param>
    /// <param name="forwardedFor"><c>X-Forwarded-For</c>.</param>
    /// <param name="forwardedProto"><c>X-Forwarded-Proto</c>.</param>
    /// <param name="forwardedHost"><c>X-Forwarded-Host</c>.</param>
    /// <param name="forwarded"><c>Forwarded</c> (RFC 7239).</param>
    public static ProxyHeaders Read(IPAddress? peer, string? forwardedFor, string? forwardedProto, string? forwardedHost, string? forwarded)
    {
        var proxied = !string.IsNullOrWhiteSpace(forwardedFor) || !string.IsNullOrWhiteSpace(forwardedProto)
                      || !string.IsNullOrWhiteSpace(forwardedHost) || !string.IsNullOrWhiteSpace(forwarded);
        if (!proxied) return Direct(peer);
        // A request from outside the local network is refused anyway; its headers are not believed.
        if (!LocalNetwork.Contains(peer)) return new ProxyHeaders(true, peer, null, null);

        string? forName = Last(forwardedFor), proto = Last(forwardedProto), host = Last(forwardedHost);
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            foreach (var pair in Last(forwarded)!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = pair.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0) continue;
                var value = pair[(eq + 1)..].Trim('"');
                switch (pair[..eq].Trim().ToLowerInvariant())
                {
                    case "for": forName ??= value; break;
                    case "proto": proto ??= value; break;
                    case "host": host ??= value; break;
                }
            }
        }
        return new ProxyHeaders(true, ParseAddress(forName) ?? peer, proto?.ToLowerInvariant(), host);
    }

    private static string? Last(string? list)
    {
        if (string.IsNullOrWhiteSpace(list)) return null;
        var last = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        return string.IsNullOrEmpty(last) ? null : last;
    }

    // "203.0.113.7", "203.0.113.7:4711", "[2001:db8::1]" or "[2001:db8::1]:4711".
    private static IPAddress? ParseAddress(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var t = text.Trim('"');
        if (t.StartsWith('['))
        {
            var close = t.IndexOf(']', StringComparison.Ordinal);
            if (close > 0) t = t[1..close];
        }
        else if (t.Count(c => c == ':') == 1)
        {
            t = t[..t.IndexOf(':', StringComparison.Ordinal)];
        }
        return IPAddress.TryParse(t, out var a) ? a : null;
    }
}
