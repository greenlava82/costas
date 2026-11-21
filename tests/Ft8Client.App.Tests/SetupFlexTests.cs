// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.ViewModels;
using Ft8Client.Data.Database;
using Ft8Client.Rig;
using Ft8Client.Rig.Flex;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

public class SetupFlexTests
{
    private static readonly HamlibModel[] Models =
    [
        new(2, "Hamlib", "NET rigctl", "Stable"),
        new(HamlibModel.SmartSdrSliceA, "FlexRadio", "SmartSDR Slice A", "Beta"),
        new(HamlibModel.SmartSdrSliceA + 1, "FlexRadio", "SmartSDR Slice B", "Beta"),
    ];

    private static readonly FlexFoundRadio Radio = new(new FlexRadioInfo("FLEX-8600", "1", "Shack", "", "192.168.1.20", 4992));

    [Fact]
    public async Task ApplyFlex_TransmitSliceB_PicksSliceBModelAddressAndCatKeying()
    {
        await WithSetup(vm =>
        {
            vm.Ptt = "rts";

            vm.ApplyFlex(Radio, new FlexSlice(1, true, true, 2, "B")).Should().BeTrue();

            vm.Model!.Number.Should().Be(HamlibModel.SmartSdrSliceA + 1);
            vm.Manufacturer.Should().Be("FlexRadio");
            vm.NetworkAddress.Should().Be("192.168.1.20:4992");
            vm.Ptt.Should().Be("cat");
            vm.FlexNote.Should().StartWith("Set up for the FLEX-8600 at 192.168.1.20:4992, slice B, keyed by CAT.");
        });
    }

    [Fact]
    public async Task ApplyFlex_RadioDidNotSay_UsesSliceA()
    {
        await WithSetup(vm =>
        {
            vm.ApplyFlex(Radio, null).Should().BeTrue();

            vm.Model!.Number.Should().Be(HamlibModel.SmartSdrSliceA);
        });
    }

    [Fact]
    public async Task ApplyFlex_HamlibWithoutSmartSdr_SaysWhyAndChangesNothing()
    {
        await WithSetup(vm =>
        {
            vm.UseModelsForTests([Models[0]]);

            vm.ApplyFlex(Radio, null).Should().BeFalse();

            vm.FlexNote.Should().Contain("Hamlib 4.6 or newer");
            vm.Model.Should().BeNull();
        });
    }

    private static async Task WithSetup(Action<SetupViewModel> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-setupflex-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        SqliteDatabase? database = null;
        try
        {
            await using var host = await AppHost.StartAsync(TestHost.Args(dir));
            database = host.Database;
            var vm = new SetupViewModel(host);
            vm.UseModelsForTests(Models);
            test(vm);
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
