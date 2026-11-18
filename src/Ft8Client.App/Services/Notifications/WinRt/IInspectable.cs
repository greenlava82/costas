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

/// <summary>The WinRT base interface. Only its slots matter here: interfaces derived from it follow them in the vtable.</summary>
[GeneratedComInterface]
[Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90")]
internal partial interface IInspectable
{
    /// <summary>GetIids.</summary>
    void GetIids(out uint count, out nint iids);

    /// <summary>GetRuntimeClassName (an HSTRING).</summary>
    nint GetRuntimeClassName();

    /// <summary>GetTrustLevel.</summary>
    int GetTrustLevel();
}
