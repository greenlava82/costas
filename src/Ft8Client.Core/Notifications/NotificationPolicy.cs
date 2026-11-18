// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Notifications;

/// <summary>The operator's choices for desktop notifications.</summary>
/// <param name="Enabled">Desktop notifications at all.</param>
/// <param name="Hunt">For Hunt list alerts.</param>
/// <param name="Callers">For a station calling me.</param>
/// <param name="OnlyInBackground">Only while Costas is minimized, in mini mode or not the active window.</param>
public sealed record NotificationPolicy(bool Enabled = true, bool Hunt = true, bool Callers = true, bool OnlyInBackground = true)
{
    /// <summary>True when events of <paramref name="kind"/> may notify.</summary>
    public bool Allows(AttentionKind kind) => Enabled && kind switch
    {
        AttentionKind.Hunt => Hunt,
        AttentionKind.Caller => Callers,
        _ => false,
    };
}
