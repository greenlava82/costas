// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using System.Security;
using Ft8Client.App.Services.Notifications.WinRt;
using Ft8Client.Core.Notifications;
using Microsoft.Win32;
using Serilog;

namespace Ft8Client.App.Services.Notifications;

/// <summary>
/// Windows toast notifications, called directly through WinRT (<c>Windows.UI.Notifications</c>) with .NET's
/// source-generated COM interop, so Costas keeps its plain <c>net10.0</c> target and needs no Windows SDK projection or
/// Windows App SDK runtime.
/// <para>
/// An unpackaged app needs an AppUserModelID that Windows knows. A Velopack install sets one on the process at start
/// (<c>velopack.&lt;id&gt;</c>, the same as its Start menu shortcut), and that one is used. Otherwise (a classic Inno
/// Setup install, or a development run) Costas uses <see cref="ClassicAppId"/>, which the classic installer puts on its
/// shortcut, sets it on the process, and registers its name and icon under
/// <c>HKCU\Software\Classes\AppUserModelId</c> so toasts show "Costas" even with no shortcut.
/// </para>
/// <para>
/// Clicking a toast raises its Activated event while Costas runs. No COM activator is registered, so a toast clicked
/// from the notification centre after Costas has closed does nothing. Toasts are silent (Costas has its own chime) and
/// use the default scenario, so Focus Assist / Do Not Disturb holds them back as it does any app's.
/// </para>
/// </summary>
[SupportedOSPlatform("windows10.0.10240")]
public sealed class WindowsToastNotifier : IDesktopNotifier
{
    /// <summary>The AppUserModelID of a classic install's shortcut and of a development run.</summary>
    public const string ClassicAppId = "Costas.Costas";

    private const string Group = "costas";

    private readonly object _gate = new();
    private readonly string _appId;
    private readonly Dictionary<AttentionKind, (IToastNotification Toast, long Token)> _shown = [];
    private readonly ToastActivatedHandler _handler;
    private IToastNotifier? _notifier;
    private bool _failed;

    private WindowsToastNotifier(string appId)
    {
        _appId = appId;
        _handler = new ToastActivatedHandler(OnActivated);
    }

    /// <inheritdoc />
    public string Name => $"Windows notifications ({_appId})";

    /// <inheritdoc />
    public event Action<string>? Activated;

    /// <summary>
    /// Settles the AppUserModelID and returns the notifier. Call before the first window is shown.
    /// </summary>
    /// <param name="iconPath">The PNG toasts show, for the registration of <see cref="ClassicAppId"/>.</param>
    public static WindowsToastNotifier Create(string? iconPath)
    {
        var id = WinRtApi.ProcessAppUserModelId();
        if (id is null)
        {
            id = ClassicAppId;
            try
            {
                WinRtApi.SetProcessAppUserModelId(id);
                Register(id, iconPath);
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or SecurityException or IOException)
            {
                Log.Warning(ex, "Could not register Costas's AppUserModelID for notifications");
            }
        }
        return new WindowsToastNotifier(id);
    }

    // What a shortcut would otherwise tell Windows: the name and icon toasts show. Per user, no admin needed.
    private static void Register(string id, string? iconPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{id}");
        key.SetValue("DisplayName", "Costas");
        if (iconPath is not null) key.SetValue("IconUri", iconPath);
    }

    /// <inheritdoc />
    public void Show(DesktopNotification n) => _ = Task.Run(() => ShowNow(n));

    private void ShowNow(DesktopNotification n)
    {
        lock (_gate)
        {
            if (_failed) return;
            try
            {
                if (_notifier is null)
                {
                    using var id = new HString(_appId);
                    _notifier = WinRtApi.Factory<IToastNotificationManagerStatics>("Windows.UI.Notifications.ToastNotificationManager")
                        .CreateToastNotifierWithId(id.Handle);
                }
                var toast = CreateToast(ToastXml.For(n));
                var two = (IToastNotification2)toast;
                using (var tag = new HString(Tag(n.Kind))) two.SetTag(tag.Handle);
                using (var group = new HString(Group)) two.SetGroup(group.Handle);
                two.SetSuppressPopup(n.Popup ? (byte)0 : (byte)1);
                var token = AddHandler(toast);
                // The same tag and group replace the earlier toast in the notification centre.
                if (_shown.Remove(n.Kind, out var old)) old.Toast.RemoveActivated(old.Token);
                _shown[n.Kind] = (toast, token);
                _notifier.Show(toast);
            }
            catch (Exception ex)
            {
                // Toasts turned off for Costas, or an unregistered AppUserModelID: say so once and stop trying.
                _failed = true;
                Log.Warning(ex, "Windows notifications are not available ({AppId})", _appId);
            }
        }
    }

    private static unsafe IToastNotification CreateToast(string xml)
    {
        var doc = WinRtApi.Activate("Windows.Data.Xml.Dom.XmlDocument");
        try
        {
            var io = ComInterfaceMarshaller<IXmlDocumentIO>.ConvertToManaged((void*)doc)!;
            using (var text = new HString(xml)) io.LoadXml(text.Handle);
            // CreateToastNotification takes the IXmlDocument interface, not the IInspectable RoActivateInstance returned.
            var iid = new Guid("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(doc, in iid, out var xmlDocument));
            try
            {
                return WinRtApi.Factory<IToastNotificationFactory>("Windows.UI.Notifications.ToastNotification").CreateToastNotification(xmlDocument);
            }
            finally
            {
                Marshal.Release(xmlDocument);
            }
        }
        finally
        {
            Marshal.Release(doc);
        }
    }

    // add_Activated takes its own reference to the handler, so ours is released straight after the call.
    private unsafe long AddHandler(IToastNotification toast)
    {
        var p = (nint)ComInterfaceMarshaller<IToastActivatedHandler>.ConvertToUnmanaged(_handler);
        try
        {
            return toast.AddActivated(p);
        }
        finally
        {
            Marshal.Release(p);
        }
    }

    private void OnActivated(string arguments)
    {
        if (ToastXml.CallOf(arguments) is { } call) Activated?.Invoke(call);
    }

    private static string Tag(AttentionKind kind) => kind == AttentionKind.Hunt ? "hunt" : "caller";

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var (toast, token) in _shown.Values)
            {
                try
                {
                    toast.RemoveActivated(token);
                }
                catch (COMException)
                {
                    // The toast is gone already.
                }
            }
            _shown.Clear();
        }
    }
}
