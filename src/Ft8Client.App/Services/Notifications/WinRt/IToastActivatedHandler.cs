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

/// <summary>
/// <c>TypedEventHandler&lt;ToastNotification, Object&gt;</c>, the Activated event's delegate. WinRT delegates derive
/// from IUnknown. The IID is the parameterized one WinRT derives from the type signature
/// (<c>pinterface({9de1c534-6ae1-11e0-84e1-18a905bcc53f};rc(Windows.UI.Notifications.ToastNotification;{997e2675-059e-4e60-8b06-1760917c8b80});cinterface(IInspectable))</c>),
/// computed with the WinRT UUIDv5 rule and checked against the Windows SDK value.
/// </summary>
[GeneratedComInterface]
[Guid("AB54DE2D-97D9-5528-B6AD-105AFE156530")]
internal partial interface IToastActivatedHandler
{
    /// <summary>Invoke(sender, args).</summary>
    void Invoke(nint sender, nint args);
}
