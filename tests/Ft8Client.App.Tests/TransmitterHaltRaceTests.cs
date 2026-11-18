// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.Audio.Backends;
using Ft8Client.Audio.Timing;
using Ft8Client.Core;
using Ft8Client.Core.Contacts;
using Ft8Client.Core.Time;
using Ft8Client.Data.Reference;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

// Halt must win every race with keying that is already on its way: simulated rig and null audio only.
public class TransmitterHaltRaceTests
{
    private static readonly DateTime Slot = new(2026, 1, 4, 2, 54, 0, DateTimeKind.Utc);
    private static readonly TxPlan Cq = new(Slot, "CQ W7LIT DN40", null, null);
    private static readonly TxGuardInput Guard = new("20m", 14_074_000, null, 2.0, null, true);

    private sealed class Rig
    {
        public readonly ManualClock Clock = new(Slot.AddSeconds(-1));
        public readonly SimulatedRig Radio;
        public readonly NullAudioOutput Out;
        public readonly Transmitter Tx;
        public readonly SlotClock Slots;

        public Rig()
        {
            Radio = new SimulatedRig(Clock);
            Out = new NullAudioOutput { Now = () => Clock.UtcNow };
            Tx = new Transmitter(Radio, Out, Clock, ReferenceData.LoadFrequencies());
            Slots = new SlotClock(Clock, Mode.Ft8);
            Slots.Event += Tx.OnSlotEvent;
            Slots.Poll();
        }

        public void Run(TimeSpan d)
        {
            var end = Clock.UtcNow + d;
            while (Clock.UtcNow < end)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(1));
                Slots.Poll();
                Tx.Poll();
            }
        }
    }

    [Fact]
    public async Task BeginLate_HaltAfterPrepare_KeysNothing()
    {
        var r = new Rig();
        r.Clock.Advance(TimeSpan.FromSeconds(1.5)); // past this slot's PTT mark, as when decodes arrive late
        var tx = r.Tx.Prepare(Cq, Mode.Ft8, 1500, Guard, out _);
        tx.Should().NotBeNull();

        await r.Tx.HaltAsync();
        r.Tx.BeginLate(tx!, TimeSpan.FromMilliseconds(200)).Should().BeFalse();
        r.Run(TimeSpan.FromSeconds(2));

        r.Radio.PttLog.Should().BeEmpty();
        r.Out.Played.Should().BeEmpty();
        r.Tx.Transmitting.Should().BeFalse();
    }

    [Fact]
    public async Task Prepare_HaltSinceTheCallerDecided_InstallsNothingWithNoRefusal()
    {
        var r = new Rig();
        var epoch = r.Tx.HaltEpoch; // read where the caller decides to transmit

        await r.Tx.HaltAsync();
        var tx = r.Tx.Prepare(Cq, Mode.Ft8, 1500, Guard, out var refusal, haltEpoch: epoch);
        r.Run(TimeSpan.FromSeconds(3));

        tx.Should().BeNull();
        refusal.Should().BeNull();
        r.Tx.Pending.Should().BeNull();
        r.Radio.PttLog.Should().BeEmpty();
        r.Out.Played.Should().BeEmpty();
    }

    [Fact]
    public void Prepare_NoHaltSinceTheCallerDecided_TransmitsAsBefore()
    {
        var r = new Rig();
        var epoch = r.Tx.HaltEpoch;

        r.Tx.Prepare(Cq, Mode.Ft8, 1500, Guard, out var refusal, haltEpoch: epoch).Should().NotBeNull();
        refusal.Should().BeNull();
        r.Run(TimeSpan.FromSeconds(2));

        r.Radio.Ptt.Should().BeTrue();
        r.Out.Played.Should().ContainSingle();
    }

    [Fact]
    public void OnSlotEvent_HaltBetweenCaptureAndPttOn_PttStaysOff()
    {
        var r = new Rig();
        r.Tx.Prepare(Cq, Mode.Ft8, 1500, Guard, out _).Should().NotBeNull();
        var halted = false;
        r.Tx.SlotEventCaptured = e =>
        {
            if (halted || e.Mark != SlotMark.PttOn) return;
            halted = true;
            r.Tx.HaltAsync().GetAwaiter().GetResult();
        };

        r.Run(TimeSpan.FromSeconds(3));

        halted.Should().BeTrue();
        r.Radio.Ptt.Should().BeFalse();
        r.Radio.PttLog.Should().BeEmpty();
        r.Out.Played.Should().BeEmpty();
        r.Tx.Transmitting.Should().BeFalse();
    }

    [Fact]
    public void OnSlotEvent_HaltBetweenCaptureAndTxStart_PlaysNothing()
    {
        var r = new Rig();
        r.Tx.Prepare(Cq, Mode.Ft8, 1500, Guard, out _).Should().NotBeNull();
        r.Run(TimeSpan.FromSeconds(1.35)); // PTT keyed at its mark; the Tx start mark is next
        r.Radio.Ptt.Should().BeTrue();
        var halted = false;
        r.Tx.SlotEventCaptured = e =>
        {
            if (halted || e.Mark != SlotMark.TxStart) return;
            halted = true;
            r.Tx.HaltAsync().GetAwaiter().GetResult();
        };

        r.Run(TimeSpan.FromSeconds(1));

        halted.Should().BeTrue();
        r.Radio.Ptt.Should().BeFalse();
        r.Out.Played.Should().BeEmpty();
        r.Tx.Transmitting.Should().BeFalse();
    }

    [Fact]
    public async Task KeyOn_HaltBeforeTheRadioAnswers_PttReleasedOnceItKeys()
    {
        var r = new Rig();
        var answer = new TaskCompletionSource();
        r.Radio.PttOnGate = answer.Task; // the PTT-on is still on its way when Halt is pressed
        r.Tx.Prepare(Cq, Mode.Ft8, 1500, Guard, out _).Should().NotBeNull();
        r.Run(TimeSpan.FromSeconds(1.35));

        await r.Tx.HaltAsync();
        answer.SetResult(); // the radio now carries out the PTT-on that was sent before the Halt
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (r.Radio.Ptt && DateTime.UtcNow < deadline) await Task.Delay(5, TestContext.Current.CancellationToken);

        r.Radio.Ptt.Should().BeFalse();
        r.Out.Played.Should().BeEmpty();
        r.Tx.Transmitting.Should().BeFalse();
    }

    [Fact]
    public async Task HaltForNextAsync_CurrentEpoch_LetsTheNextTransmissionThrough()
    {
        var r = new Rig();
        (await r.Tx.TuneAsync(1500, 5.0, Guard)).Should().BeNull();
        var epoch = r.Tx.HaltEpoch;

        await r.Tx.HaltForNextAsync(ref epoch);
        r.Tx.Prepare(Cq, Mode.Ft8, 1500, Guard, out var refusal, haltEpoch: epoch).Should().NotBeNull();
        refusal.Should().BeNull();
    }

    [Fact]
    public async Task HaltForNextAsync_AnotherHaltCameFirst_NextTransmissionRefused()
    {
        var r = new Rig();
        var epoch = r.Tx.HaltEpoch;

        await r.Tx.HaltAsync(); // the operator's Halt
        await r.Tx.HaltForNextAsync(ref epoch);
        r.Tx.Prepare(Cq, Mode.Ft8, 1500, Guard, out _, haltEpoch: epoch).Should().BeNull();
        r.Tx.Pending.Should().BeNull();
    }
}
