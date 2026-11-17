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
/// What a reverse proxy such as nginx says about a request: whether it carries proxy headers at all, whether they come
/// from a proxy Costas believes, and if so the address of the browser, the scheme the browser used, and the host it asked
/// for. Only this computer (loopback) and the proxy address set in Settings are believed: any other device could make the
/// headers up. The address taken is the last one in the list, which the nearest proxy added (earlier ones can be made up
/// by the browser).
/// </summary>
/// <param name="Proxied">The request carries proxy headers, believed or not.</param>
/// <param name="Trusted">They came from loopback or the configured proxy, so the values below are the proxy's.</param>
/// <param name="Client">The browser's address when trusted, otherwise the address the connection came from.</param>
/// <param name="Proto">The scheme the browser used ("https"), when trusted.</param>
/// <param name="Host">The host the browser asked for, when trusted.</param>
public sealed record ProxyHeaders(bool Proxied, bool Trusted, IPAddress? Client, string? Proto, string? Host)
{
    /// <summary>A request straight from the browser.</summary>
    public static ProxyHeaders Direct(IPAddress? peer) => new(false, false, peer, null, null);

    /// <summary>True when proxy headers from <paramref name="peer"/> are believed: this computer, or the configured proxy.</summary>
    public static bool IsTrustedProxy(IPAddress? peer, IPAddress? trustedProxy)
    {
        if (peer is null) return false;
        if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();
        if (IPAddress.IsLoopback(peer)) return true;
        if (trustedProxy is null) return false;
        if (trustedProxy.IsIPv4MappedToIPv6) trustedProxy = trustedProxy.MapToIPv4();
        return peer.Equals(trustedProxy);
    }

    /// <summary>Reads the headers of a request from <paramref name="peer"/>.</summary>
    /// <param name="peer">The address the connection came from.</param>
    /// <param name="trustedProxy">The proxy address set in Settings, or null for this computer only.</param>
    /// <param name="forwardedFor"><c>X-Forwarded-For</c>.</param>
    /// <param name="forwardedProto"><c>X-Forwarded-Proto</c>.</param>
    /// <param name="forwardedHost"><c>X-Forwarded-Host</c>.</param>
    /// <param name="forwarded"><c>Forwarded</c> (RFC 7239).</param>
    public static ProxyHeaders Read(IPAddress? peer, IPAddress? trustedProxy, string? forwardedFor, string? forwardedProto, string? forwardedHost,
                                    string? forwarded)
    {
        var proxied = !string.IsNullOrWhiteSpace(forwardedFor) || !string.IsNullOrWhiteSpace(forwardedProto)
                      || !string.IsNullOrWhiteSpace(forwardedHost) || !string.IsNullOrWhiteSpace(forwarded);
        if (!proxied) return Direct(peer);
        // From any other device the headers could be made up: not believed.
        if (!IsTrustedProxy(peer, trustedProxy)) return new ProxyHeaders(true, false, peer, null, null);

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
        return new ProxyHeaders(true, true, ParseAddress(forName) ?? peer, proto?.ToLowerInvariant(), host);
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
