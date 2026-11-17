// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Settings;

/// <summary>
/// Someone who may sign in to the remote page. The id never changes, so renaming keeps their sign-ins (and, later, the
/// station profile they operate as). The password hash is in the secret store under this id, never here.
/// </summary>
public sealed class RemoteUser
{
    /// <summary>Stable random id.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The name typed to sign in; compared without regard to case.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>May sign in. Turning it off signs the user out everywhere.</summary>
    public bool Enabled { get; set; } = true;
}
