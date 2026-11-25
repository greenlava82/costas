// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Ft8Client.App.ViewModels;
using Ft8Client.App.Views;
using Ft8Client.Data.Database;
using Ft8Client.Data.Settings;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

public class WaterfallCallsSwitchTests
{
    [Fact]
    public async Task Calls_SwitchedInStripOrPoppedOutWindow_AppliesToBothAndIsRemembered()
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-wfcalls-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var settings = new AppSettings { SetupComplete = true };
        settings.Updates.CheckEnabled = false;
        settings.PskReporter.FeedEnabled = false;
        settings.Usage.Enabled = false;
        settings.Dxpeditions.Enabled = false;
        settings.Window.WaterfallCallsigns = false;
        new SettingsStore(Path.Combine(dir, "settings.json")).Save(settings);
        SqliteDatabase? database = null;
        try
        {
            await using var host = await AppHost.StartAsync(TestHost.Args(dir));
            database = host.Database;
            var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(WaterfallCallsSwitchTests).Assembly);
            await session.DispatchThenStop(host, () =>
            {
                var vm = new MainViewModel(host);
                var window = new MainWindow { DataContext = vm };
                window.Show();
                try
                {
                    var strip = window.FindControl<WaterfallView>("ReceiveWaterfall")!;
                    var calls = window.FindControl<ToggleButton>("CallsButton")!;
                    calls.IsChecked.Should().BeFalse("the saved setting is off");
                    strip.ShowCallTags.Should().BeFalse();

                    calls.IsChecked = true;
                    strip.ShowCallTags.Should().BeTrue();
                    host.Settings.Current.Window.WaterfallCallsigns.Should().BeTrue();

                    window.FindControl<Button>("PopOutButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var popped = window.OwnedWindows.OfType<WaterfallWindow>().Should().ContainSingle().Subject;
                    popped.Calls.IsChecked.Should().BeTrue("the window starts with the switch as it is");
                    popped.View.ShowCallTags.Should().BeTrue();

                    popped.Calls.IsChecked = false;
                    popped.View.ShowCallTags.Should().BeFalse();
                    strip.ShowCallTags.Should().BeFalse("the strip comes back with the same choice");
                    calls.IsChecked.Should().BeFalse();
                    host.Settings.Current.Window.WaterfallCallsigns.Should().BeFalse();

                    popped.Close();
                    strip.ShowCallTags.Should().BeFalse();
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
}
