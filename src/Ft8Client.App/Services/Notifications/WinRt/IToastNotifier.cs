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

/// <summary><c>Windows.UI.Notifications.IToastNotifier</c>, first two methods.</summary>
[GeneratedComInterface]
[Guid("75927B93-03F3-41EC-91D3-6E5BAC1B38E7")]
internal partial interface IToastNotifier : IInspectable
{
    /// <summary>Show.</summary>
    void Show(IToastNotification notification);

    /// <summary>Hide.</summary>
    void Hide(IToastNotification notification);
}
