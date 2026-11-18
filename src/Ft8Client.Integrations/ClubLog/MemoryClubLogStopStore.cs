// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Collections.Concurrent;
using Ft8Client.Data.Settings;

namespace Ft8Client.Integrations.ClubLog;

/// <summary>A stop store that lasts as long as the object: for tests and for callers that persist nothing.</summary>
public sealed class MemoryClubLogStopStore : IClubLogStopStore
{
    private readonly ConcurrentDictionary<string, ClubLogStop> _stops = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ClubLogStop? Load(string profileId) => _stops.TryGetValue(profileId, out var s) ? s : null;

    /// <inheritdoc />
    public void Save(string profileId, ClubLogStop? stop)
    {
        if (stop is null) _stops.TryRemove(profileId, out _);
        else _stops[profileId] = stop;
    }
}
