// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.App.Services;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

/// <summary>Settings changes survive a save that fails (evaluation B10).</summary>
public class AppSettingsAccessorTests
{
    [Fact]
    public void Update_SaveFails_KeepsChangeAndReportsProblem()
    {
        // A folder where the file should be: every write fails, on any OS and as any user.
        var path = Path.Combine(Directory.CreateTempSubdirectory("ft8acc").FullName, "settings.json");
        Directory.CreateDirectory(path);
        var accessor = new AppSettingsAccessor(new SettingsStore(path), new AppSettings());
        AppSettings? changed = null;
        accessor.Changed += s => changed = s;

        accessor.Update(s => s.Operating.Band = "40m");

        accessor.Current.Operating.Band.Should().Be("40m");
        changed!.Operating.Band.Should().Be("40m");
        accessor.Problem.Should().Contain("could not be saved");
    }

    [Fact]
    public void Update_AfterUnreadableFile_GoodSaveClearsProblem()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ft8acc").FullName, "settings.json");
        File.WriteAllText(path, "{ not json");
        var store = new SettingsStore(path);
        var accessor = new AppSettingsAccessor(store, store.Load());
        accessor.Problem.Should().NotBeNull();

        accessor.Update(s => s.Operating.Band = "40m");

        accessor.Problem.Should().BeNull();
        new SettingsStore(path).Load().Operating.Band.Should().Be("40m");
    }
}
