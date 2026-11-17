// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Spectrum;

namespace Ft8Client.Core.Tests;

public class NoiseFloorTrackerTests
{
    private static float[] Row(float noise, int bins = 100) => Enumerable.Repeat(noise, bins).ToArray();

    [Fact]
    public void Update_FirstRow_TakesItsFloorAtOnce()
    {
        var tracker = new NoiseFloorTracker();
        tracker.Update(Row(-70), 0, 99).Should().Be(-70);
    }

    [Fact]
    public void Update_BusyBand_StaysOnTheNoise()
    {
        // Signals 30 dB up in a quarter of the bins must not lift the floor.
        var row = Row(-70);
        for (var i = 0; i < 25; i++) row[i * 4] = -40;
        new NoiseFloorTracker().Update(row, 0, 99).Should().Be(-70);
    }

    [Fact]
    public void Update_OnlyTheGivenRange_IsUsed()
    {
        // Bins outside the receive range (below 200 Hz, past the filter) are quieter and are ignored.
        var row = Row(-100);
        for (var i = 20; i <= 80; i++) row[i] = -60;
        new NoiseFloorTracker().Update(row, 20, 80).Should().Be(-60);
    }

    [Fact]
    public void Update_LevelStep_FollowsGradually()
    {
        var tracker = new NoiseFloorTracker();
        tracker.Update(Row(-70), 0, 99);
        var next = tracker.Update(Row(-50), 0, 99);
        next.Should().BeApproximately(-68, 0.001);
        for (var i = 0; i < 60; i++) next = tracker.Update(Row(-50), 0, 99);
        next.Should().BeApproximately(-50, 0.1);
    }

    [Fact]
    public void Reset_AfterRows_StartsAgainFromTheNextRow()
    {
        var tracker = new NoiseFloorTracker();
        tracker.Update(Row(-70), 0, 99);
        tracker.Reset();
        tracker.Floor.Should().BeNull();
        tracker.Update(Row(-40), 0, 99).Should().Be(-40);
    }

    [Fact]
    public void Update_RangeOutsideTheRow_IsClamped()
    {
        new NoiseFloorTracker().Update(Row(-70, 10), -5, 500).Should().Be(-70);
    }
}
