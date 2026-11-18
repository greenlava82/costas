// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Settings;

/// <summary>Desktop notifications. They only tell the operator; clicking one never calls, transmits or changes band.</summary>
public sealed class NotificationOptions
{
    /// <summary>Show desktop notifications at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>For Hunt list alerts.</summary>
    public bool Hunt { get; set; } = true;

    /// <summary>For a station calling me.</summary>
    public bool Callers { get; set; } = true;

    /// <summary>Only while Costas is minimized, in mini mode or not the active window.</summary>
    public bool OnlyInBackground { get; set; } = true;
}
