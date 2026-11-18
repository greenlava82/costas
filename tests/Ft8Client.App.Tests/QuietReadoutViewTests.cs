// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia.Headless;
using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.Core.Decoding;
using Ft8Client.Data.Database;
using Ft8Client.Data.Settings;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

/// <summary>The Operate view's "why is it quiet" bar follows the snapshot, shows on Operate only, and its button only advises.</summary>
public class QuietReadoutViewTests
{
    private static async Task WithViewModelAsync(Action<MainViewModel, AppHost> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-quiet-" + Guid.NewGuid());
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
            var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(QuietReadoutViewTests).Assembly);
            await session.Dispatch(() => test(new MainViewModel(host), host), CancellationToken.None);
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

    private static readonly QuietReadout DialOff = new(QuietCause.DialOff, "No decodes: the dial is on 14.090, not the usual FT8 frequency for 20 m.", 14_074_000);

    [Fact]
    public async Task Apply_DialOff_ShowsTheLineAndGoTo() => await WithViewModelAsync((vm, host) =>
    {
        vm.Apply(host.Session.Snapshot with { Quiet = DialOff, Transmitting = false });
        vm.QuietVisible.Should().BeTrue();
        vm.QuietText.Should().Be(DialOff.Text);
        vm.QuietGoText.Should().Be("Go to 14.074");
        vm.QuietCanGo.Should().BeTrue();
    });

    [Fact]
    public async Task Apply_NoReadout_Hidden() => await WithViewModelAsync((vm, host) =>
    {
        vm.Apply(host.Session.Snapshot with { Quiet = DialOff });
        vm.Apply(host.Session.Snapshot with { Quiet = null });
        vm.QuietVisible.Should().BeFalse();
    });

    [Fact]
    public async Task Apply_FixIsNotAFrequency_NoButton() => await WithViewModelAsync((vm, host) =>
    {
        vm.Apply(host.Session.Snapshot with { Quiet = new QuietReadout(QuietCause.Silence, "No decodes: the radio's sound device sends only silence.") });
        vm.QuietVisible.Should().BeTrue();
        vm.QuietCanGo.Should().BeFalse();
    });

    [Fact]
    public async Task Apply_Transmitting_NoGoTo() => await WithViewModelAsync((vm, host) =>
    {
        vm.Apply(host.Session.Snapshot with { Quiet = DialOff, Transmitting = true });
        vm.QuietCanGo.Should().BeFalse("the radio never changes frequency under a transmission");
    });

    [Fact]
    public async Task Tab_OtherView_HidesTheLine() => await WithViewModelAsync((vm, host) =>
    {
        vm.Apply(host.Session.Snapshot with { Quiet = DialOff });
        vm.Tab = 1;
        vm.QuietVisible.Should().BeFalse();
        vm.Tab = 0;
        vm.QuietVisible.Should().BeTrue();
    });

    [Fact]
    public async Task Apply_LineShown_RadioNotRetuned() => await WithViewModelAsync((vm, host) =>
    {
        var before = host.Rig.GetStateAsync(CancellationToken.None).GetAwaiter().GetResult().FrequencyHz;
        vm.Apply(host.Session.Snapshot with { Quiet = DialOff with { GoToDialHz = before + 16_000 } });
        host.Rig.GetStateAsync(CancellationToken.None).GetAwaiter().GetResult().FrequencyHz.Should().Be(before,
            "only the operator's click on Go to tunes the radio");
    });
}
