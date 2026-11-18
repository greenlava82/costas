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

/// <summary><c>Windows.UI.Notifications.IToastNotification2</c>: tag, group and whether it pops up.</summary>
[GeneratedComInterface]
[Guid("9DFB9FD1-143A-490E-90BF-B9FBA7132DE7")]
internal partial interface IToastNotification2 : IInspectable
{
    /// <summary>put_Tag (an HSTRING).</summary>
    void SetTag(nint value);

    /// <summary>get_Tag.</summary>
    nint GetTag();

    /// <summary>put_Group (an HSTRING).</summary>
    void SetGroup(nint value);

    /// <summary>get_Group.</summary>
    nint GetGroup();

    /// <summary>put_SuppressPopup (a WinRT boolean: one byte).</summary>
    void SetSuppressPopup(byte value);
}
