// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Avalonia.Headless;
using Ft8Client.App.ViewModels;
using Ft8Client.Data.Database;
using Ft8Client.Data.Settings;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

/// <summary>
/// The callsign chip in the top bar: with more than one station profile the desk always shows whose callsign is in use,
/// and can switch while idle (owner request 2026-11-18).
/// </summary>
public class ProfileChipTests
{
    [Fact]
    public Task MainViewModel_OneProfile_NoChip() => WithMain(oneProfile: true, (host, vm) =>
    {
        vm.HasProfiles.Should().BeFalse();
        vm.Title.Should().Be("Costas · simulation");
    });

    [Fact]
    public Task MainViewModel_TwoProfiles_ShowsTheCallsignInUse() => WithMain(oneProfile: false, (host, vm) =>
    {
        vm.HasProfiles.Should().BeTrue();
        vm.ProfileText.Should().Be("K1UTA · Ash");
        vm.Title.Should().Be("Costas · simulation · K1UTA");
        vm.ProfileItems.Select(i => (i.Label, i.Tick)).Should().Equal(("K1GLV · Home", ""), ("K1UTA · Ash", "✓"));
        vm.ProfileFlagged.Should().BeFalse();
    });

    [Fact]
    public Task SwitchProfile_Idle_SwitchesAndTheChipFollows() => WithMain(oneProfile: false, (host, vm) =>
    {
        vm.SwitchProfileCommand.Execute("home");

        host.Settings.Current.Profile.Callsign.Should().Be("K1GLV");
        vm.ProfileText.Should().Be("K1GLV · Home");
        vm.ProfileItems.Single(i => i.InUse).Id.Should().Be("home");
        vm.ProfileFlagged.Should().BeFalse("the desk made this switch itself");
    });

    [Fact]
    public Task SwitchProfile_CallingCq_RefusedOnTheStatusLine() => WithMain(oneProfile: false, (host, vm) =>
    {
        host.Session.ToggleCq();
        host.Session.Snapshot.CallingCq.Should().BeTrue();

        vm.SwitchProfileCommand.Execute("home");

        host.Settings.Current.Profile.Callsign.Should().Be("K1UTA");
        vm.StatusText.Should().Be("The station profile cannot change while calling CQ as K1UTA. Try again when the station is idle.");
        host.Session.ToggleCq();
    });

    [Fact]
    public Task RemoteSwitch_FlagsTheChipForAMinute() => WithMain(oneProfile: false, (host, vm) =>
    {
        host.SwitchProfileForRemoteUser("u1").Refusal.Should().BeNull();

        vm.ProfileText.Should().Be("K1GLV · Home");
        vm.ProfileFlagged.Should().BeTrue("a phone switched whose callsign is on the air");
    });

    // Two profiles (Ash, K1UTA, in use; Home, K1GLV) and a remote user linked to Home, or one profile alone.
    private static async Task WithMain(bool oneProfile, Action<AppHost, MainViewModel> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-chip-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var settings = new AppSettings { SetupComplete = true };
        settings.Profile.Callsign = "K1GLV";
        settings.Profile.Grid = "FN42";
        if (!oneProfile)
        {
            settings.Profiles.Add(new StationProfile { Id = "ash", Name = "Ash", Callsign = "K1UTA", Grid = "FN42" });
            settings.ActiveProfile = "ash";
        }
        settings.Remote.Users.Add(new RemoteUser { Id = "u1", Username = "dan", ProfileId = "home" });
        settings.Updates.CheckEnabled = false;
        settings.PskReporter.FeedEnabled = false;
        settings.Usage.Enabled = false;
        settings.Dxpeditions.Enabled = false;
        new SettingsStore(Path.Combine(dir, "settings.json")).Save(settings);
        SqliteDatabase? database = null;
        try
        {
            await using var host = await AppHost.StartAsync(TestHost.Args(dir));
            database = host.Database;
            var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ProfileChipTests).Assembly);
            await session.Dispatch(() => test(host, new MainViewModel(host)), CancellationToken.None);
        }
        finally
        {
            if (database is not null)
            {
                using var connection = database.Open();
                SqliteConnection.ClearPool(connection);
            }
            AppearanceTests.DeleteWhenSettled(dir);
        }
    }
}
