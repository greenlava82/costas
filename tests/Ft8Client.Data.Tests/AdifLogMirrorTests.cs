// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Data.Adif;
using Ft8Client.Data.Files;
using Ft8Client.Data.Log;

namespace Ft8Client.Data.Tests;

/// <summary>The ADIF copy of the log in the backups folder (C8).</summary>
public class AdifLogMirrorTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 21, 0, 0, DateTimeKind.Utc);

    private static QsoRecord Contact(string call, string station) => new()
    {
        Call = call, Band = "20m", Mode = "FT8", QsoDateOn = Now.AddDays(-1), StationCallsign = station, ProfileId = "home",
    };

    [Fact]
    public void Write_TwoCallsigns_OneFilePerCallsign()
    {
        var folder = Path.Combine(Directory.CreateTempSubdirectory("ft8mirror").FullName, "backups");
        var mirror = new AdifLogMirror(() => [Contact("PA3A", "W7LIT"), Contact("DL1A", "VE3/W7LIT"), Contact("G4A", "W7LIT")], folder);

        var files = mirror.Write(Now);

        files.Select(Path.GetFileName).Should().Equal("costas-log-VE3_W7LIT.adi", "costas-log-W7LIT.adi");
        AdifReader.Read(File.ReadAllText(files[1])).Select(r => r["call"]).Should().Equal("PA3A", "G4A");
        Directory.EnumerateFiles(folder, "*" + AtomicFile.TempSuffix).Should().BeEmpty();
    }

    [Fact]
    public void Write_CallsignNoLongerInLog_RemovesItsFileOnly()
    {
        var folder = Directory.CreateTempSubdirectory("ft8mirror").FullName;
        File.WriteAllText(Path.Combine(folder, "costas-2026-11-18.db"), "daily copy");
        var log = new List<QsoRecord> { Contact("PA3A", "W7LIT"), Contact("DL1A", "K7OLD") };
        var mirror = new AdifLogMirror(() => log, folder);
        mirror.Write(Now);

        log.RemoveAt(1);
        mirror.Write(Now.AddMinutes(1));

        mirror.Existing().Select(Path.GetFileName).Should().Equal("costas-log-W7LIT.adi");
        File.Exists(Path.Combine(folder, "costas-2026-11-18.db")).Should().BeTrue("only the copy's own files are removed");
    }

    [Fact]
    public void Write_EmptyLog_WritesNoFile()
    {
        var folder = Directory.CreateTempSubdirectory("ft8mirror").FullName;

        new AdifLogMirror(() => [], folder).Write(Now).Should().BeEmpty();

        Directory.EnumerateFiles(folder).Should().BeEmpty();
    }

    [Fact]
    public void Write_FolderIsAFile_ThrowsIOException()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ft8mirror").FullName, "backups");
        File.WriteAllText(path, "not a folder");

        var act = () => new AdifLogMirror(() => [Contact("PA3A", "W7LIT")], path).Write(Now);

        act.Should().Throw<IOException>();
    }
}
