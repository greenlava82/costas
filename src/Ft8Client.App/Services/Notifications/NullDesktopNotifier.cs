// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Notifications;

namespace Ft8Client.App.Services.Notifications;

/// <summary>No notification centre (macOS for now, tests, or a backend that failed to start): shows nothing.</summary>
public sealed class NullDesktopNotifier : IDesktopNotifier
{
    /// <inheritdoc />
    public string Name => "None";

    /// <inheritdoc />
    public void Show(DesktopNotification n)
    {
    }

    /// <inheritdoc />
    public event Action<string>? Activated
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
