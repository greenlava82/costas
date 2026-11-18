// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia.Headless;
using Avalonia.Threading;
using Ft8Client.App.ViewModels;
using Ft8Client.Data.Database;
using Ft8Client.Data.Settings;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

/// <summary>Apply saves like Save but keeps Settings open, so it can be pressed again and again.</summary>
public class SettingsApplyTests
{
    [Fact]
    public Task Apply_ValidChanges_SavedAndWindowStaysOpen() => WithSettings((host, vm, closed) =>
    {
        vm.Callsign = "K0GST";
        vm.LotwUser = "k0gst";
        vm.ApplyCommand.Execute(null);

        closed().Should().Be(0, "Apply keeps the window open");
        vm.Message.Should().BeEmpty();
        vm.AppliedNote.Should().StartWith("Applied");
        host.Settings.Current.Profile.Callsign.Should().Be("K0GST");
        host.Settings.Current.Profile.Lotw.Username.Should().Be("k0gst");

        vm.Callsign = "K0GST/P";
        vm.ApplyCommand.Execute(null);
        host.Settings.Current.Profile.Callsign.Should().Be("K0GST/P", "a second Apply saves the next change");

        vm.SaveCommand.Execute(null);
        closed().Should().Be(1, "Save still closes");
    });

    [Fact]
    public Task Apply_InvalidCallsign_RefusedAndNothingApplied() => WithSettings((host, vm, closed) =>
    {
        vm.Callsign = "NOT A CALL";
        vm.ApplyCommand.Execute(null);

        vm.Message.Should().Contain("callsign");
        vm.AppliedNote.Should().BeEmpty();
        closed().Should().Be(0);
        host.Settings.Current.Profile.Callsign.Should().Be("N0CALL");
    });

    [Fact]
    public Task Apply_NewRemotePassword_SetOnceThenTheBoxEmpties() => WithSettings((host, vm, _) =>
    {
        vm.AddRemoteUserCommand.Execute(null);
        var row = vm.RemoteUsers.Single();
        row.Username = "guest";
        row.NewPassword = "correct horse battery";
        vm.ApplyCommand.Execute(null);

        row.NewPassword.Should().BeEmpty("a second Apply must not set it again and sign the user out everywhere");
        row.HasPassword.Should().BeTrue();
        host.Remote.Control.Users!.HasPassword(row.Id).Should().BeTrue();

        // Signed in on a phone, then another Apply: the sign-in survives.
        var login = host.Remote.Control.Users!.Login("guest", "correct horse battery", "127.0.0.1");
        login.Token.Should().NotBeNull();
        vm.ApplyCommand.Execute(null);
        host.Remote.Control.Users!.SignIn(login.Token!).Should().NotBeNull();
    });

    [Fact]
    public Task RemoteUserProfile_LinkedThenOperateAs_SwitchesAndWindowDoesNotSwitchBack() => WithSettings((host, vm, _) =>
    {
        var home = vm.SelectedProfile!;
        vm.NewOperatorProfileCommand.Execute(null);
        var guest = vm.SelectedProfile!;
        vm.Callsign = "K0GST";
        vm.SelectedProfile = home;
        vm.AddRemoteUserCommand.Execute(null);
        var row = vm.RemoteUsers.Single();
        row.Username = "guest";
        row.NewPassword = "correct horse battery";
        row.ProfileChoices.Select(c => c.Label).Should().Equal(ProfileChoice.NoneLabel, "Home (N0CALL)", "Operator (K0GST)");
        row.SelectedProfileChoice.Should().Be(row.ProfileChoices[0], "a new user operates as the profile in use");
        row.SelectedProfileChoice = row.ProfileChoices.Single(c => c.Id == guest.Id);
        vm.ApplyCommand.Execute(null);
        vm.Message.Should().BeEmpty();
        host.Settings.Current.ActiveProfile.Should().Be(home.Id);
        host.Settings.Current.Remote.Users.Single().ProfileId.Should().Be(guest.Id);

        // The guest taps Operate as on their phone while the station is idle.
        host.SwitchProfileForRemoteUser(row.Id).Should().Be(((string?)null, "K0GST (Operator profile)"));
        host.Settings.Current.Profile.Callsign.Should().Be("K0GST");
        host.SwitchProfileForRemoteUser(row.Id).Should().Be(((string?)null, (string?)null), "already in use");

        // The desk's Settings window, open since before, saves something else: it keeps the guest's profile.
        vm.PreferHearsMe = !vm.PreferHearsMe;
        vm.ApplyCommand.Execute(null);
        vm.Message.Should().BeEmpty();
        host.Settings.Current.ActiveProfile.Should().Be(guest.Id);

        // Deleting the profile drops the link: the user then operates as whichever profile is in use.
        vm.SelectedProfile = vm.Profiles.Single(p => p.Id == guest.Id);
        vm.DeleteProfileCommand.Execute(null);
        vm.ApplyCommand.Execute(null);
        host.Settings.Current.Remote.Users.Single().ProfileId.Should().BeNull();
        host.SwitchProfileForRemoteUser(row.Id).Should().Be(((string?)null, (string?)null));
    });

