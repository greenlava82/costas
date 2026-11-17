// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.App.Remote;

/// <summary>The answer to a sign-in.</summary>
/// <param name="Token">The device's new token, or null when refused.</param>
/// <param name="Username">The user's name as set in Settings.</param>
/// <param name="Wait">How long to wait before trying again, when refused for too many wrong passwords.</param>
public sealed record RemoteLogin(string? Token, string? Username, TimeSpan Wait)
{
    /// <summary>Wrong username or password.</summary>
    public static readonly RemoteLogin Refused = new(null, null, TimeSpan.Zero);

    /// <summary>Too many wrong passwords: wait.</summary>
    public static RemoteLogin TooMany(TimeSpan wait) => new(null, null, wait);
}
