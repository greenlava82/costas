// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using Ft8Client.Core.Transmit;

namespace Ft8Client.Core.Tests;

public class TxLevelTunerTests
{
    private static TxLevelStep Next(double gain, double alc, double? ceiling = null, int quiet = 0, double max = 0) =>
        TxLevelTuner.Next(new TxLevelState(gain, ceiling, quiet), alc, max);

    [Fact]
    public void Next_NoAlc_RaisesHalfDecibel()
    {
        var s = Next(-10, 0.0);
        s.Change.Should().Be(TxLevelChange.Raised);
        s.State.GainDb.Should().Be(-9.5);
    }

    [Fact]
    public void Next_AlcJustActing_Holds()
    {
        var s = Next(-10, 0.1, quiet: 3);
        s.Change.Should().Be(TxLevelChange.Held);
        s.State.Should().Be(new TxLevelState(-10));
    }

    [Theory]
    [InlineData(0.3, -11)]
    [InlineData(0.6, -13)]
    [InlineData(1.0, -16)]
    public void Next_TooMuchAlc_LowersFurtherTheHigherItRead(double alc, double expected)
    {
        var s = Next(-10, alc);
        s.Change.Should().Be(TxLevelChange.Lowered);
        s.State.GainDb.Should().Be(expected);
        s.State.CeilingDb.Should().Be(-10);
    }

    [Fact]
    public void Next_NoAlcBelowCeiling_StopsHalfDecibelUnderIt()
    {
        Next(-11, 0.0, ceiling: -10).State.GainDb.Should().Be(-10.5);
        var s = Next(-10.5, 0.0, ceiling: -10);
        s.Change.Should().Be(TxLevelChange.AtCeiling);
        s.State.Should().Be(new TxLevelState(-10.5, -10, 1));
    }

    [Fact]
    public void Next_QuietAtCeilingLongEnough_ForgetsCeilingSoItCanProbeAgain()
    {
        var s = Next(-10.5, 0.0, ceiling: -10, quiet: TxLevelTuner.RelaxAfter - 1);
        s.State.Should().Be(new TxLevelState(-10.5));
        Next(s.State.GainDb, 0.0).State.GainDb.Should().Be(-10);
    }

    [Fact]
    public void Next_NoAlcAtMaximum_StaysAndSaysSo()
    {
        Next(-0.8, 0.0, max: -0.5).State.GainDb.Should().Be(-0.5);
        var s = Next(-0.5, 0.0, max: -0.5);
        s.Change.Should().Be(TxLevelChange.AtMaximum);
        s.State.GainDb.Should().Be(-0.5);
    }

    [Fact]
    public void Next_MaximumAboveZero_NeverGoesAboveZeroDbfs()
    {
        Next(0, 0.0, max: 4).Change.Should().Be(TxLevelChange.AtMaximum);
    }

    [Fact]
    public void Next_HighAlcAtFloor_StaysAtFloor()
    {
        Next(-39, 1.0).State.GainDb.Should().Be(TxLevelTuner.FloorDb);
    }

    [Fact]
    public void Next_HighAlcAtLowestAllowed_StaysThere()
    {
        var s = TxLevelTuner.Next(new TxLevelState(-27), 1.0, 0, minDb: -30);
        s.State.GainDb.Should().Be(-30);
        TxLevelTuner.Next(s.State, 1.0, 0, minDb: -30).State.GainDb.Should().Be(-30);
    }

    [Fact]
    public void Next_LowestBelowFloor_StillStopsAtFloor()
    {
        TxLevelTuner.Next(new TxLevelState(-39), 1.0, 0, minDb: -60).State.GainDb.Should().Be(TxLevelTuner.FloorDb);
    }

    [Fact]
    public void Next_RadioWhoseKneeIsAtMinusEight_SettlesWithoutSwinging()
    {
        // ALC acts above -8 dB: rises from -12 to the knee, overshoots once, then stays put.
        static double Alc(double g) => g > -8 ? 0.3 + (g + 8) * 0.2 : g > -8.6 ? 0.1 : 0.0;
        var state = new TxLevelState(-12);
        var levels = new List<double>();
        for (var i = 0; i < 30; i++)
        {
            state = TxLevelTuner.Next(state, Alc(state.GainDb), 0).State;
            levels.Add(state.GainDb);
        }
        levels.TakeLast(TxLevelTuner.RelaxAfter - 1).Should().AllBeEquivalentTo(levels[^1]);
        Alc(levels[^1]).Should().BeInRange(TxLevelTuner.Quiet, TxLevelTuner.High);
    }

    [Fact]
    public void Next_ZeroAlcSmallReading_Lowers()
    {
        var step = TxLevelTuner.Next(new TxLevelState(-10), 1.0 / 64, 0, zeroAlc: true);
        step.Change.Should().Be(TxLevelChange.Lowered);
        step.State.GainDb.Should().Be(-11);
    }

