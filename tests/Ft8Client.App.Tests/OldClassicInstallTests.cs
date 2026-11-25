// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

/// <summary>
/// The old all-users classic install: reading its Apps &amp; features entry, telling it apart from the running copy, and
/// the update bar's Remove old copy and Not now (the uninstaller itself needs Windows' administrator prompt, so it is
/// stood in for).
/// </summary>
public sealed class OldClassicInstallTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("costas-oldcopy-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly OldClassicInstall Old = new("0.1.474", @"C:\Program Files\Costas", @"C:\Program Files\Costas\unins000.exe");

    [Fact]
    public void FromUninstallEntry_InnoSetupEntry_ReadsVersionFolderAndUninstaller()
    {
        var old = OldClassicInstall.FromUninstallEntry("\"C:\\Program Files\\Costas\\unins000.exe\"", "C:\\Program Files\\Costas\\", "0.1.474", _ => true);

        old.Should().Be(Old);
    }

    [Fact]
    public void FromUninstallEntry_UninstallerGone_None()
    {
        OldClassicInstall.FromUninstallEntry(@"C:\Program Files\Costas\unins000.exe", @"C:\Program Files\Costas", "0.1.474", _ => false)
            .Should().BeNull("an entry left behind after the folder was deleted is nothing to remove");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void FromUninstallEntry_NoUninstaller_None(string? uninstall)
    {
        OldClassicInstall.FromUninstallEntry(uninstall, @"C:\Program Files\Costas", "0.1.474", _ => true).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FromUninstallEntry_NoInstallLocation_UsesTheUninstallersFolder(string? location)
    {
        var exe = Path.Combine(_dir, "unins000.exe");

        var old = OldClassicInstall.FromUninstallEntry(exe, location, null, _ => true);

        old!.Folder.Should().Be(_dir);
        old.Version.Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"C:\Users\tony\AppData\Local\Costas\current\", true)]
    [InlineData(@"C:\Program Files\Costas2\", true)]
    [InlineData(@"C:\Program Files\Costas\", false)]
    [InlineData(@"C:\Program Files\Costas\bin\", false)]
    [InlineData(@"c:\program files\costas", false)]
    public void IsSeparateFrom_RunningCopy_OnlyAnotherFolderCounts(string appDir, bool separate)
    {
        Old.IsSeparateFrom(appDir).Should().Be(separate);
    }

    [Fact]
    public void IsSeparateFrom_UnreadableFolder_NotOffered()
    {
        (Old with { Folder = "\0" }).IsSeparateFrom(@"C:\Users\tony\AppData\Local\Costas\current\").Should().BeFalse();
    }

    [Fact]
    public async Task Find_NotWindows_None()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows reads the registry.");
        OldClassicInstall.Find().Should().BeNull();
        (await Old.RemoveAsync()).Should().Be("Only on Windows.");
    }

    private (UpdateService Updates, UpdateBarViewModel Bar) Bar()
    {
        var s = new AppSettings();
        s.Updates.CheckEnabled = false;
        var settings = new AppSettingsAccessor(new SettingsStore(Path.Combine(_dir, "settings.json")), s);
        var updates = new UpdateService(settings, Path.Combine(_dir, "downloads"), Path.Combine(_dir, "logs"));
        var bar = new UpdateBarViewModel(updates, () => false, () => { });
        return (updates, bar);
    }

    [Fact]
    public async Task RemoveOldCopy_Removed_BarGoes()
    {
        var (updates, bar) = Bar();
        using var _ = updates;
        var removed = new List<OldClassicInstall>();
        updates.RemoveOldCopy = old =>
        {
            removed.Add(old);
            return Task.FromResult<string?>(null);
        };
        updates.ShowOldCopyForTests(Old);
        bar.OldCopyVisible.Should().BeTrue();

        await bar.RemoveOldCopyCommand.ExecuteAsync(null);

        removed.Should().Equal(Old);
        updates.OldCopy.Should().BeNull();
        bar.OldCopyVisible.Should().BeFalse();
        bar.OldCopyBusy.Should().BeFalse();
        bar.OldCopyDetail.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveOldCopy_PromptCancelled_BarStaysAndSaysWhy()
    {
        var (updates, bar) = Bar();
        using var _ = updates;
        updates.RemoveOldCopy = _ => Task.FromResult<string?>("Not removed: Windows' administrator prompt was cancelled.");
        updates.ShowOldCopyForTests(Old);

        await bar.RemoveOldCopyCommand.ExecuteAsync(null);

        bar.OldCopyVisible.Should().BeTrue("it can be tried again");
        bar.OldCopyDetail.Should().Be("Not removed: Windows' administrator prompt was cancelled.");
        bar.OldCopyBusy.Should().BeFalse();
        updates.OldCopy.Should().Be(Old);
    }

    [Fact]
    public async Task RemoveOldCopy_WhileRunning_SaysSoAndRunsOnce()
    {
        var (updates, bar) = Bar();
        using var _ = updates;
        var release = new TaskCompletionSource<string?>();
        var runs = 0;
        updates.RemoveOldCopy = _ =>
        {
            runs++;
            return release.Task;
        };
        updates.ShowOldCopyForTests(Old);

        var first = bar.RemoveOldCopyCommand.ExecuteAsync(null);
        bar.OldCopyBusy.Should().BeTrue();
        bar.OldCopyDetail.Should().Contain("administrator");
        var second = bar.RemoveOldCopyCommand.ExecuteAsync(null);
        release.SetResult(null);
        await first;
        await second;

        runs.Should().Be(1, "a second click while the uninstaller runs does nothing");
        bar.OldCopyVisible.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveOldCopy_NoneShown_DoesNothing()
    {
        var (updates, bar) = Bar();
        using var _ = updates;
        var runs = 0;
        updates.RemoveOldCopy = _ =>
        {
            runs++;
            return Task.FromResult<string?>(null);
        };

        (await updates.RemoveOldCopyAsync()).Should().BeNull();

        runs.Should().Be(0);
        bar.OldCopyVisible.Should().BeFalse();
    }
}
