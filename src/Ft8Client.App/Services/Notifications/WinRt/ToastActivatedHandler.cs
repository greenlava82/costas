// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Runtime.InteropServices.Marshalling;

namespace Ft8Client.App.Services.Notifications.WinRt;

/// <summary>The Activated handler handed to a toast: passes the toast's launch string on.</summary>
[GeneratedComClass]
internal sealed partial class ToastActivatedHandler(Action<string> activated) : IToastActivatedHandler
{
    /// <inheritdoc />
    public unsafe void Invoke(nint sender, nint args)
    {
        // Runs on a Windows thread: never let an exception escape into the notification platform.
        try
        {
            if (args == 0) return;
            var e = ComInterfaceMarshaller<IToastActivatedEventArgs>.ConvertToManaged((void*)args);
            if (e is null) return;
            var h = e.GetArguments();
            try
            {
                activated(HString.Read(h));
            }
            finally
            {
                HString.Delete(h);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not read a clicked notification");
        }
    }
}
