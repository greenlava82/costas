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
using Ft8Client.Core.Blocking;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Time;
using Ft8Client.Data.Reference;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

/// <summary>The block list: a blocked call is not listed and never answered on its band; elsewhere it is.</summary>
public class BlockListSessionTests
{
    // I call CQ in the even slots; callers answer in the odd ones.
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

        public Rig(BlockList blocked)
        {
            var freqs = ReferenceData.LoadFrequencies();
            Tx = new Transmitter(new SimulatedRig(Clock), new NullAudioOutput { Now = () => Clock.UtcNow }, Clock, freqs);
            var config = new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", FixedTxOffsetHz = 1500, Blocked = blocked };
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

    [Fact]
    public async Task CallingCq_BlockedCallerOnThisBand_NotAnsweredOrListedButInRawDecodes()
    {
        var r = new Rig(new BlockList([new BlockedCall("ON4XYZ", "20m", null)]));
        r.Session.ToggleCq();
        r.Decoder.Script(Slot(1), "W7LIT ON4XYZ/P JO20");
        await r.RunUntilAsync(Slot(2).AddSeconds(14));

        r.Sent.Should().Equal("CQ W7LIT DN40", "CQ W7LIT DN40");
        r.Session.Snapshot.Waiting.Should().BeEmpty();
        r.Session.Snapshot.Stations.Should().NotContain(s => s.Call.StartsWith("ON4XYZ", StringComparison.Ordinal));
        r.Session.Snapshot.RawDecodes.Should().Contain(l => l.Decode.Text == "W7LIT ON4XYZ/P JO20");
        r.Session.Snapshot.UnansweredCqs.Should().Be(2);
    }

    [Fact]
    public async Task CallingCq_BlockedOnAnotherBand_Answered()
    {
        var r = new Rig(new BlockList([new BlockedCall("ON4XYZ", "40m", null)]));
        r.Session.ToggleCq();
        r.Decoder.Script(Slot(1), "W7LIT ON4XYZ JO20");
        await r.RunUntilAsync(Slot(2).AddSeconds(14));

        r.Sent.Should().Equal("CQ W7LIT DN40", "ON4XYZ W7LIT -10");
        r.Session.Snapshot.UnansweredCqs.Should().Be(0);
    }

    [Fact]
    public async Task Reconfigure_CallBlocked_LeavesStationListAtOnce()
    {
        var r = new Rig(BlockList.Empty);
        r.Decoder.Script(Slot(0), "CQ ON4XYZ JO20", "CQ K1ABC FN42");
        await r.RunUntilAsync(Slot(0).AddSeconds(14.8));
        r.Session.Snapshot.Stations.Select(s => s.Call).Should().Contain("ON4XYZ");

        r.Session.Reconfigure(new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", FixedTxOffsetHz = 1500, Blocked = new BlockList([new BlockedCall("ON4XYZ", null, null)]) });

        r.Session.Snapshot.Stations.Select(s => s.Call).Should().Equal("K1ABC");
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
