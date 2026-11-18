// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Notifications;

namespace Ft8Client.App.Services.Notifications;

/// <summary>
/// The operating system's notification centre: Windows toasts or <c>org.freedesktop.Notifications</c>. A backend that
/// cannot show notifications here does nothing. Notifications carry no action buttons: clicking one only brings Costas
/// forward with the station selected.
/// </summary>
public interface IDesktopNotifier : IDisposable
{
    /// <summary>"Windows notifications", "Desktop notifications (D-Bus)" or "None", for Diagnostics and the log.</summary>
    string Name { get; }

    /// <summary>
    /// Shows <paramref name="n"/>, replacing the earlier notification of the same kind. Returns at once: the work runs
    /// in the background, so it never holds up the UI or slot threads. Failures are logged, never thrown.
    /// </summary>
    void Show(DesktopNotification n);

    /// <summary>Raised, on any thread, with the station's call when the operator clicks a notification.</summary>
    event Action<string>? Activated;
}
