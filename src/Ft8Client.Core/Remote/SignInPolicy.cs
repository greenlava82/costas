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
/// Which requests the remote page serves, and over which a password may be sent. The page itself is plain HTTP, so a
/// password is taken only where something else encrypts it: a believed proxy that says the browser used https, or
/// Tailscale (WireGuard). On home Wi-Fi it would cross the network readable by any other device there.
/// </summary>
public static class SignInPolicy
{
    /// <summary>Said when a request carries proxy headers while sign-in is off.</summary>
    public const string ProxyWithoutSignIn =
        "Costas refuses connections through a proxy while sign-in is off. Turn on \"Require a username and password\" in Settings, Mobile connection.";

    /// <summary>
    /// Why a request must be refused before anything is served, or null to serve it. With sign-in off, any request with
    /// proxy headers is refused: a proxy may carry the whole internet. With sign-in on, every request is served and the
    /// password and token decide.
    /// </summary>
    public static string? RequestRefusal(bool signInRequired, ProxyHeaders proxy) =>
        !signInRequired && proxy.Proxied ? ProxyWithoutSignIn : null;

    /// <summary>
    /// True for a request from this computer itself with no proxy headers while sign-in is off: either a browser on this
    /// computer, or a reverse proxy here set up without its headers, which would open the station to the internet with no
    /// password. Served, but Settings warns.
    /// </summary>
    public static bool SuspectLoopback(bool signInRequired, IPAddress? peer, ProxyHeaders proxy) =>
        !signInRequired && !proxy.Proxied && peer is not null && IPAddress.IsLoopback(peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer);

    /// <summary>Why a password must not be taken over this connection, or null when it may.</summary>
    /// <param name="peer">The address the connection came from.</param>
    /// <param name="local">This computer's address the connection arrived on.</param>
    /// <param name="proxy">The request's proxy headers.</param>
    public static string? PasswordRefusal(IPAddress? peer, IPAddress? local, ProxyHeaders proxy)
    {
        if (proxy.Trusted)
        {
            return proxy.Proto == "https" ? null
                : "Sign in over https: the proxy in front of Costas must use TLS and send X-Forwarded-Proto.";
        }
        if (LocalNetwork.IsTailscale(peer) || LocalNetwork.IsTailscale(local)) return null;
        if (peer is not null && IPAddress.IsLoopback(peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer))
        {
            // A browser on this computer, or a proxy here without its headers: no way to tell whether the password
            // crossed a network in the clear.
            return "Costas cannot tell how this sign-in reached it. Behind a proxy on this computer, have the proxy send "
                   + "X-Forwarded-Proto (see the nginx example in the documentation).";
        }
        return "Sign in through Tailscale or your https address: on home Wi-Fi your password would cross the network unencrypted.";
    }
}
