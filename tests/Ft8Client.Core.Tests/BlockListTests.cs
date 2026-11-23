// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Blocking;

namespace Ft8Client.Core.Tests;

public sealed class BlockListTests
{
    [Fact]
    public void Create_TypedCall_NormalisesCallAndBand()
    {
        var e = BlockedCall.Create(" on4xyz/p ", "20 m", Mode.Ft8, out var error);

        error.Should().BeEmpty();
        e.Should().Be(new BlockedCall("ON4XYZ", "20m", Mode.Ft8));
    }

    [Fact]
    public void Create_NotACall_Refused()
    {
        BlockedCall.Create("JO31", null, null, out var error).Should().BeNull();
        error.Should().Contain("not a callsign");
        BlockedCall.Create("  ", null, null, out error).Should().BeNull();
        error.Should().Be("Type a callsign.");
    }

    [Fact]
    public void IsBlocked_BandEntry_OnlyThatBand()
    {
        var list = new BlockList([new BlockedCall("ON4XYZ", "20m", null)]);

        list.IsBlocked("ON4XYZ", "20m", Mode.Ft8).Should().BeTrue();
        list.IsBlocked("ON4XYZ", "20m", Mode.Ft4).Should().BeTrue();
        list.IsBlocked("ON4XYZ", "40m", Mode.Ft8).Should().BeFalse();
    }

    [Fact]
    public void IsBlocked_ModeEntry_OnlyThatMode()
    {
        var list = new BlockList([new BlockedCall("ON4XYZ", null, Mode.Ft8)]);

        list.IsBlocked("ON4XYZ", "40m", Mode.Ft8).Should().BeTrue();
        list.IsBlocked("ON4XYZ", "40m", Mode.Ft4).Should().BeFalse();
    }

    [Fact]
    public void IsBlocked_PortableOrHashedForm_Blocked()
    {
        var list = new BlockList([new BlockedCall("ON4XYZ", null, null)]);

        list.IsBlocked("ON4XYZ/P", "20m", Mode.Ft8).Should().BeTrue();
        list.IsBlocked("<ON4XYZ>", "20m", Mode.Ft8).Should().BeTrue();
        list.IsBlocked("on4xyz", "20m", Mode.Ft8).Should().BeTrue();
    }

    [Fact]
    public void IsBlocked_OtherCallOrNull_NotBlocked()
    {
        var list = new BlockList([new BlockedCall("ON4XYZ", null, null)]);

        list.IsBlocked("ON4XY", "20m", Mode.Ft8).Should().BeFalse();
        list.IsBlocked(null, "20m", Mode.Ft8).Should().BeFalse();
        BlockList.Empty.IsBlocked("ON4XYZ", "20m", Mode.Ft8).Should().BeFalse();
    }

    [Fact]
    public void Describe_BandAndMode_InWords()
    {
        new BlockedCall("ON4XYZ", "20m", Mode.Ft8).Describe().Should().Be("ON4XYZ on 20 m in FT8");
        new BlockedCall("ON4XYZ", null, null).Describe().Should().Be("ON4XYZ on every band");
    }
}