    [Fact]
    public Task NewOperatorProfile_FromHome_KeepsTheStationButNothingOfTheOperator() => WithSettings((host, vm, _) =>
    {
        vm.OperatorName = "Dan";
        vm.Qth = "Boise ID";
        vm.LicenceChoice = SettingsViewModel.LicenceName(Ft8Client.Core.Bands.UsLicenceClass.Technician);
        vm.LotwEnabled = true;
        vm.LotwUser = "N0CALL";
        vm.EqslUser = "n0call";
        vm.ApplyCommand.Execute(null);
        var home = vm.SelectedProfile!;

        vm.NewOperatorProfileCommand.Execute(null);

        var guest = vm.SelectedProfile!;
        guest.Should().NotBeSameAs(home);
        guest.Name.Should().Be("Operator");
        vm.Callsign.Should().BeEmpty();
        vm.OperatorName.Should().BeEmpty();
        vm.Grid.Should().Be("DN40", "the station has not moved");
        vm.Qth.Should().Be("Boise ID");
        vm.LotwEnabled.Should().BeFalse();
        vm.LotwUser.Should().BeEmpty();
        vm.EqslUser.Should().BeEmpty();
        vm.LicenceChoice.Should().Be("Not US / don't check", "the licence check is off until the new operator chooses a class");

        // Save asks for the new operator's callsign first.
        vm.ApplyCommand.Execute(null);
        vm.Message.Should().Contain("Operator profile");
        vm.Callsign = "K0GST";
        vm.OperatorName = "Ash";
        vm.ApplyCommand.Execute(null);
        vm.Message.Should().BeEmpty();

        var s = host.Settings.Current;
        s.Profile.Name.Should().Be("Operator");
        s.Profile.Callsign.Should().Be("K0GST");
        s.Profile.OperatorName.Should().Be("Ash");
        s.Profile.Rig.Should().BeEquivalentTo(s.Profiles.Single(p => p.Id == home.Id).Rig, "the same radio");
        s.Profile.Lotw.Username.Should().BeNull();
        s.Profile.LicenceClass.Should().Be("none");
        s.Profiles.Single(p => p.Id == home.Id).OperatorName.Should().Be("Dan", "the first operator's name stays theirs");

        // Each profile shows its own name again.
        vm.SelectedProfile = home;
        vm.OperatorName.Should().Be("Dan");
        vm.LotwUser.Should().Be("N0CALL");
        vm.SelectedProfile = guest;
        vm.OperatorName.Should().Be("Ash");
    });

    [Fact]
    public Task Save_AnotherProfileShown_DoesNotTakeTheFirstOnesName() => WithSettings((host, vm, closed) =>
    {
        // Settings used to load the name, QTH, radio and antenna once, from the profile active when it opened, and write
        // them into whichever profile was shown at Save.
        vm.OperatorName = "Dan";
        vm.RigDescription = "FT-710";
        vm.NewProfileCommand.Execute(null);
        vm.OperatorName.Should().Be("Dan", "a copy starts with the same details");
        vm.OperatorName = "Dan portable";
        vm.SaveCommand.Execute(null);

        closed().Should().Be(1);
        host.Settings.Current.Profile.OperatorName.Should().Be("Dan portable");
        host.Settings.Current.Profiles[0].OperatorName.Should().Be("Dan");
        host.Settings.Current.Profiles[0].RigDescription.Should().Be("FT-710");
    });

    [Fact]
    public Task FlexAddress_TypedAndSaved_KeptPerProfileAndFollowed() => WithSettings((host, vm, _) =>
    {
        vm.FlexPlaceholder.Should().Be("Not set, for example 192.168.1.20");
        vm.FlexStatus.Should().BeEmpty("nothing to say before an address is set");

        vm.FlexAddress = " 192.168.1.20 ";
        vm.SaveCommand.Execute(null);

        host.Settings.Current.Profile.Rig.FlexAddress.Should().Be("192.168.1.20");
        host.Accessories.Settled.Wait(TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        vm.FlexStatus.Should().Be("Not while replaying recordings.", "the tests' host replays recordings, so nothing connects");

        vm.NewProfileCommand.Execute(null);
        vm.FlexAddress.Should().Be(" 192.168.1.20 ", "a copy of the profile has the same radio");
        vm.FlexAddress = string.Empty;
        vm.SaveCommand.Execute(null);
        host.Settings.Current.Profile.Rig.FlexAddress.Should().BeNull();
        host.Settings.Current.Profiles[0].Rig.FlexAddress.Should().Be("192.168.1.20");
    });

    // A simulated host in a temporary folder (TestHost: an in-memory credential store, so nothing here touches the
    // operator's saved passwords).
    private static async Task WithSettings(Action<AppHost, SettingsViewModel, Func<int>> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-apply-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var settings = new AppSettings { SetupComplete = true };
        settings.Profile.Callsign = "N0CALL";
        settings.Profile.Grid = "DN40";
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
            var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SettingsApplyTests).Assembly);
            await session.Dispatch(() =>
            {
                var vm = new SettingsViewModel(host);
                var closed = 0;
                vm.Close += () => closed++;
                test(host, vm, () => closed);
            }, CancellationToken.None);
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
