// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Decoding;

namespace Ft8Client.Core.Tests;

public class InputLevelTrendTests
{
    private static void Feed(InputLevelTrend t, int seconds, InputLevelState state, double db)
    {
        for (var i = 0; i < seconds; i++) t.Add(state, db);
    }

    [Fact]
    public void Add_SevenOfTenAgree_Settles()
    {
        var t = new InputLevelTrend();
        Feed(t, 6, InputLevelState.TooQuiet, -50);
        t.State.Should().BeNull();
        t.Add(InputLevelState.TooQuiet, -50).Should().BeTrue();
        t.State.Should().Be(InputLevelState.TooQuiet);
        t.RmsDbfs.Should().Be(-50);
    }

    [Fact]
    public void Add_OneLoudCrash_StaysGood()
    {
        var t = new InputLevelTrend();
        Feed(t, 9, InputLevelState.Good, -25);
        t.Add(InputLevelState.Clipping, -5);
        t.State.Should().Be(InputLevelState.Good);
    }

    [Fact]
    public void Add_MixedReadings_Unsettled()
    {
        var t = new InputLevelTrend();
        Feed(t, 5, InputLevelState.Good, -38);
        Feed(t, 5, InputLevelState.TooQuiet, -42);
        t.State.Should().BeNull();
        t.RmsDbfs.Should().Be(-40);
    }

    [Fact]
    public void Add_OldReadingsLeaveTheWindow()
    {
        var t = new InputLevelTrend();
        Feed(t, 10, InputLevelState.Silence, -120);
        Feed(t, 7, InputLevelState.Good, -25);
        t.State.Should().Be(InputLevelState.Good);
    }

    [Fact]
    public void Clear_Settled_ForgetsIt()
    {
        var t = new InputLevelTrend();
        Feed(t, 10, InputLevelState.NoAudio, -120);
        t.Clear().Should().BeTrue();
        t.State.Should().BeNull();
        t.RmsDbfs.Should().BeNull();
    }
}
