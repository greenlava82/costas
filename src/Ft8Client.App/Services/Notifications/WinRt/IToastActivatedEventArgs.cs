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

/// <summary><c>Windows.UI.Notifications.IToastActivatedEventArgs</c>.</summary>
[GeneratedComInterface]
[Guid("E3BF92F3-C197-436F-8265-0625824F8DAC")]
internal partial interface IToastActivatedEventArgs : IInspectable
{
    /// <summary>get_Arguments: the toast's launch string (an HSTRING).</summary>
    nint GetArguments();
}
