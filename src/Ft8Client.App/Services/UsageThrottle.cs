// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
namespace Ft8Client.App.Services;

/// <summary>
/// Lets an event through once per key in a time window, so a repeated failure (a refused transmission every slot, a
/// flapping service) counts once instead of filling the statistics.
/// </summary>
public sealed class UsageThrottle(TimeSpan window)
{
    private readonly Dictionary<string, DateTime> _last = new(StringComparer.Ordinal);

    /// <summary>True when <paramref name="key"/> was not let through in the last window.</summary>
    public bool Allow(string key, DateTime utcNow)
    {
        lock (_last)
        {
            if (_last.TryGetValue(key, out var t) && utcNow - t < window) return false;
            _last[key] = utcNow;
            return true;
        }
    }
}
