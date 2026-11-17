// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Remote;

/// <summary>
/// Slows down password guessing on the remote page. Failures are counted per username and per client address; after
/// <see cref="FreeFailures"/> of either, sign-in is refused for <see cref="FirstLockout"/>, doubling with each further
/// failure up to <see cref="MaxLockout"/>. A successful sign-in clears that username and address.
/// </summary>
public sealed class LoginThrottle
{
    /// <summary>Failures allowed before the first wait.</summary>
    public const int FreeFailures = 5;

    /// <summary>The first wait.</summary>
    public static readonly TimeSpan FirstLockout = TimeSpan.FromSeconds(30);

    /// <summary>The longest wait.</summary>
    public static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(15);

    // More keys than this and the oldest are forgotten, so a flood of made-up usernames cannot grow memory without end.
    private const int MaxKeys = 4096;

    private readonly object _gate = new();
    private readonly Dictionary<string, (int Failures, DateTime Until, DateTime Last)> _keys = new(StringComparer.Ordinal);

    /// <summary>How long until <paramref name="username"/> from <paramref name="address"/> may try again; zero when now.</summary>
    public TimeSpan Wait(string username, string address, DateTime nowUtc)
    {
        lock (_gate)
        {
            var wait = TimeSpan.Zero;
            foreach (var k in Keys(username, address))
            {
                if (_keys.TryGetValue(k, out var e) && e.Until > nowUtc && e.Until - nowUtc > wait) wait = e.Until - nowUtc;
            }
            return wait;
        }
    }

    /// <summary>Counts a wrong username or password.</summary>
    public void Failed(string username, string address, DateTime nowUtc)
    {
        lock (_gate)
        {
            foreach (var k in Keys(username, address))
            {
                var failures = (_keys.TryGetValue(k, out var e) ? e.Failures : 0) + 1;
                var until = failures <= FreeFailures ? DateTime.MinValue
                    : nowUtc + TimeSpan.FromTicks(Math.Min(MaxLockout.Ticks, FirstLockout.Ticks << Math.Min(20, failures - FreeFailures - 1)));
                _keys[k] = (failures, until, nowUtc);
            }
            if (_keys.Count > MaxKeys)
            {
                foreach (var k in _keys.OrderBy(p => p.Value.Last).Take(_keys.Count - MaxKeys).Select(p => p.Key).ToList()) _keys.Remove(k);
            }
        }
    }

    /// <summary>Clears the count after a successful sign-in.</summary>
    public void Succeeded(string username, string address)
    {
        lock (_gate)
        {
            foreach (var k in Keys(username, address)) _keys.Remove(k);
        }
    }

    private static string[] Keys(string username, string address) =>
        ["u:" + username.Trim().ToUpperInvariant(), "a:" + address];
}
