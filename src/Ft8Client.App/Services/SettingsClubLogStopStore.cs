// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Data.Settings;
using Ft8Client.Integrations.ClubLog;

namespace Ft8Client.App.Services;

/// <summary>Keeps Club Log login stops in the station profiles' settings, so they survive a restart.</summary>
public sealed class SettingsClubLogStopStore(AppSettingsAccessor settings) : IClubLogStopStore
{
    /// <inheritdoc />
    public ClubLogStop? Load(string profileId) => settings.Current.Profiles.FirstOrDefault(p => p.Id == profileId)?.ClubLog.Stop;

    /// <inheritdoc />
    public void Save(string profileId, ClubLogStop? stop)
    {
        if (Load(profileId) is null && stop is null) return;
        settings.Update(s =>
        {
            if (s.Profiles.FirstOrDefault(p => p.Id == profileId) is { } p) p.ClubLog.Stop = stop;
        });
    }
}
