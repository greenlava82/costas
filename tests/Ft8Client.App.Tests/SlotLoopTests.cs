// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.Audio.Backends;
using Ft8Client.Audio.Timing;
using Ft8Client.Core;
using Ft8Client.Core.Time;
using Ft8Client.Data.Reference;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

public class SlotLoopTests
{
    private static readonly TxGuardInput Guard = new("20m", 14_074_000, null, 2.0, null, true);

    private sealed class Harness
    {
        public readonly ManualClock Clock = new(new DateTime(2026, 1, 4, 2, 54, 0, DateTimeKind.Utc));
        public readonly SimulatedRig Radio;
        public readonly Transmitter Tx;
        public readonly SlotLoop Loop;
        public readonly List<Exception> Logged = [];
        public long Ticks = 1_000; // Environment.TickCount64 is small soon after boot

        public Harness()
        {
            Radio = new SimulatedRig(Clock);
            Tx = new Transmitter(Radio, new NullAudioOutput { Now = () => Clock.UtcNow }, Clock, ReferenceData.LoadFrequencies());
            Loop = new SlotLoop(new SlotClock(Clock, Mode.Ft8), Tx, () => Ticks, Logged.Add);
        }
    }

    private static void Throw() => throw new InvalidOperationException("boom");

    [Fact]
    public void Guarded_FirstError_LogsAndHalts()
    {
        var h = new Harness();

        h.Loop.Guarded(Throw);

        h.Logged.Should().ContainSingle().Which.Message.Should().Be("boom");
        h.Tx.HaltEpoch.Should().Be(1);
    }

    [Fact]
    public void Guarded_SecondErrorWithin10s_NotLoggedAgain()
    {
        var h = new Harness();
        h.Loop.Guarded(Throw);

        h.Ticks += 5_000;
        h.Loop.Guarded(Throw);

        h.Logged.Should().ContainSingle();
    }

    [Fact]
    public void Guarded_ErrorAfter10s_LoggedAndHaltedAgain()
    {
        var h = new Harness();
        h.Loop.Guarded(Throw);

        h.Ticks += 10_000;
        h.Loop.Guarded(Throw);

        h.Logged.Should().HaveCount(2);
        h.Tx.HaltEpoch.Should().Be(2);
    }

    [Fact]
    public async Task Guarded_RepeatWithin10sWhileTransmitting_StillHalts()
    {
        var h = new Harness();
        h.Loop.Guarded(Throw);
        (await h.Tx.TuneAsync(1500, 5.0, Guard)).Should().BeNull();
        h.Radio.Ptt.Should().BeTrue();

        h.Ticks += 1_000;
        h.Loop.Guarded(Throw);

        h.Radio.Ptt.Should().BeFalse();
        h.Logged.Should().ContainSingle();
    }

    [Fact]
    public void Guarded_NoError_NeitherLogsNorHalts()
    {
        var h = new Harness();

        h.Loop.Guarded(() => { });

        h.Logged.Should().BeEmpty();
        h.Tx.HaltEpoch.Should().Be(0);
    }
}
