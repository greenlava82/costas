// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Runtime.InteropServices;

namespace Ft8Client.App.Services.Notifications.WinRt;

/// <summary>A WinRT string (HSTRING), created and freed with combase.</summary>
internal sealed partial class HString : IDisposable
{
    /// <summary>Creates the HSTRING for <paramref name="s"/>.</summary>
    public HString(string s) => Marshal.ThrowExceptionForHR(WindowsCreateString(s, (uint)s.Length, out _handle));

    private readonly nint _handle;

    /// <summary>The HSTRING to pass to WinRT. Zero is the empty string.</summary>
    public nint Handle => _handle;

    /// <summary>The text of an HSTRING WinRT returned.</summary>
    public static unsafe string Read(nint h)
    {
        if (h == 0) return string.Empty;
        var p = WindowsGetStringRawBuffer(h, out var length);
        return new string((char*)p, 0, (int)length);
    }

    /// <summary>Frees an HSTRING WinRT returned.</summary>
    public static void Delete(nint h)
    {
        if (h != 0) _ = WindowsDeleteString(h);
    }

    /// <inheritdoc />
    public void Dispose() => Delete(_handle);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string source, uint length, out nint hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint hstring);

    [LibraryImport("combase.dll")]
    private static partial nint WindowsGetStringRawBuffer(nint hstring, out uint length);
}
