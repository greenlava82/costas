// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Settings;

/// <summary>Operating from a phone on the home network.</summary>
public sealed class RemoteOptions
{
    /// <summary>Serve the remote page. Off until the operator turns it on.</summary>
    public bool Enabled { get; set; }

    /// <summary>TCP port for the remote page.</summary>
    public int Port { get; set; } = 8073;

    /// <summary>
    /// Every device must sign in with a username and password from <see cref="Users"/>. Off by default: at home and over
    /// Tailscale a phone just opens the page. Needed behind a reverse proxy, which the page refuses without it.
    /// </summary>
    public bool RequireSignIn { get; set; }

    /// <summary>
    /// The address of a reverse proxy on another computer, whose proxy headers are believed (those from this computer
    /// always are). Empty when the proxy runs on this computer or there is none.
    /// </summary>
    public string? TrustedProxy { get; set; }

    /// <summary>Who may sign in. Their password hashes and sign-ins are in the secret store.</summary>
    public List<RemoteUser> Users { get; set; } = [];
}
