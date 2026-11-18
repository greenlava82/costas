// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Notifications;
using Ft8Client.Core.Time;
using Serilog;

namespace Ft8Client.App.Services.Notifications;

/// <summary>
/// Desktop notifications for "needs you" events: the settings and <see cref="NotificationGate"/> decide, the backend
/// shows. Starts with no backend (tests, and a host with no window); the app picks one with <see cref="UseBackend"/>
/// before its first window opens. A notification only tells the operator: clicking it raises <see cref="Activated"/>,
/// which brings Costas forward and selects the station, and nothing else.
/// </summary>
public sealed class DesktopNotifications : IDisposable
{
    private readonly NotificationGate _gate = new();
    private readonly Func<NotificationPolicy> _policy;
    private readonly IClock _clock;
    private IDesktopNotifier _backend = new NullDesktopNotifier();

    /// <summary>Creates the service.</summary>
    /// <param name="policy">The operator's current choices (read on every event).</param>
    /// <param name="clock">Clock.</param>
    public DesktopNotifications(Func<NotificationPolicy> policy, IClock clock)
    {
        _policy = policy;
        _clock = clock;
    }

    /// <summary>True while Costas's main window is the active window, not minimized and not in mini mode. Set by the window.</summary>
    public Func<bool> InForeground { get; set; } = () => false;

    /// <summary>The backend in use.</summary>
    public IDesktopNotifier Backend => _backend;

    /// <summary>Raised, on any thread, with the station's call when the operator clicks a notification.</summary>
    public event Action<string>? Activated;

    /// <summary>The backend for this computer: Windows toasts, D-Bus on Linux, nothing elsewhere.</summary>
    /// <param name="baseDirectory">The app folder, which holds <c>costas.png</c>.</param>
    public static IDesktopNotifier PlatformBackend(string baseDirectory)
    {
        var icon = Path.Combine(baseDirectory, "costas.png");
        var iconPath = File.Exists(icon) ? icon : null;
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240)) return WindowsToastNotifier.Create(iconPath);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD()) return new FreedesktopNotifier(iconPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Desktop notifications are not available");
        }
        return new NullDesktopNotifier();
    }

    /// <summary>Uses <paramref name="backend"/> from now on.</summary>
    public void UseBackend(IDesktopNotifier backend)
    {
        _backend.Activated -= OnActivated;
        _backend = backend;
        _backend.Activated += OnActivated;
        Log.Information("Desktop notifications: {Backend}", backend.Name);
    }

    /// <summary>Offers an event; shows it when the settings, the foreground rule and the rate limit allow. UI thread.</summary>
    /// <returns>The notification shown, or null.</returns>
    public DesktopNotification? Raise(AttentionEvent e)
    {
        _gate.Policy = _policy();
        var n = _gate.Offer(e, InForeground(), _clock.UtcNow);
        if (n is not null) _backend.Show(n);
        return n;
    }

    private void OnActivated(string call) => Activated?.Invoke(call);

    /// <inheritdoc />
    public void Dispose() => _backend.Dispose();
}
