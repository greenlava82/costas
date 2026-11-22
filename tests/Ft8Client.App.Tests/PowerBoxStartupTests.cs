// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia.Headless;
using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.Data.Database;
using Ft8Client.Data.Settings;
using Ft8Client.Rig;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

/// <summary>
/// The power box in the bottom bar shows a reading the radio gave before the main window was built: the reading only
/// raises Changed when it changes, so a window that never asked kept the box hidden (owner report 2026-11-14).
/// </summary>
public class PowerBoxStartupTests
{
    [Fact]
    public async Task MainViewModel_PowerReadBeforeItWasBuilt_ShowsThePowerBox()
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-power-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var settings = new AppSettings { SetupComplete = true };
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
            ((SimulatedRig)host.Rig).PowerWatts = 25;
            // A refresh is skipped while the service's own loop is reading, so ask until the reading is in.
            for (var i = 0; i < 50 && host.RadioPower.Power is null; i++)
            {
                await host.RadioPower.RefreshAsync();
                if (host.RadioPower.Power is null) await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            host.RadioPower.Power.Should().NotBeNull("the reading is taken before the window exists");

            var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(PowerBoxStartupTests).Assembly);
            await session.DispatchThenStop(host, () =>
            {
                var vm = new MainViewModel(host);

                vm.HasPower.Should().BeTrue();
                vm.PowerText.Should().Be("25 W");
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
