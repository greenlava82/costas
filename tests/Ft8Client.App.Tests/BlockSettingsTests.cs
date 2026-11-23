// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.App.Tests;

public class BlockSettingsTests
{
    [Fact]
    public void BlockEntry_StoredAndRead_SameEntry()
    {
        var b = new Ft8Client.Core.Blocking.BlockedCall("ON4XYZ", "20m", Ft8Client.Core.Mode.Ft8);
        var stored = Services.SettingsMapper.ToEntry(b);

        stored.Mode.Should().Be("FT8");
        Services.SettingsMapper.ToBlocked(stored).Should().Be(b);
        Services.SettingsMapper.ToBlocked(new Ft8Client.Data.Settings.BlockEntry { Call = "JO31" }).Should().BeNull();
    }
}
