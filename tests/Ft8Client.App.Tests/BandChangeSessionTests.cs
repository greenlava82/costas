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

/// <summary>A slot recorded across a band change is not filed under the new band (evaluation B7).</summary>
public class BandChangeSessionTests
{
    private static readonly DateTime T0 = new(2026, 1, 4, 2, 54, 0, DateTimeKind.Utc);

    private static DateTime Slot(int n) => T0.AddSeconds(15 * n);

    private sealed class Rig
    {
        public readonly ManualClock Clock = new(T0.AddSeconds(-1));
        public readonly ScriptDecoder Decoder = new();
        public readonly Transmitter Tx;
        public readonly Session Session;
        public readonly SlotClock Slots;
        public readonly List<string> Sent = [];
        public readonly List<DateTime> Decoded = [];

        public Rig()
        {
            var freqs = ReferenceData.LoadFrequencies();
            Tx = new Transmitter(new SimulatedRig(Clock), new NullAudioOutput { Now = () => Clock.UtcNow }, Clock, freqs);
            var config = new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", FixedTxOffsetHz = 1500 };
            Session = new Session(config, Decoder, new SilentSource(), Tx, Clock, ReferenceData.LoadCountries(), freqs);
            Session.Transmitting += (tx, _) => Sent.Add(tx.Plan.Message);
            Session.SlotDecoded += (slot, _, _) => Decoded.Add(slot);
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

    [Fact]
    public async Task ApplyDecodes_BandChangedDuringSlot_SlotDropped()
    {
        var r = new Rig();
        r.Decoder.Script(Slot(0), "CQ ZL2RPA RF70");
        r.Decoder.Script(Slot(1), "CQ VK3XYZ QF22");
        await r.RunUntilAsync(Slot(0).AddSeconds(10));
        r.Session.SetBand("40m", Mode.Ft8);
        await r.RunUntilAsync(Slot(1).AddSeconds(14.8));

        r.Session.Snapshot.Rank.Line.Should().NotContain(x => x.Station.Call == "ZL2RPA", "it was heard on 20 m");
        r.Session.Snapshot.Rank.Line.Should().Contain(x => x.Station.Call == "VK3XYZ");
        r.Decoded.Should().Equal([Slot(1)], "PSK Reporter and the band statistics never hear of the mixed slot");
    }

    [Fact]
    public async Task ApplyDecodes_DialMovedFarWithinBand_SlotDropped()
    {
        var r = new Rig();
        r.Decoder.Script(Slot(0), "CQ ZL2RPA RF70");
        await r.RunUntilAsync(Slot(0).AddSeconds(10));
        r.Session.SetDial(14_080_000);
        await r.RunUntilAsync(Slot(0).AddSeconds(14.8));

        r.Decoded.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyDecodes_DialNudged_SlotKept()
    {
        var r = new Rig();
        r.Decoder.Script(Slot(0), "CQ ZL2RPA RF70");
        await r.RunUntilAsync(Slot(0).AddSeconds(10));
        r.Session.SetDial(14_074_500);
        await r.RunUntilAsync(Slot(0).AddSeconds(14.8));

        r.Decoded.Should().Equal(Slot(0));
    }

    private sealed class ScriptDecoder : IDecoder
    {
        private readonly Dictionary<DateTime, string[]> _script = [];

        public void Script(DateTime slot, params string[] lines)
        {
            lock (_script) _script[slot] = lines;
        }

        public Task<DecodeResult> DecodeAsync(SlotAudio slot, DecodeContext ctx, CancellationToken ct)
        {
            string[] lines;
            lock (_script) lines = _script.GetValueOrDefault(slot.SlotStartUtc) ?? [];
            var decodes = lines.Select((l, i) => new Decode(slot.SlotStartUtc, -10, 0.1, 800 + 300 * i, l, false)).ToList();
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
