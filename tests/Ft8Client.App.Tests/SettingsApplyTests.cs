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
using Ft8Client.Data.Secrets;
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

    // A simulated host in a temporary folder, with an in-memory credential store so nothing here touches the
    // operator's saved passwords.
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
            await using var host = await AppHost.StartAsync(new CommandLine
            {
                Home = dir, SimulateFolder = Path.Combine(TestPaths.Samples, "ft8"), Secrets = new InMemorySecretStore(),
            });
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
