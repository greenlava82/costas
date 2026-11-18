// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.App.Remote;

/// <summary>
/// Recent "someone else is here" notices for the people already using the station (owner request 2026-11-17): a user
/// signed in, or someone took control. Each phone is told about the others, not about itself, and the desk about
/// the phones. They only inform; control still goes to whoever sends a command.
/// </summary>
public sealed class RemoteNotices
{
    /// <summary>How long a notice stays on the phones.</summary>
    public static readonly TimeSpan PhoneLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long a notice stays on the desk's remote indicator.</summary>
    public static readonly TimeSpan DeskLifetime = TimeSpan.FromMinutes(1);

    private const int Kept = 5;
    private readonly object _gate = new();
    private readonly List<Notice> _notices = [];
    // Starts from the time, so ids never repeat across restarts: a phone remembers the ids it dismissed.
    private long _next = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Adds a notice. <paramref name="aboutDevice"/> is the device it is about (not told), or null when the desk did it
    /// (the desk is then not told).
    /// </summary>
    public void Add(string text, string? aboutDevice, DateTime nowUtc)
    {
        lock (_gate)
        {
            _notices.Add(new Notice(++_next, text, aboutDevice, nowUtc));
            if (_notices.Count > Kept) _notices.RemoveAt(0);
        }
    }

    /// <summary>What <paramref name="deviceId"/> should see: recent notices about anyone else, newest first.</summary>
    public IReadOnlyList<RemoteNotice> For(string deviceId, DateTime nowUtc)
    {
        lock (_gate)
        {
            return _notices
                .Where(n => !string.Equals(n.AboutDevice, deviceId, StringComparison.Ordinal) && nowUtc - n.TimeUtc < PhoneLifetime)
                .OrderByDescending(n => n.Id)
                .Select(n => new RemoteNotice(n.Id, n.Text, n.TimeUtc.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC"))
                .ToList();
        }
    }

    /// <summary>The newest notice about a phone from the last minute, for the desk; null when there is none.</summary>
    public string? ForDesk(DateTime nowUtc)
    {
        lock (_gate)
        {
            return _notices.LastOrDefault(n => n.AboutDevice is not null && nowUtc - n.TimeUtc < DeskLifetime)?.Text;
        }
    }

    private sealed record Notice(long Id, string Text, string? AboutDevice, DateTime TimeUtc);
}
