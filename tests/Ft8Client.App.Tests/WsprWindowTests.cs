// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ft8Client.App.ViewModels;
using Ft8Client.App.Views;
using Ft8Client.Core;
using Ft8Client.Data.Database;
using Ft8Client.Data.Settings;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

/// <summary>The main window in WSPR mode: the WSPR screen takes the place of Operate, and the beacon starts only on a click.</summary>
public class WsprWindowTests
{
    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task WithWindowAsync(Action<AppHost, MainViewModel, MainWindow> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-wspr-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var settings = new AppSettings { SetupComplete = true };
        settings.Updates.CheckEnabled = false;
        settings.PskReporter.FeedEnabled = false;
        settings.Usage.Enabled = false;
        settings.Dxpeditions.Enabled = false;
        settings.Profile.Callsign = "W7LIT";
        settings.Profile.Grid = "DN40";
        new SettingsStore(Path.Combine(dir, "settings.json")).Save(settings);
        SqliteDatabase? database = null;
        try
        {
            await using var host = await AppHost.StartAsync(TestHost.Args(dir));
            database = host.Database;
            var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(WsprWindowTests).Assembly);
            await session.DispatchThenStop(host, () =>
            {
                var vm = new MainViewModel(host);
                var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                window.Show();
                try
                {
                    Settle(window);
                    test(host, vm, window);
                }
                finally
                {
                    window.Close();
                }
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

    [Fact]
    public async Task Window_Ft8_ShowsOperateAndNotTheWsprScreen()
    {
        await WithWindowAsync((host, vm, window) =>
        {
            window.GetVisualDescendants().OfType<OperateView>().Single().IsEffectivelyVisible.Should().BeTrue();
            window.GetVisualDescendants().OfType<WsprView>().Single().IsEffectivelyVisible.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Window_WsprMode_ShowsTheWsprScreenInPlaceOfOperate()
    {
        await WithWindowAsync((host, vm, window) =>
        {
            host.Session.SetBand("20m", Mode.Wspr);
            vm.Apply(host.Session.Snapshot);
            Settle(window);

            window.GetVisualDescendants().OfType<WsprView>().Single().IsEffectivelyVisible.Should().BeTrue();
            window.GetVisualDescendants().OfType<OperateView>().Single().IsEffectivelyVisible.Should().BeFalse();
            var buttons = window.GetVisualDescendants().OfType<WsprView>().Single().GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
            buttons.Should().Contain("Start beacon");
        });
    }

    [Fact]
    public async Task Window_WsprMode_OtherTabsStillShow()
    {
        await WithWindowAsync((host, vm, window) =>
        {
            host.Session.SetBand("20m", Mode.Wspr);
            vm.Apply(host.Session.Snapshot);
            vm.SelectTab(3);
            Settle(window);

            window.GetVisualDescendants().OfType<WsprView>().Single().IsEffectivelyVisible.Should().BeFalse("the Log tab is showing");
            window.GetVisualDescendants().OfType<OperateView>().Single().IsEffectivelyVisible.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Window_WsprMode_NothingIsTransmittedWithoutAClick()
    {
        await WithWindowAsync((host, vm, window) =>
        {
            host.Session.SetBand("20m", Mode.Wspr);
            vm.Apply(host.Session.Snapshot);
            Settle(window);

            host.Session.BeaconOn.Should().BeFalse();
            host.Session.Snapshot.Transmitting.Should().BeFalse();
            vm.Wspr.BeaconOn.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Window_BeaconRunning_BandChangeAsksFirst()
    {
        await WithWindowAsync((host, vm, window) =>
        {
            host.Session.SetBand("20m", Mode.Wspr);
            host.Rig.SetFrequencyAsync(14_095_600, CancellationToken.None).GetAwaiter().GetResult(); // the radio follows, as SetBandAsync tunes it
            host.Session.SetDial(14_095_600);
            vm.Apply(host.Session.Snapshot);
            host.Session.StartBeaconAsync().GetAwaiter().GetResult().Should().BeNull();
            vm.Apply(host.Session.Snapshot);

            vm.SelectedMode = "PSK31"; // the menu showed FT8 (the session was moved directly), so a different mode is a change
            Settle(window);

            vm.Wspr.LeaveQuestion.Should().Be("Stop the beacon and switch to PSK31?");
            host.Session.BeaconOn.Should().BeTrue("nothing changes until the operator says yes");
            host.Session.Config.Mode.Should().Be(Mode.Wspr);
            vm.SelectedMode.Should().Be("WSPR");
        });
    }
}
