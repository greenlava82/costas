// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Decoding;
using Ft8Client.Tests.Shared;

namespace Ft8Client.Decoding.Tests;

public class Jt9LocatorTests
{
    [Fact]
    public void Find_ConfiguredAppImageExtractedBesideIt_ReturnsExtractedJt9()
    {
        var dir = Directory.CreateTempSubdirectory("costas-jt9-appimage").FullName;
        var appImage = Path.Combine(dir, "wsjtx-3.0.1-linux-x86_64.AppImage");
        File.WriteAllText(appImage, "not run");
        var jt9 = Path.Combine(dir, "squashfs-root", "usr", "bin", Jt9Locator.ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(jt9)!);
        File.WriteAllText(jt9, "stand-in");

        Jt9Locator.Find(appImage, _ => true).Should().Be(Path.GetFullPath(jt9));
    }

    [Fact]
    public void Find_ConfiguredAppImageNotExtracted_NeverReturnsTheAppImage()
    {
        var dir = Directory.CreateTempSubdirectory("costas-jt9-appimage").FullName;
        var appImage = Path.Combine(dir, "wsjtx-3.0.1-linux-x86_64.AppImage");
        File.WriteAllText(appImage, "not run");
        var offered = new List<string>();

        var found = Jt9Locator.Find(appImage, path =>
        {
            offered.Add(path);
            return false;
        });

        found.Should().BeNull();
        offered.Should().NotContain(p => Jt9Locator.IsAppImage(p));
    }

    [Fact]
    public void Find_ConfiguredJt9DoesNotRun_TriesTheOthers()
    {
        var dir = Directory.CreateTempSubdirectory("costas-jt9-broken").FullName;
        var broken = Path.Combine(dir, Jt9Locator.ExecutableName);
        File.WriteAllText(broken, "stand-in");

        var found = Jt9Locator.Find(broken, path => path != Path.GetFullPath(broken));

        found.Should().NotBe(Path.GetFullPath(broken));
    }

    [Fact]
    public void Candidates_ConfiguredWsjtxFolder_LooksInBinAndUsrBin()
    {
        var dir = Directory.CreateTempSubdirectory("costas-jt9-folder").FullName;

        Jt9Locator.Candidates(dir).Take(3).Should().Equal(
            Path.Combine(dir, Jt9Locator.ExecutableName),
            Path.Combine(dir, "bin", Jt9Locator.ExecutableName),
            Path.Combine(dir, "usr", "bin", Jt9Locator.ExecutableName));
    }

    [Theory]
    [InlineData("/home/op/Applications/WSJT/wsjtx-3.0.1-linux-x86_64.AppImage", true)]
    [InlineData("/home/op/wsjtx.appimage ", true)]
    [InlineData("/usr/bin/jt9", false)]
    [InlineData(null, false)]
    public void IsAppImage_Path_RecognisesAppImages(string? path, bool expected)
    {
        Jt9Locator.IsAppImage(path).Should().Be(expected);
    }

    [Fact]
    public void Runs_ProgramThatStarts_ReturnsTrue()
    {
        var exe = StandIn("ok", "@echo Usage: jt9 [OPTIONS] file1 [file2 ...]\r\n", "#!/bin/sh\necho 'Usage: jt9 [OPTIONS] file1 [file2 ...]'\n");

        Jt9Locator.Runs(exe).Should().BeTrue();
    }

    [Fact]
    public void Runs_LibraryMissing_ReturnsFalse()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The loader's message and exit code are Linux's.");
        var exe = StandIn("nolib", "",
            "#!/bin/sh\necho 'jt9: error while loading shared libraries: libQt5Core.so.5: cannot open shared object file: No such file or directory' >&2\nexit 127\n");

        Jt9Locator.Runs(exe).Should().BeFalse();
    }

    [Fact]
    public void Runs_FileNotExecutable_ReturnsFalse()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no execute permission bit.");
        var dir = Directory.CreateTempSubdirectory("costas-jt9-noexec").FullName;
        var exe = Path.Combine(dir, "jt9");
        File.WriteAllText(exe, "#!/bin/sh\necho hi\n");

        Jt9Locator.Runs(exe).Should().BeFalse();
    }

    private static string StandIn(string name, string windows, string unix)
    {
        var dir = Directory.CreateTempSubdirectory("costas-jt9-" + name).FullName;
        var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "jt9.cmd" : "jt9");
        ExecutableScript.Write(exe, OperatingSystem.IsWindows() ? windows : unix);
        return exe;
    }
}
