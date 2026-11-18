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

/// <summary><c>Windows.UI.Notifications.IToastNotification</c>, up to remove_Activated.</summary>
[GeneratedComInterface]
[Guid("997E2675-059E-4E60-8B06-1760917C8B80")]
internal partial interface IToastNotification : IInspectable
{
    /// <summary>get_Content.</summary>
    nint GetContent();

    /// <summary>put_ExpirationTime (an IReference of DateTime).</summary>
    void SetExpirationTime(nint value);

    /// <summary>get_ExpirationTime.</summary>
    nint GetExpirationTime();

    /// <summary>add_Dismissed.</summary>
    long AddDismissed(nint handler);

    /// <summary>remove_Dismissed.</summary>
    void RemoveDismissed(long token);

    /// <summary>add_Activated: the handler is a <see cref="IToastActivatedHandler"/>.</summary>
    long AddActivated(nint handler);

    /// <summary>remove_Activated.</summary>
    void RemoveActivated(long token);
}
