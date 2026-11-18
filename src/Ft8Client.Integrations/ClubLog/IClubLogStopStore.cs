// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Data.Settings;

namespace Ft8Client.Integrations.ClubLog;

/// <summary>Where a Club Log login stop is kept per station profile, so it outlives the upload service.</summary>
public interface IClubLogStopStore
{
    /// <summary>The profile's stop, or null when uploads are not stopped.</summary>
    ClubLogStop? Load(string profileId);

    /// <summary>Records a stop for the profile, or clears it with null.</summary>
    void Save(string profileId, ClubLogStop? stop);
}
