// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Audio.Timing;
using Ft8Client.Core;
using Ft8Client.Core.Time;

namespace Ft8Client.Audio.Tests;

public class SlotClockWsprTests
{
    private static DateTime T(int h, int m, double s) => new DateTime(2026, 1, 4, h, m, 0, DateTimeKind.Utc).AddSeconds(s);

    private static (SlotClock Clock, ManualClock Time, List<SlotEvent> Events) Make(DateTime start)
    {
        var time = new ManualClock(start);
        var clock = new SlotClock(time, Mode.Wspr);
        var events = new List<SlotEvent>();
        clock.Event += events.Add;
        clock.Poll();
        return (clock, time, events);
    }

    private static void Run(SlotClock c, ManualClock t, TimeSpan duration, int stepMs = 250)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += TimeSpan.FromMilliseconds(stepMs))
        {
            t.Advance(TimeSpan.FromMilliseconds(stepMs));
            c.Poll();
        }
    }

    [Fact]
    public void Constructor_Wspr_KeepsWspr() => new SlotClock(new ManualClock(T(2, 0, 0)), Mode.Wspr).Mode.Should().Be(Mode.Wspr);

    [Fact]
    public void Constructor_KeyboardMode_FallsBackToFt8() =>
        new SlotClock(new ManualClock(T(2, 0, 0)), Mode.Psk31).Mode.Should().Be(Mode.Ft8);

    [Fact]
    public void OffsetOf_Wspr_FollowsWsprTiming()
    {
        var c = new SlotClock(new ManualClock(T(2, 0, 0)), Mode.Wspr);
        c.OffsetOf(SlotMark.Start, Mode.Wspr).Should().Be(TimeSpan.Zero);
        c.OffsetOf(SlotMark.PttOn, Mode.Wspr).Should().Be(TimeSpan.FromMilliseconds(800));
        c.OffsetOf(SlotMark.TxStart, Mode.Wspr).Should().Be(TimeSpan.FromSeconds(1));
        c.OffsetOf(SlotMark.EarlyDecode, Mode.Wspr).Should().Be(c.OffsetOf(SlotMark.CaptureCutoff, Mode.Wspr), "WSPR has no early pass");
        c.OffsetOf(SlotMark.CaptureCutoff, Mode.Wspr).Should().Be(TimeSpan.FromMilliseconds(114_048));
    }

    [Fact]
    public void Poll_JoinMidSlot_SkipsPartialThenAnnouncesEvenMinuteSlots()
    {
        var (c, t, ev) = Make(T(2, 1, 20));
        Run(c, t, TimeSpan.FromMinutes(5));
        ev.Where(e => e.Mark == SlotMark.Start).Select(e => e.SlotStartUtc).Should().Equal(T(2, 2, 0), T(2, 4, 0), T(2, 6, 0));
        ev.Should().OnlyContain(e => e.SlotStartUtc.Second == 0 && e.SlotStartUtc.Minute % 2 == 0);
    }

    [Fact]
    public void Poll_WholeSlot_RaisesMarksInOrderAtTheirOffsets()
    {
        var (c, t, ev) = Make(T(2, 1, 59.5));
        Run(c, t, TimeSpan.FromSeconds(125));
        var slot = ev.Where(e => e.SlotStartUtc == T(2, 2, 0)).ToList();
        slot.Select(e => e.Mark).Should().Equal(SlotMark.Start, SlotMark.PttOn, SlotMark.TxStart, SlotMark.EarlyDecode, SlotMark.CaptureCutoff);
        slot.Should().OnlyContain(e => e.Mode == Mode.Wspr);
        slot.Should().OnlyContain(e => e.LateBy < TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void Poll_SystemTimeJumpsBackMidSlot_NoDoubleSlot()
    {
        var (c, t, ev) = Make(T(2, 1, 59.5));
        Run(c, t, TimeSpan.FromSeconds(30));
        t.JumpWallClock(TimeSpan.FromSeconds(-20));
        Run(c, t, TimeSpan.FromMinutes(5));
        var starts = ev.Where(e => e.Mark == SlotMark.Start).Select(e => e.SlotStartUtc).ToList();
        starts.Should().OnlyHaveUniqueItems();
        starts.Should().BeInAscendingOrder();
        ev.Count(e => e.Mark == SlotMark.CaptureCutoff && e.SlotStartUtc == T(2, 2, 0)).Should().Be(1);
    }

    [Fact]
    public void SetMode_FtThenWspr_ChangesAtNextBoundary()
    {
        var time = new ManualClock(T(2, 0, 7));
        var clock = new SlotClock(time, Mode.Ft8);
        clock.Poll();
        clock.SetMode(Mode.Wspr);
        clock.Mode.Should().Be(Mode.Ft8, "the change waits for the slot boundary");
        Run(clock, time, TimeSpan.FromSeconds(10));
        clock.Mode.Should().Be(Mode.Wspr);
    }

    [Fact]
    public void SetMode_KeyboardFromWspr_FallsBackToFt8AtBoundary()
    {
        var time = new ManualClock(T(2, 0, 7));
        var clock = new SlotClock(time, Mode.Wspr);
        clock.Poll();
        clock.SetMode(Mode.Rtty);
        Run(clock, time, TimeSpan.FromMinutes(2));
        clock.Mode.Should().Be(Mode.Ft8, "WSPR's 2-minute marks make no sense for a keyboard mode");
    }

    [Fact]
    public void SetMode_Keyboard_IsIgnored()
    {
        var clock = new SlotClock(new ManualClock(T(2, 0, 0)), Mode.Wspr);
        clock.SetMode(Mode.Rtty);
        clock.Mode.Should().Be(Mode.Wspr);
    }
}

public class SlotRecorderWsprTests
{
    [Fact]
    public void Cutoff_Wspr_IsWholeSlotLong()
    {
        var r = new SlotRecorder();
        var start = new DateTime(2026, 1, 4, 2, 2, 0, DateTimeKind.Utc);
        r.BeginSlot(start, Mode.Wspr);
        var chunk = new float[12_000];
        for (var i = 0; i < 114; i++) r.Append(chunk);
        var slot = r.Cutoff();
        slot.Should().NotBeNull();
        slot!.Mode.Should().Be(Mode.Wspr);
        slot.SlotStartUtc.Should().Be(start);
        slot.Samples12k.Length.Should().Be(1_440_000);
    }

    [Fact]
    public void Cutoff_Ft8AfterWspr_StillDecoderLength()
    {
        var r = new SlotRecorder();
        r.BeginSlot(new DateTime(2026, 1, 4, 2, 2, 0, DateTimeKind.Utc), Mode.Wspr);
        r.Cutoff();
        r.BeginSlot(new DateTime(2026, 1, 4, 2, 2, 0, DateTimeKind.Utc), Mode.Ft8);
        r.Cutoff()!.Samples12k.Length.Should().Be(180_000);
    }

    [Fact]
    public void Append_PastTheSlot_IsClippedNotThrown()
    {
        var r = new SlotRecorder();
        r.BeginSlot(new DateTime(2026, 1, 4, 2, 2, 0, DateTimeKind.Utc), Mode.Wspr);
        var chunk = new float[12_000];
        for (var i = 0; i < 140; i++) r.Append(chunk);
        r.Cutoff()!.Samples12k.Length.Should().Be(1_440_000);
    }
}
