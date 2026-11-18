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

/// <summary>Activation of WinRT classes and the process's AppUserModelID.</summary>
internal static partial class WinRtApi
{
    /// <summary>The activation factory of <paramref name="className"/>, as <typeparamref name="T"/>.</summary>
    public static unsafe T Factory<T>(string className)
        where T : class
    {
        using var name = new HString(className);
        var iid = typeof(T).GUID;
        Marshal.ThrowExceptionForHR(RoGetActivationFactory(name.Handle, ref iid, out var p));
        try
        {
            return ComInterfaceMarshaller<T>.ConvertToManaged((void*)p) ?? throw new InvalidOperationException($"{className} has no {typeof(T).Name}");
        }
        finally
        {
            Marshal.Release(p);
        }
    }

    /// <summary>A new instance of <paramref name="className"/> (default constructor), as a raw IInspectable pointer the caller releases.</summary>
    public static nint Activate(string className)
    {
        using var name = new HString(className);
        Marshal.ThrowExceptionForHR(RoActivateInstance(name.Handle, out var p));
        return p;
    }

    /// <summary>The AppUserModelID set on this process (Velopack sets one at start), or null.</summary>
    public static string? ProcessAppUserModelId()
    {
        if (GetCurrentProcessExplicitAppUserModelID(out var p) < 0 || p == 0) return null;
        try
        {
            return Marshal.PtrToStringUni(p) is { Length: > 0 } id ? id : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(p);
        }
    }

    /// <summary>Sets this process's AppUserModelID (before any window is shown, so the taskbar groups it with its shortcut).</summary>
    public static void SetProcessAppUserModelId(string id) => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(id));

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint activatableClassId, ref Guid iid, out nint factory);

    [LibraryImport("combase.dll")]
    private static partial int RoActivateInstance(nint activatableClassId, out nint instance);

    [LibraryImport("shell32.dll")]
    private static partial int GetCurrentProcessExplicitAppUserModelID(out nint appId);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);
}
