// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Wspr;

namespace Ft8Client.Core.Tests;

public class WsprBeaconLogicTests
{
    private static readonly DateTime Day = new(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime Slot(int hour, int minute) => Day.AddHours(hour).AddMinutes(minute);

    // The 60 slots of the 2-hour block that starts at hour h, as the schedule sees them.
    private static bool[] Block(WsprTxSchedule s, int hour, int percent) =>
        Enumerable.Range(0, 60).Select(i => s.IsTx(Slot(hour, 2 * i), percent)).ToArray();

    // --- presence

    [Fact]
    public void Expired_NoInputYet_IsExpired() => new WsprPresence().Expired(Day, 6).Should().BeTrue("a beacon that was never started has no operator");

    [Fact]
    public void Expired_WithinTheWatchdog_IsNot()
    {
        var p = new WsprPresence();
        p.Start(Day);
        p.Expired(Day.AddMinutes(5.9), 6).Should().BeFalse();
        p.Expired(Day.AddMinutes(6), 6).Should().BeTrue();
    }

    [Fact]
    public void Expired_ShortWatchdog_NeverLessThanOneSlot()
    {
        var p = new WsprPresence();
        p.Start(Day);
        p.Expired(Day.AddSeconds(119), 1).Should().BeFalse("the limit is 2 minutes even with a 1 minute watchdog");
        p.Expired(Day.AddMinutes(2), 1).Should().BeTrue();
        p.Expired(Day.AddMinutes(1.5), 0).Should().BeFalse();
        WsprPresence.Limit(1).Should().Be(TimeSpan.FromMinutes(2));
        WsprPresence.Limit(6).Should().Be(TimeSpan.FromMinutes(6));
    }

    [Fact]
    public void NoteOperatorInput_KeepsItRunning()
    {
        var p = new WsprPresence();
        p.Start(Day);
        p.NoteOperatorInput(Day.AddMinutes(5));
        p.Expired(Day.AddMinutes(10), 6).Should().BeFalse();
        p.Expired(Day.AddMinutes(11), 6).Should().BeTrue();
    }

    [Fact]
    public void NoteOperatorInput_OlderThanTheLast_DoesNotMoveItBack()
    {
        var p = new WsprPresence();
        p.Start(Day.AddMinutes(5));
        p.NoteOperatorInput(Day);
        p.LastInputUtc.Should().Be(Day.AddMinutes(5));
    }

    [Fact]
    public void Clear_ForgetsTheInput()
    {
        var p = new WsprPresence();
        p.Start(Day);
        p.Clear();
        p.LastInputUtc.Should().BeNull();
        p.Expired(Day, 6).Should().BeTrue();
    }

    [Fact]
    public void StoppedText_SaysTheTimeAndTheWatchdog() =>
        WsprPresence.StoppedText(6).Should().Be("Beacon stopped: no operator input for 6 minutes (Tx watchdog).");

    [Fact]
    public void StoppedText_ShortWatchdog_ShowsTheTwoMinuteFloor() =>
        WsprPresence.StoppedText(1).Should().Be("Beacon stopped: no operator input for 2 minutes (Tx watchdog).");

    // --- the slot table

    [Fact]
    public void IsTx_TwentyPercent_GapsAreThreeToFiveReceiveSlots()
    {
        var table = Block(new WsprTxSchedule(new Random(1)), 4, 20);

        var tx = Enumerable.Range(0, 60).Where(i => table[i]).ToList();
        tx.Should().NotBeEmpty();
        tx[0].Should().BeGreaterThanOrEqualTo(3, "the first transmission comes after at least 3 receive slots");
        for (var i = 1; i < tx.Count; i++) (tx[i] - tx[i - 1] - 1).Should().BeInRange(3, 5);
        table[0].Should().BeFalse("the first slot of a block never transmits");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(2026)]
    public void IsTx_TwentyPercentAnySeed_NeverTwoInARowAndAboutAFifth(int seed)
    {
        var s = new WsprTxSchedule(new Random(seed));
        var counts = 0;
        var total = 0;
        for (var hour = 0; hour < 24; hour += 2)
        {
            var table = Block(s, hour, 20);
            for (var i = 1; i < 60; i++) (table[i] && table[i - 1]).Should().BeFalse($"no two transmissions in a row (hour {hour}, slot {i})");
            counts += table.Count(t => t);
            total += 60;
        }
        (100.0 * counts / total).Should().BeInRange(17, 23, "20 % gives about a fifth of the slots");
    }

    [Fact]
    public void IsTx_OverAThousandSlots_ShareIsTwentyPercentPlusOrMinusThree()
    {
        var s = new WsprTxSchedule(new Random(42));
        var tx = 0;
        const int count = 1008; // 16.8 blocks of 60 slots
        for (var i = 0; i < count; i++)
            if (s.IsTx(Day.AddMinutes(2 * i), 20)) tx++;
        (100.0 * tx / count).Should().BeInRange(17, 23);
    }

    [Fact]
    public void IsTx_SameBlock_StaysTheSameTable()
    {
        var s = new WsprTxSchedule(new Random(5));
        Block(s, 6, 20).Should().Equal(Block(s, 6, 20), "the table is built once per block and percentage");
    }

    [Fact]
    public void IsTx_NextBlock_BuildsANewTable()
    {
        var s = new WsprTxSchedule(new Random(5));
        var a = Block(s, 6, 20);
        var b = Block(s, 8, 20);
        a.Should().NotEqual(b, "the next 2-hour block draws new gaps");
    }

    [Fact]
    public void IsTx_PercentChanges_RebuildsTheTable()
    {
        var s = new WsprTxSchedule(new Random(5));
        var five = Block(s, 6, 5).Count(t => t);
        var ten = Block(s, 6, 10).Count(t => t);
        five.Should().BeLessThan(ten);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(100, true)]
    [InlineData(150, true)]
    public void IsTx_ZeroOrFullPercent_IsNeverOrAlways(int percent, bool expected)
    {
        var s = new WsprTxSchedule(new Random(1));
        Enumerable.Range(0, 60).Select(i => s.IsTx(Slot(6, 2 * i), percent)).Should().OnlyContain(t => t == expected);
    }

    [Theory]
    [InlineData(30, 18, 1)]
    [InlineData(40, 24, 1)]
    [InlineData(50, 30, 2)]
    [InlineData(70, 42, 3)]
    public void IsTx_AboveQuarter_HitsTheShareAndNeverRunsPastTheLimit(int percent, int expectedSlots, int runLimit)
    {
        var s = new WsprTxSchedule(new Random(11));
        var table = Block(s, 10, percent);

        table.Count(t => t).Should().BeInRange(expectedSlots - 4, expectedSlots + 1);
        table[0].Should().BeFalse();
        var run = 0;
        foreach (var t in table)
        {
            run = t ? run + 1 : 0;
            run.Should().BeLessThanOrEqualTo(runLimit + 1);
        }
        // Every one of the 10 band columns of the 6 by 10 table has a transmit slot.
        for (var band = 0; band < 10; band++) Enumerable.Range(0, 6).Count(r => table[r * 10 + band]).Should().BeGreaterThanOrEqualTo(1, $"column {band}");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(19, 9)]
    [InlineData(20, 0)]
    [InlineData(41, 0)]
    public void HoppingIndex_Minute_IsTheTwoMinuteSlotInTwentyMinutes(int minute, int expected) =>
        WsprTxSchedule.HoppingIndex(Slot(3, 0).AddMinutes(minute)).Should().Be(expected);

    // --- scheduler

    private static readonly WsprSlotFacts On = new(BeaconOn: true, PresenceExpired: false, FirstSlotAfterHop: false, TransmitRefused: false, Percent: 100);

    [Fact]
    public void Decide_BeaconOff_Receives() =>
        new WsprBeaconScheduler(new WsprTxSchedule(new Random(1))).Decide(Slot(4, 0), On with { BeaconOn = false }).Transmit.Should().BeFalse();

    [Fact]
    public void Decide_PresenceExpired_Receives() =>
        new WsprBeaconScheduler(new WsprTxSchedule(new Random(1))).Decide(Slot(4, 0), On with { PresenceExpired = true }).Should().Match<WsprSlotDecision>(d => !d.Transmit && d.Reason.Contains("operator"));

    [Fact]
    public void Decide_FirstSlotAfterHop_NeverTransmitsEvenAtFullPercent() =>
        new WsprBeaconScheduler(new WsprTxSchedule(new Random(1))).Decide(Slot(4, 0), On with { FirstSlotAfterHop = true }).Transmit.Should().BeFalse();

    [Fact]
    public void Decide_GuardRefusedLastPlan_Receives() =>
        new WsprBeaconScheduler(new WsprTxSchedule(new Random(1))).Decide(Slot(4, 0), On with { TransmitRefused = true }).Transmit.Should().BeFalse();

    [Fact]
    public void Decide_FullPercentAndNothingAgainst_Transmits() =>
        new WsprBeaconScheduler(new WsprTxSchedule(new Random(1))).Decide(Slot(4, 0), On).Should().Be(WsprSlotDecision.Tx);

    [Fact]
    public void Decide_FollowsTheTable()
    {
        var table = Block(new WsprTxSchedule(new Random(9)), 8, 20);
        var scheduler = new WsprBeaconScheduler(new WsprTxSchedule(new Random(9)));
        for (var i = 0; i < 60; i++)
            scheduler.Decide(Slot(8, 2 * i), On with { Percent = 20 }).Transmit.Should().Be(table[i], $"slot {i}");
    }

    // --- message cycle

    [Fact]
    public void Next_PlainCallFourCharacterGrid_AlwaysType1()
    {
        var c = new WsprMessageCycle("W7LIT", "DN40", 37, preferType1: false);
        Enumerable.Range(0, 4).Select(_ => c.Next().Text).Should().OnlyContain(t => t == "W7LIT DN40 37");
        c.Messages.Should().HaveCount(1);
    }

    [Fact]
    public void Next_SixCharacterGrid_AlternatesType1ThenType3()
    {
        var c = new WsprMessageCycle("W7LIT", "DN40AB", 37, preferType1: false);
        Enumerable.Range(0, 4).Select(_ => c.Next().Text).Should().Equal("W7LIT DN40 37", "<W7LIT> DN40AB 37", "W7LIT DN40 37", "<W7LIT> DN40AB 37");
    }

    [Fact]
    public void Next_SixCharacterGridPreferType1_StaysType1() =>
        new WsprMessageCycle("W7LIT", "DN40AB", 37, preferType1: true).Messages.Select(m => m.Text).Should().Equal("W7LIT DN40 37");

    [Fact]
    public void Next_CompoundCall_AlternatesType2ThenType3()
    {
        var c = new WsprMessageCycle("PJ4/K1ABC", "FK52UD", 33, preferType1: true);
        Enumerable.Range(0, 3).Select(_ => c.Next().Text).Should().Equal("PJ4/K1ABC 33", "<PJ4/K1ABC> FK52UD 33", "PJ4/K1ABC 33");
    }

    [Fact]
    public void Messages_CompoundCallFourCharacterGrid_TheType3OneIsRefusedByThePacker()
    {
        var c = new WsprMessageCycle("PJ4/K1ABC", "FK52", 33, preferType1: false);

        c.Messages.Select(m => WsprEncoder.Encode(m).Ok).Should().Equal(true, false);
        WsprEncoder.Encode(c.Messages[1]).Refusal.Should().Contain("6-character locator");
    }
}
