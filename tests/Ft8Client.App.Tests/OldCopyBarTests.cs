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
using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.App.Views;
using Ft8Client.Data.Database;
using Ft8Client.Data.Settings;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

public class OldCopyBarTests
{
    [Fact]
    public async Task OldCopyBar_OldAllUsersInstall_ShownUntilNotNow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-oldcopy-" + Guid.NewGuid());
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
            var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(OldCopyBarTests).Assembly);
            await session.DispatchThenStop(host, () =>
            {
                var vm = new MainViewModel(host);
                var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                window.Show();
                try
                {
                    Settle(window);
                    var bar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "OldCopyBar");
                    bar.IsEffectivelyVisible.Should().BeFalse("no old copy is installed here");

                    host.Updates!.ShowOldCopyForTests(new OldClassicInstall("0.1.474", @"C:\Program Files\Costas", @"C:\Program Files\Costas\unins000.exe"));
                    Settle(window);
                    bar.IsEffectivelyVisible.Should().BeTrue();
                    vm.Update.OldCopyText.Should().Contain("0.1.474").And.Contain(@"C:\Program Files\Costas");

                    vm.Update.DismissOldCopyCommand.Execute(null);
                    Settle(window);
                    bar.IsEffectivelyVisible.Should().BeFalse("Not now hides it until Costas starts again");
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
