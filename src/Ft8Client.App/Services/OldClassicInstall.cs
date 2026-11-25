// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Serilog;

namespace Ft8Client.App.Services;

/// <summary>
/// An old classic (Inno Setup) Costas installed for all users, still there beside the Velopack install. Its Start menu and
/// desktop shortcuts open the old version, which then "moves to Velopack" again: it downloads a full installer, reinstalls
/// Costas with no update splash, and puts a station that follows development builds back on releases. Removing it needs
/// Windows' administrator prompt, so Costas only offers it (<see cref="ClassicInstallCleanup"/> removes a per-user copy by
/// itself).
/// </summary>
/// <param name="Version">The old copy's version, as Apps &amp; features shows it ("" when unknown).</param>
/// <param name="Folder">Where it is installed, usually C:\Program Files\Costas.</param>
/// <param name="Uninstaller">Its uninstaller.</param>
public sealed record OldClassicInstall(string Version, string Folder, string Uninstaller)
{
    // The classic installer's AppId (installer/Costas.iss); Inno Setup adds "_is1".
    internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{ED0829A3-63BC-4E6C-99CB-5DFF4BB4A021}_is1";

    // ERROR_CANCELLED: the operator said No to the administrator prompt.
    private const int Cancelled = 1223;

    /// <summary>The all-users classic install, or null when there is none (or this is not Windows).</summary>
    public static OldClassicInstall? Find()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return Read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Information("Could not look for an old all-users install: {Error}", ex.Message);
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static OldClassicInstall? Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
        if (key?.GetValue("UninstallString") is not string uninstall) return null;
        var exe = uninstall.Trim().Trim('"');
        if (!File.Exists(exe)) return null;
        var folder = key.GetValue("InstallLocation") as string ?? Path.GetDirectoryName(exe) ?? string.Empty;
        return new OldClassicInstall(key.GetValue("DisplayVersion") as string ?? string.Empty, folder.TrimEnd('\\'), exe);
    }

    /// <summary>
    /// Runs the old copy's uninstaller with Windows' administrator prompt and waits (up to two minutes) until it is gone.
    /// Returns null when it was removed, or what went wrong. The data folder (log, settings) is not touched.
    /// </summary>
    public async Task<string?> RemoveAsync()
    {
        if (!OperatingSystem.IsWindows()) return "Only on Windows.";
        try
        {
            var start = new ProcessStartInfo(Uninstaller, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true, Verb = "runas" };
            using (var p = Process.Start(start))
            {
                if (p is not null) await p.WaitForExitAsync().ConfigureAwait(false);
            }
            // Inno's uninstaller hands over to a copy of itself in the temp folder and exits; the copy does the work.
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
            while (Find() is not null && DateTime.UtcNow < deadline) await Task.Delay(500).ConfigureAwait(false);
            if (Find() is not null) return "The old copy is still installed. Remove Costas " + Version + " in Settings, Apps, Installed apps.";
            Log.Information("Removed the old all-users install in {Folder}", Folder);
            return null;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == Cancelled)
        {
            return "Not removed: Windows' administrator prompt was cancelled.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Log.Warning("Removing the old all-users install failed: {Error}", ex.Message);
            return $"Could not remove it: {ex.Message}";
        }
    }
}
