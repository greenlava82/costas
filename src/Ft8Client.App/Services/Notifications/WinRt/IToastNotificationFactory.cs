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

/// <summary><c>Windows.UI.Notifications.IToastNotificationFactory</c>.</summary>
[GeneratedComInterface]
[Guid("04124B20-82C6-4229-B109-FD9ED4662B53")]
internal partial interface IToastNotificationFactory : IInspectable
{
    /// <summary>CreateToastNotification from an <c>IXmlDocument</c> pointer.</summary>
    IToastNotification CreateToastNotification(nint content);
}
