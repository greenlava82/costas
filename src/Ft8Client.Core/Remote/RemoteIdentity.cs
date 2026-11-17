// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Remote;

/// <summary>
/// Who a remote-page connection is: the device, and the user it signed in as when sign-in is on. The user id is stable
/// across renames, so a later station profile per user can key on it.
/// </summary>
/// <param name="DeviceId">Stable id for the device's token; what the control lease holds.</param>
/// <param name="UserId">The signed-in user's id, or null when sign-in is off (a paired phone).</param>
/// <param name="Username">The signed-in user's name as shown, or null when sign-in is off.</param>
public sealed record RemoteIdentity(string DeviceId, string? UserId = null, string? Username = null)
{
    /// <summary>The device id for a token: the start of its SHA-256 hash, so the id cannot be turned back into the token.</summary>
    public static string DeviceIdFor(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))[..16];
}
