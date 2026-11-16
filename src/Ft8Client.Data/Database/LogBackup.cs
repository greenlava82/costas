// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Ft8Client.Data.Database;

/// <summary>
/// Daily copies of the log database and the settings file, so a damaged disk, a bad import or a mistaken bulk delete
/// can be undone. Each copy is a consistent snapshot taken with SQLite's online backup while Costas keeps running.
/// Copies are named by UTC date (<c>costas-2026-11-15.db</c>, <c>settings-2026-11-15.json</c>); the newest
/// <see cref="Keep"/> days are kept.
/// </summary>
public sealed class LogBackup(SqliteDatabase database, string folder, string? settingsPath = null)
{
    /// <summary>How many daily copies are kept.</summary>
    public const int Keep = 14;

    /// <summary>A copy is taken when the newest is at least this old.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private const string DbPrefix = "costas-", DbSuffix = ".db", SettingsPrefix = "settings-", SettingsSuffix = ".json";

    /// <summary>The backups folder.</summary>
    public string Folder { get; } = folder;

    /// <summary>The newest database copy and when it was written, or null if there is none.</summary>
    public (string Path, DateTime WrittenUtc)? Newest()
    {
        if (!Directory.Exists(Folder)) return null;
        var newest = Directory.EnumerateFiles(Folder, DbPrefix + "*" + DbSuffix).Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        return newest is null ? null : (newest.FullName, newest.LastWriteTimeUtc);
    }

    /// <summary>True when no copy was written in the last <see cref="Interval"/>.</summary>
    public bool IsDue(DateTime nowUtc) => Newest() is not { } n || nowUtc - n.WrittenUtc >= Interval;

    /// <summary>
    /// Writes today's copies (replacing an earlier one from the same UTC day) and removes copies beyond
    /// <see cref="Keep"/> days. Returns the database copy's path. Throws <see cref="IOException"/> or
    /// <see cref="SqliteException"/> when the copy cannot be written.
    /// </summary>
    public string Run(DateTime nowUtc)
    {
        Directory.CreateDirectory(Folder);
        var day = nowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var target = Path.Combine(Folder, DbPrefix + day + DbSuffix);
        var tmp = target + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var source = database.Open())
        using (var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tmp, Pooling = false }.ToString()))
        {
            dest.Open();
            source.BackupDatabase(dest);
        }
        File.Move(tmp, target, overwrite: true);
        File.SetLastWriteTimeUtc(target, nowUtc);
        if (settingsPath is not null && File.Exists(settingsPath))
            File.Copy(settingsPath, Path.Combine(Folder, SettingsPrefix + day + SettingsSuffix), overwrite: true);
        Prune(DbPrefix, DbSuffix);
        Prune(SettingsPrefix, SettingsSuffix);
        return target;
    }

    // Names sort by date, so the newest Keep names stay.
    private void Prune(string prefix, string suffix)
    {
        foreach (var old in Directory.EnumerateFiles(Folder, prefix + "*" + suffix).Order(StringComparer.Ordinal).Reverse().Skip(Keep))
            File.Delete(old);
    }
}
