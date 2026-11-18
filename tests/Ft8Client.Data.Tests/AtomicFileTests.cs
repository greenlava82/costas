// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Data.Files;

namespace Ft8Client.Data.Tests;

/// <summary>Files swapped in whole, so a crash never leaves half of one (C8).</summary>
public class AtomicFileTests
{
    private static string NewDir() => Directory.CreateTempSubdirectory("ft8atomic").FullName;

    [Fact]
    public void WriteAllText_ExistingFile_ReplacedAndNoTempLeft()
    {
        var path = Path.Combine(NewDir(), "log.adi");
        File.WriteAllText(path, "old");

        AtomicFile.WriteAllText(path, "new é");

        File.ReadAllText(path).Should().Be("new é");
        File.Exists(path + AtomicFile.TempSuffix).Should().BeFalse();
        File.ReadAllBytes(path).Take(3).Should().NotEqual([0xEF, 0xBB, 0xBF], "no byte order mark");
    }

    [Fact]
    public void WriteAllText_TempLeftByCrash_OverwrittenAndRemoved()
    {
        var path = Path.Combine(NewDir(), "log.adi");
        File.WriteAllText(path + AtomicFile.TempSuffix, "half a fi");

        AtomicFile.WriteAllText(path, "whole");

        File.ReadAllText(path).Should().Be("whole");
        File.Exists(path + AtomicFile.TempSuffix).Should().BeFalse();
    }

    [Fact]
    public void WriteAllText_TempCannotBeWritten_ThrowsAndKeepsOldFile()
    {
        var path = Path.Combine(NewDir(), "log.adi");
        File.WriteAllText(path, "old and whole");
        Directory.CreateDirectory(path + AtomicFile.TempSuffix); // stands in for a full disk: the temp file cannot be made

        var act = () => AtomicFile.WriteAllText(path, "new");

        act.Should().Throw<Exception>().Which.Should().Match(e => e is IOException || e is UnauthorizedAccessException);
        File.ReadAllText(path).Should().Be("old and whole");
    }

    [Fact]
    public void WriteAllText_FolderGone_ThrowsIOException()
    {
        var path = Path.Combine(NewDir(), "gone", "log.adi");

        var act = () => AtomicFile.WriteAllText(path, "x");

        act.Should().Throw<IOException>();
    }
}
