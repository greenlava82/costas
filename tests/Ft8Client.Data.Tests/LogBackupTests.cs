// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Data.Database;
using Ft8Client.Data.Log;

namespace Ft8Client.Data.Tests;

/// <summary>Daily copies of the log and settings (evaluation B14).</summary>
public class LogBackupTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 2, 0, 0, DateTimeKind.Utc);

    private static (SqliteDatabase Db, string Dir) NewLog()
    {
        var dir = Directory.CreateTempSubdirectory("ft8bak").FullName;
        var db = new SqliteDatabase(Path.Combine(dir, "costas.db"));
        new QsoRepository(db).Insert(new QsoRecord { Call = "K1ABC", Band = "20m", Mode = "FT8", QsoDateOn = Now.AddDays(-1), StationCallsign = "N0CALL" });
        return (db, dir);
    }

    [Fact]
    public void Run_LogWithContact_CopyOpensWithTheContact()
    {
        var (db, dir) = NewLog();
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{}");
        var backup = new LogBackup(db, Path.Combine(dir, "backups"), Path.Combine(dir, "settings.json"));

        var path = backup.Run(Now);

        Path.GetFileName(path).Should().Be("costas-2026-11-15.db");
        new QsoRepository(new SqliteDatabase(path)).Count().Should().Be(1);
        File.Exists(Path.Combine(dir, "backups", "settings-2026-11-15.json")).Should().BeTrue();
        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void IsDue_NoneOrOld_TrueAndRecent_False()
    {
        var (db, dir) = NewLog();
        var backup = new LogBackup(db, Path.Combine(dir, "backups"));
        backup.IsDue(Now).Should().BeTrue("there is no copy yet");

        backup.Run(Now);

        backup.IsDue(Now.AddHours(23)).Should().BeFalse();
        backup.IsDue(Now.AddHours(24)).Should().BeTrue();
    }

    [Fact]
    public void Run_MoreThanKeepDays_KeepsNewest()
    {
        var (db, dir) = NewLog();
        var backup = new LogBackup(db, Path.Combine(dir, "backups"));

        for (var d = 0; d < LogBackup.Keep + 3; d++) backup.Run(Now.AddDays(d));

        var names = Directory.EnumerateFiles(backup.Folder, "costas-*.db").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        names.Should().HaveCount(LogBackup.Keep);
        names[0].Should().Be("costas-2026-11-18.db", "the three oldest days are gone");
    }
}
