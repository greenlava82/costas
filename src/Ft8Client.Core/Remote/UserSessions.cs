// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using System.Security.Cryptography;

namespace Ft8Client.Core.Remote;

/// <summary>
/// The devices one remote-page user is signed in on. Each holds a random token; only its SHA-256 hash is kept, with the
/// day it was last used, so a device unused for <see cref="Lifetime"/> must sign in again. Serialized to one line
/// (<c>hash:day,hash:day</c>) for the OS credential store.
/// </summary>
public sealed class UserSessions
{
    /// <summary>Most devices one user stays signed in on; signing in on another forgets the least recently used.</summary>
    public const int MaxDevices = 16;

    /// <summary>A sign-in ends after this long without use.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private readonly object _gate = new();
    private readonly List<(string Hash, long Day)> _entries = [];

    /// <summary>Number of devices signed in, expired ones included until the next <see cref="Prune"/>.</summary>
    public int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    /// <summary>Loads what <see cref="Serialize"/> saved; malformed entries are dropped.</summary>
    public static UserSessions Parse(string? saved)
    {
        var s = new UserSessions();
        foreach (var item in (saved ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = item.IndexOf(':', StringComparison.Ordinal);
            if (colon != 64) continue;
            var hash = item[..64];
            if (!hash.All(Uri.IsHexDigit)) continue;
            if (!long.TryParse(item.AsSpan(65), NumberStyles.None, CultureInfo.InvariantCulture, out var day)) continue;
            s._entries.Add((hash.ToUpperInvariant(), day));
        }
        if (s._entries.Count > MaxDevices) s._entries.RemoveRange(0, s._entries.Count - MaxDevices);
        return s;
    }

    /// <summary>The entries, comma separated.</summary>
    public string Serialize()
    {
        lock (_gate) return string.Join(',', _entries.Select(e => e.Hash + ":" + e.Day.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>A new token for a device that just signed in. It is shown to the device once.</summary>
    public string Issue(DateTime nowUtc)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_gate)
        {
            _entries.Add((Hash(token), Day(nowUtc)));
            if (_entries.Count > MaxDevices)
            {
                // The least recently used device goes first.
                var oldest = _entries.Select((e, i) => (e.Day, i)).Min();
                _entries.RemoveAt(oldest.i);
            }
        }
        return token;
    }

    /// <summary>True when <paramref name="token"/> is signed in and was used within <see cref="Lifetime"/>.</summary>
    public bool Verify(string? token, DateTime nowUtc) => Find(token, nowUtc) >= 0;

    /// <summary>
    /// Marks <paramref name="token"/> as used today. True when that changed what <see cref="Serialize"/> gives, so the
    /// caller saves at most once a day per device.
    /// </summary>
    public bool Touch(string? token, DateTime nowUtc)
    {
        lock (_gate)
        {
            var i = Find(token, nowUtc);
            if (i < 0 || _entries[i].Day == Day(nowUtc)) return false;
            _entries[i] = (_entries[i].Hash, Day(nowUtc));
            return true;
        }
    }

    /// <summary>Signs one device out. True when it was signed in.</summary>
    public bool Remove(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return false;
        var h = Hash(token);
        lock (_gate) return _entries.RemoveAll(e => e.Hash == h) > 0;
    }

    /// <summary>Forgets expired sign-ins. True when any were forgotten.</summary>
    public bool Prune(DateTime nowUtc)
    {
        lock (_gate) return _entries.RemoveAll(e => Expired(e.Day, nowUtc)) > 0;
    }

    /// <summary>Signs every device out.</summary>
    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    private int Find(string? token, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return -1;
        var h = System.Text.Encoding.ASCII.GetBytes(Hash(token));
        lock (_gate)
        {
            var found = -1;
            for (var i = 0; i < _entries.Count; i++)
            {
                if (CryptographicOperations.FixedTimeEquals(h, System.Text.Encoding.ASCII.GetBytes(_entries[i].Hash)) && !Expired(_entries[i].Day, nowUtc)) found = i;
            }
            return found;
        }
    }

    private static bool Expired(long day, DateTime nowUtc) => Day(nowUtc) - day >= (long)Lifetime.TotalDays;

    private static long Day(DateTime utc) => utc.Ticks / TimeSpan.TicksPerDay;

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