    [Fact]
    public void Next_ZeroAlcRadioWithAlcAtLowLevels_SettlesJustBelowAlc()
    {
        static double Alc(double g) => g > -9 ? 0.05 + (g + 9) * 0.1 : 0.0;
        var state = new TxLevelState(-6);
        var readings = new List<double>();
        for (var i = 0; i < 40; i++)
        {
            readings.Add(Alc(state.GainDb));
            state = TxLevelTuner.Next(state, readings[^1], 0, zeroAlc: true).State;
        }
        // After settling, only the occasional probe above the remembered ceiling touches ALC.
        readings.TakeLast(20).Count(a => a > 0).Should().BeLessThanOrEqualTo(2);
        state.GainDb.Should().BeInRange(-10.5, -9);
    }

    [Fact]
    public void Next_QuietFarBelowRiseTo_RisesStraightToIt()
    {
        var step = TxLevelTuner.Next(new TxLevelState(-26), 0.0, -4, zeroAlc: true, riseToDb: -15.5);
        step.Change.Should().Be(TxLevelChange.Raised);
        step.State.Should().Be(new TxLevelState(-15.5));
    }

    [Fact]
    public void Next_QuietJustBelowRiseTo_RisesToItNotPastIt()
    {
        TxLevelTuner.Next(new TxLevelState(-15.8), 0.0, -4, riseToDb: -15.5).State.GainDb.Should().Be(-15.3);
        TxLevelTuner.Next(new TxLevelState(-15.6), 0.0, -4, riseToDb: -15.0).State.GainDb.Should().Be(-15.0);
    }

    [Fact]
    public void Next_QuietAtOrAboveRiseTo_RisesHalfDecibel()
    {
        TxLevelTuner.Next(new TxLevelState(-15.5), 0.0, -4, riseToDb: -15.5).State.GainDb.Should().Be(-15);
        TxLevelTuner.Next(new TxLevelState(-12), 0.0, -4, riseToDb: -15.5).State.GainDb.Should().Be(-11.5);
    }

    [Fact]
    public void Next_RiseToAboveMaximum_StopsAtMaximum()
    {
        TxLevelTuner.Next(new TxLevelState(-20), 0.0, -8, riseToDb: -2).State.GainDb.Should().Be(-8);
        TxLevelTuner.Next(new TxLevelState(-20), 0.0, 3, riseToDb: 2).State.GainDb.Should().Be(0);
    }

    [Fact]
    public void Next_RiseToAboveCeiling_StopsHalfDecibelUnderCeiling()
    {
        var step = TxLevelTuner.Next(new TxLevelState(-26, -18), 0.0, -4, riseToDb: -15.5);
        step.State.Should().Be(new TxLevelState(-18.5, -18));
    }

    [Fact]
    public void Next_AlcAtRiseTo_LowersAndThenOnlyRisesHalfDecibelAtATime()
    {
        var lowered = TxLevelTuner.Next(new TxLevelState(-15.5), 0.02, -4, zeroAlc: true, riseToDb: -15.5);
        lowered.Change.Should().Be(TxLevelChange.Lowered);
        lowered.State.Should().Be(new TxLevelState(-16.5, -15.5, 0, AlcBelowRiseTo: true));

        // The ALC set a ceiling; once it is forgotten the level probes upward half a decibel at a time, never jumping.
        var state = lowered.State with { GainDb = -25 };
        state = TxLevelTuner.Next(state, 0.0, -4, zeroAlc: true, riseToDb: -15.5).State;
        state.GainDb.Should().Be(-24.5);
        state.AlcBelowRiseTo.Should().BeTrue();
    }

    [Fact]
    public void Next_CeilingForgottenAfterAlcBelowRiseTo_KeepsRisingSlowly()
    {
        var state = new TxLevelState(-16.5, -16, TxLevelTuner.RelaxAfter - 1, AlcBelowRiseTo: true);
        state = TxLevelTuner.Next(state, 0.0, -4, zeroAlc: true, riseToDb: -15.5).State;
        state.Should().Be(new TxLevelState(-16.5, null, 0, AlcBelowRiseTo: true));
        state = TxLevelTuner.Next(state, 0.0, -4, zeroAlc: true, riseToDb: -15.5).State;
        state.GainDb.Should().Be(-16);
    }

    [Fact]
    public void Next_AlcWellAboveRiseTo_StillRisesStraightToItLater()
    {
        // ALC acting above the level is no reason to distrust it.
        var state = TxLevelTuner.Next(new TxLevelState(-10), 0.02, -4, zeroAlc: true, riseToDb: -15.5).State;
        state.AlcBelowRiseTo.Should().BeFalse();
        state = TxLevelTuner.Next(state with { GainDb = -25 }, 0.0, -4, zeroAlc: true, riseToDb: -15.5).State;
        state.GainDb.Should().Be(-15.5);
    }

    [Fact]
    public void Next_AlcHeldBelowRiseTo_DoesNotJump()
    {
        var step = TxLevelTuner.Next(new TxLevelState(-26), 0.1, -4, riseToDb: -15.5);
        step.Change.Should().Be(TxLevelChange.Held);
        step.State.GainDb.Should().Be(-26);
    }
}
