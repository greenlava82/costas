// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Data.Settings;

namespace Ft8Client.App.Remote;

/// <summary>
/// The line that tells a signed-in user whose profile they will operate as. Signing in does not switch the station
/// profile; their command that takes control does (D25), so the phone says so before it happens (owner request
/// 2026-11-18).
/// </summary>
public static class RemoteProfileNote
{
    /// <summary>
    /// "You will operate as K1GLV (Home profile) when you take control.", or null when the user is not linked to a
    /// profile, the profile is gone, or it is already the one in use.
    /// </summary>
    public static string? For(AppSettings s, string? userId)
    {
        if (userId is null) return null;
        var linked = s.Remote.Users.FirstOrDefault(u => u.Id == userId)?.ProfileId;
        if (linked is null || linked == s.ActiveProfile || s.Profiles.FirstOrDefault(p => p.Id == linked) is not { } p) return null;
        var call = p.Callsign.Length > 0 ? p.Callsign : "your callsign";
        return $"You will operate as {call} ({p.Name} profile) when you take control, if the station is idle.";
    }
}
