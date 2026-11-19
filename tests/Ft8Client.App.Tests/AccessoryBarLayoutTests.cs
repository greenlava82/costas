// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ft8Client.App.ViewModels;
using Ft8Client.App.Views;
using Ft8Client.Data.Database;
using Ft8Client.Data.Settings;
using Ft8Client.Rig.Flex;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

public class AccessoryBarLayoutTests
{
    // The widest the button gets: both devices, the longest states.
    private static readonly FlexAccessories Both = new[]
    {
        "S0|amplifier 0x18E5F0AA model=PowerGeniusXL state=TRANSMIT_A",
        "S0|amplifier 0x2C01D3B4 model=TunerGeniusXL operate=1 bypass=0 tuning=1",
    }.Aggregate(FlexAccessories.Empty, (a, l) => a.With(FlexStatusLine.Parse(l)!));

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1280, 720)]
    public async Task BottomBar_AmpAndTunerShown_FitsBesideTuneAndHalt(int width, int height)
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-accessory-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var settings = new AppSettings { SetupComplete = true };
        settings.Updates.CheckEnabled = false;
        settings.PskReporter.FeedEnabled = false;
        settings.Usage.Enabled = false;
        settings.Dxpeditions.Enabled = false;
        settings.Window.Width = width;
        settings.Window.Height = height;
        new SettingsStore(Path.Combine(dir, "settings.json")).Save(settings);
        SqliteDatabase? database = null;
        try
        {
            await using var host = await AppHost.StartAsync(TestHost.Args(dir));
            database = host.Database;
            var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AccessoryBarLayoutTests).Assembly);
            await session.Dispatch(() =>
            {
                var vm = new MainViewModel(host);
                var window = new MainWindow { DataContext = vm, Width = width, Height = height };
                window.Show();
                try
                {
                    Settle(window);
                    var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AccessoryButton");
                    button.IsEffectivelyVisible.Should().BeFalse("nothing is paired in simulation");

                    // Through the service, as the radio's status would arrive: a snapshot update meanwhile rebuilds the
                    // panel from the service, so setting the view model's panel directly would be undone at random.
                    host.Accessories.ShowForTests(Both);
                    Settle(window);
                    vm.Accessory.BarText.Should().Be("Amp: Transmitting  Tuner: Tuning");

                    button.IsEffectivelyVisible.Should().BeTrue();
                    var halt = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Halt transmit");
                    foreach (var b in new[] { button, halt })
                    {
                        var left = b.TranslatePoint(default, window)!.Value.X;
                        left.Should().BeGreaterThanOrEqualTo(0, $"{AutomationProperties.GetName(b)} starts on screen at {width} px");
                        (left + b.Bounds.Width).Should().BeLessThanOrEqualTo(width + 0.5, $"{AutomationProperties.GetName(b)} ends on screen at {width} px");
                    }
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

    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
