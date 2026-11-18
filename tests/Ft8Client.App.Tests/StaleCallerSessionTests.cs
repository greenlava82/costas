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
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Time;
using Ft8Client.Data.Reference;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

/// <summary>A click on a station that replied to me earlier but has since moved on to calling CQ starts afresh.</summary>
public class StaleCallerSessionTests
{
    // Even slots are ZL2RPA's; I send in the odd ones.
    private static readonly DateTime T0 = new(2026, 1, 4, 2, 54, 0, DateTimeKind.Utc);

    private static DateTime Slot(int n) => T0.AddSeconds(15 * n);

    [Fact]
    public async Task CallStation_StoppedStationNowCallingCq_SendsFreshCallAndCurrentReport()
    {
        var r = new Rig();
        r.Decoder.Script(Slot(0), ("CQ ZL2RPA RF70", -10));
        await r.RunUntilAsync(Slot(0).AddSeconds(14.8));
        r.Session.CallStation("ZL2RPA").Should().BeTrue();
        r.Decoder.Script(Slot(2), ("W7LIT ZL2RPA -22", -10));
        await r.RunUntilAsync(Slot(3).AddSeconds(14));
        r.Sent.Should().Equal("ZL2RPA W7LIT DN40", "ZL2RPA W7LIT R-10");
        await r.Session.AbandonAsync();

        // A late report puts it in Waiting for you; then it goes back to calling CQ, much stronger.
        r.Decoder.Script(Slot(4), ("W7LIT ZL2RPA -22", -10));
        r.Decoder.Script(Slot(6), ("CQ ZL2RPA RF70", 3));
        await r.RunUntilAsync(Slot(6).AddSeconds(14.8));
        r.Session.CallStation("ZL2RPA").Should().BeTrue();
        r.Decoder.Script(Slot(8), ("W7LIT ZL2RPA -15", 3));
        await r.RunUntilAsync(Slot(9).AddSeconds(14));

        r.Sent.Skip(2).Should().Equal(["ZL2RPA W7LIT DN40", "ZL2RPA W7LIT R+03"],
            "the stale R-10 is not resumed: the contact starts again from its CQ with the report from that CQ");
    }

    private sealed class Rig
    {
        public readonly ManualClock Clock = new(T0.AddSeconds(-1));
        public readonly ScriptDecoder Decoder = new();
        public readonly Transmitter Tx;
        public readonly Session Session;
        public readonly SlotClock Slots;
        public readonly List<string> Sent = [];

        public Rig()
        {
            var freqs = ReferenceData.LoadFrequencies();
            Tx = new Transmitter(new SimulatedRig(Clock), new NullAudioOutput { Now = () => Clock.UtcNow }, Clock, freqs);
            var config = new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", FixedTxOffsetHz = 1500 };
            Session = new Session(config, Decoder, new SilentSource(), Tx, Clock, ReferenceData.LoadCountries(), freqs);
            Session.Transmitting += (tx, _) => Sent.Add(tx.Plan.Message);
            Slots = new SlotClock(Clock, Mode.Ft8);
            Slots.Event += Session.OnSlotEvent;
            Slots.Poll();
        }

        public async Task RunUntilAsync(DateTime end)
        {
            while (Clock.UtcNow < end)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(10));
                Slots.Poll();
                Tx.Poll();
                await Session.DecodeIdle;
            }
        }
    }

    private sealed class ScriptDecoder : IDecoder
    {
        private readonly Dictionary<DateTime, (string Text, int Snr)[]> _script = [];

        public void Script(DateTime slot, params (string Text, int Snr)[] lines)
        {
            lock (_script) _script[slot] = lines;
        }

        public Task<DecodeResult> DecodeAsync(SlotAudio slot, DecodeContext ctx, CancellationToken ct)
        {
            (string Text, int Snr)[] lines;
            lock (_script) lines = _script.GetValueOrDefault(slot.SlotStartUtc) ?? [];
            var decodes = lines.Select((l, i) => new Decode(slot.SlotStartUtc, l.Snr, 0.1, 800 + 300 * i, l.Text, false)).ToList();
            return Task.FromResult(new DecodeResult(decodes, TimeSpan.FromSeconds(0.1), DecodeStatus.Ok));
        }
    }

    private sealed class SilentSource : ISlotSource
    {
        public void BeginSlot(DateTime slotStartUtc, Mode mode)
        {
        }

        public void CancelSlot()
        {
        }

        public SlotAudio? Cutoff(DateTime slotStartUtc, Mode mode) => new(slotStartUtc, mode, new short[16]);
    }
}
