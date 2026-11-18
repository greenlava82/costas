// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Notifications;
using Serilog;
using Tmds.DBus.Protocol;

namespace Ft8Client.App.Services.Notifications;

/// <summary>
/// Linux (and other freedesktop desktops): <c>org.freedesktop.Notifications</c> on the session bus, through
/// Tmds.DBus.Protocol, which Avalonia already uses. One notification per kind, replaced in place (<c>replaces_id</c>).
/// Only the <c>default</c> action is offered, which notification servers bind to a click on the notification body; there
/// are no buttons. Without a session bus or a notification server, nothing is shown.
/// </summary>
public sealed class FreedesktopNotifier : IDesktopNotifier
{
    private const string Service = "org.freedesktop.Notifications";
    private const string ObjectPath = "/org/freedesktop/Notifications";
    private const string Interface = "org.freedesktop.Notifications";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<AttentionKind, uint> _ids = [];
    private readonly Dictionary<uint, string> _callById = [];
    private readonly string _icon;
    private DBusConnection? _connection;
    private bool _failed;
    private bool _disposed;

    /// <summary>Creates the notifier. Nothing connects until the first notification.</summary>
    /// <param name="iconPath">A PNG to show, or null for the <c>costas</c> theme icon.</param>
    public FreedesktopNotifier(string? iconPath) => _icon = iconPath ?? "costas";

    /// <inheritdoc />
    public string Name => "Desktop notifications (D-Bus)";

    /// <inheritdoc />
    public event Action<string>? Activated;

    /// <inheritdoc />
    public void Show(DesktopNotification n) => _ = ShowAsync(n);

    private async Task ShowAsync(DesktopNotification n)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || _failed) return;
            var connection = await ConnectAsync().ConfigureAwait(false);
            if (connection is null) return;
            var replaces = _ids.GetValueOrDefault(n.Kind);
            var id = await connection.CallMethodAsync(Notify(connection, n, replaces), (m, _) => m.GetBodyReader().ReadUInt32(), null)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            _ids[n.Kind] = id;
            lock (_callById)
            {
                if (replaces != 0) _callById.Remove(replaces);
                _callById[id] = n.Call;
            }
        }
        catch (Exception ex)
        {
            // No notification server (a bare window manager) or a broken bus: say so once and stop trying.
            _failed = true;
            Log.Warning(ex, "Desktop notifications are not available: org.freedesktop.Notifications did not answer");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DBusConnection?> ConnectAsync()
    {
        if (_connection is not null) return _connection;
        if (DBusAddress.Session is not { Length: > 0 } address)
        {
            _failed = true;
            Log.Information("Desktop notifications off: no D-Bus session bus");
            return null;
        }
        var connection = new DBusConnection(address);
        await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await connection.WatchSignalAsync(Service, ObjectPath, Interface, "ActionInvoked",
            (m, _) =>
            {
                var r = m.GetBodyReader();
                return (Id: r.ReadUInt32(), Action: r.ReadString());
            },
            n =>
            {
                if (!n.HasValue || n.Value.Action != "default") return;
                string? call;
                lock (_callById) call = _callById.GetValueOrDefault(n.Value.Id);
                if (call is not null) Activated?.Invoke(call);
            }, ObserverFlags.None, emitOnCapturedContext: false).ConfigureAwait(false);
        _connection = connection;
        return connection;
    }

    // Notify(app_name s, replaces_id u, app_icon s, summary s, body s, actions as, hints a{sv}, expire_timeout i) -> id u
    private MessageBuffer Notify(DBusConnection connection, DesktopNotification n, uint replaces)
    {
        using var w = connection.GetMessageWriter();
        w.WriteMethodCallHeader(destination: Service, path: ObjectPath, @interface: Interface, member: "Notify", signature: "susssasa{sv}i");
        w.WriteString("Costas");
        w.WriteUInt32(replaces);
        w.WriteString(_icon);
        w.WriteString(n.Title);
        w.WriteString(Escape(n.Body));
        w.WriteArray(new[] { "default", "Show in Costas" });
        var hints = w.WriteDictionaryStart();
        w.WriteDictionaryEntryStart();
        w.WriteString("desktop-entry");
        w.WriteVariantString("costas");
        w.WriteDictionaryEntryStart();
        w.WriteString("category");
        w.WriteVariantString("im.received");
        // Costas plays its own chime when the operator wants one.
        w.WriteDictionaryEntryStart();
        w.WriteString("suppress-sound");
        w.WriteVariantBool(true);
        w.WriteDictionaryEntryStart();
        w.WriteString("urgency");
        w.WriteVariantByte(1);
        w.WriteDictionaryEnd(hints);
        w.WriteInt32(-1);
        return w.CreateMessage();
    }

    // The body may carry simple markup on some servers: show < and & as typed.
    private static string Escape(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _connection?.Dispose();
    }
}
