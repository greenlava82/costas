// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Ft8Client.App.Services.Notifications.WinRt;

/// <summary><c>Windows.UI.Notifications.IToastNotificationManagerStatics</c> (windows.ui.notifications.h), first two methods.</summary>
[GeneratedComInterface]
[Guid("50AC103F-D235-4598-BBEF-98FE4D1A3AD4")]
internal partial interface IToastNotificationManagerStatics : IInspectable
{
    /// <summary>CreateToastNotifier, for packaged apps. Not used.</summary>
    nint CreateToastNotifier();

    /// <summary>CreateToastNotifierWithId: a notifier for an AppUserModelID (an HSTRING).</summary>
    IToastNotifier CreateToastNotifierWithId(nint applicationId);
}
