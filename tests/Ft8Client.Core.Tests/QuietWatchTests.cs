// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Decoding;

namespace Ft8Client.Core.Tests;

public class QuietWatchTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 19, 0, 0, DateTimeKind.Utc);

    private static DateTime Slot(int n) => T0.AddSeconds(15 * n);

    [Fact]
    public void Slot_TwoEmptySlots_Quiet()
    {
        var w = new QuietWatch(T0);
        w.Slot(Slot(0), 0);
        w.Quiet.Should().BeFalse();
        w.Slot(Slot(1), 0);
        w.Quiet.Should().BeTrue();
        w.EmptySlots.Should().Be(2);
    }

    [Fact]
    public void Slot_ADecode_StartsTheCountAgain()
    {
        var w = new QuietWatch(T0);
        w.Slot(Slot(0), 0);
        w.Slot(Slot(1), 0);
        w.Slot(Slot(2), 3);
        w.Quiet.Should().BeFalse();
        w.EmptySlots.Should().Be(0);
    }

    [Fact]
    public void Slot_BeganBeforeTheWatchStarted_NotCounted()
    {
        var w = new QuietWatch(T0.AddSeconds(4));
        w.Slot(Slot(0), 0);
        w.Slot(Slot(1), 0);
        w.EmptySlots.Should().Be(1);
    }

    [Fact]
    public void Slot_ITransmittedInIt_NotCounted()
    {
        var w = new QuietWatch(T0);
        w.Slot(Slot(0), 0);
        w.Transmitted(Slot(1));
        w.Slot(Slot(1), 0);
        w.Quiet.Should().BeFalse();
        w.Slot(Slot(2), 0);
        w.Quiet.Should().BeTrue();
    }

    [Fact]
    public void Slot_SameSlotTwice_CountedOnce()
    {
        var w = new QuietWatch(T0);
        w.Slot(Slot(0), 0);
        w.Slot(Slot(0), 0);
        w.EmptySlots.Should().Be(1);
    }

    [Fact]
    public void Slot_EarlyPassEmptyThenFullDecodeHears_NotQuiet()
    {
        var w = new QuietWatch(T0);
        w.Slot(Slot(0), 0);
        w.Slot(Slot(1), 0);
        w.Slot(Slot(1), 2);
        w.Quiet.Should().BeFalse();
    }

    [Fact]
    public void Reset_AfterQuiet_CountsFromTheChange()
    {
        var w = new QuietWatch(T0);
        w.Slot(Slot(0), 0);
        w.Slot(Slot(1), 0);
        w.Reset(Slot(2).AddSeconds(3));
        w.Quiet.Should().BeFalse();
        w.Slot(Slot(2), 0); // recorded across the change
        w.EmptySlots.Should().Be(0);
        w.Slot(Slot(3), 0);
        w.Slot(Slot(4), 0);
        w.Quiet.Should().BeTrue();
    }
}
