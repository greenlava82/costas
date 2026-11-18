// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Remote;

/// <summary>Works out a remote user's <see cref="RemoteProfileOffer"/> from the settings and the station's state.</summary>
public static class RemoteProfileOffers
{
    /// <summary>
    /// The profile <paramref name="userId"/> is linked to, beside the one in use, or null when the user is not linked to a
    /// profile or it has been deleted (they then operate as whichever profile is in use).
    /// </summary>
    public static RemoteProfileOffer? For(AppSettings s, string? userId, SessionSnapshot snapshot)
    {
        if (userId is null) return null;
        var linked = s.Remote.Users.FirstOrDefault(u => u.Id == userId)?.ProfileId;
        if (linked is null || s.Profiles.FirstOrDefault(p => p.Id == linked) is not { } mine) return null;
        var onMine = mine.Id == s.Profile.Id;
        return new RemoteProfileOffer(Label(mine), CallOf(mine), Label(s.Profile), onMine, onMine ? null : ProfileSwitch.Busy(snapshot));
    }

    /// <summary>"K1GLV (Home profile)", or the name alone for a profile with no callsign yet.</summary>
    public static string Label(StationProfile p) => p.Callsign.Length > 0 ? $"{p.Callsign} ({p.Name} profile)" : $"the {p.Name} profile";

    private static string CallOf(StationProfile p) => p.Callsign.Length > 0 ? p.Callsign : p.Name;
}
