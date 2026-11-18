// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Diagnostics;

namespace Ft8Client.App.Services;

/// <summary>Opens a folder in the system's file manager (Explorer, Finder, or the desktop's on Linux).</summary>
public static class FolderOpener
{
    /// <summary>Creates the folder if it is missing and opens it. Returns false when it could not be opened.</summary>
    public static bool Open(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var exe = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            start.ArgumentList.Add(folder);
            using var _ = Process.Start(start);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "Could not open {Folder}", folder);
            return false;
        }
    }
}
