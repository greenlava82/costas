// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Notifications;

/// <summary>
/// Decides which "needs you" events become desktop notifications, so a busy band does not flood the notification
/// centre:
/// <list type="bullet">
/// <item>Nothing when notifications, or that kind of event, are off; nothing while Costas is in the foreground when
/// the operator asked for background only.</item>
/// <item>A station already notified for the same kind in the last <see cref="RepeatQuiet"/> is not notified again.</item>
/// <item>At most one notification of each kind pops up per <see cref="MergeWindow"/>. Further events of that kind in
/// the window are merged into it ("3 Hunt list stations heard"), which is replaced quietly, without popping up again.</item>
/// </list>
/// Not thread-safe: call from one thread (the UI thread).
/// </summary>
public sealed class NotificationGate
{
    /// <summary>How long after a notification pops up further events of its kind are merged into it.</summary>
    public static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(60);

    /// <summary>How long a station stays quiet after it was notified (per kind).</summary>
    public static readonly TimeSpan RepeatQuiet = TimeSpan.FromMinutes(10);

    /// <summary>How many stations a merged notification names before "and N more".</summary>
    public const int MaxNamed = 4;

    private readonly Dictionary<(AttentionKind, string), DateTime> _notified = [];
    private readonly Dictionary<AttentionKind, (DateTime StartedUtc, List<AttentionEvent> Items)> _groups = [];

    /// <summary>The operator's choices. Read on every event, so a change applies at once.</summary>
    public NotificationPolicy Policy { get; set; } = new();

    /// <summary>
    /// Takes an event; returns the notification to show, or null when it should not notify.
    /// </summary>
    /// <param name="e">The event.</param>
    /// <param name="inForeground">Costas's main window is the active window, not minimized and not in mini mode.</param>
    /// <param name="nowUtc">The time.</param>
    public DesktopNotification? Offer(AttentionEvent e, bool inForeground, DateTime nowUtc)
    {
        if (!Policy.Allows(e.Kind) || (Policy.OnlyInBackground && inForeground)) return null;
        var key = (e.Kind, e.Call);
        if (_notified.TryGetValue(key, out var last) && nowUtc - last < RepeatQuiet) return null;
        _notified[key] = nowUtc;
        if (_notified.Count > 500) Prune(nowUtc);

        if (_groups.TryGetValue(e.Kind, out var g) && nowUtc - g.StartedUtc < MergeWindow)
        {
            g.Items.Add(e);
            return Merged(e.Kind, g.Items);
        }
        _groups[e.Kind] = (nowUtc, [e]);
        return new DesktopNotification(e.Kind, e.Title, e.Body, e.Call, Popup: true);
    }

    private static DesktopNotification Merged(AttentionKind kind, List<AttentionEvent> items)
    {
        var n = items.Count;
        var title = kind == AttentionKind.Hunt ? $"{n} Hunt list stations heard" : $"{n} stations are calling you";
        var named = items.Take(MaxNamed).Select(x => x.Short).ToList();
        var body = string.Join(", ", named) + (n > MaxNamed ? $" and {n - MaxNamed} more." : ".");
        return new DesktopNotification(kind, title, body, items[^1].Call, Popup: false);
    }

    private void Prune(DateTime nowUtc)
    {
        foreach (var k in _notified.Where(kv => nowUtc - kv.Value >= RepeatQuiet).Select(kv => kv.Key).ToList()) _notified.Remove(k);
    }
}
