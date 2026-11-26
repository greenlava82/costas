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
using Ft8Client.Core.Wspr;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Data.Reference;
using Ft8Client.Decoding;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

/// <summary>WSPR receive through the session: slots, decodes, storage, refusals (docs/10-wspr-build.md, W1).</summary>
public sealed class WsprSessionTests : IDisposable
{
    // Even minute boundary: the slots below start at 02:02:00, 02:04:00 ...
    private static readonly DateTime T0 = new(2026, 11, 26, 2, 2, 0, DateTimeKind.Utc);
    private static DateTime Slot(int n) => T0.AddMinutes(2 * n);

    private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAlive;
    private readonly WsprSpotRepository _store;
    private readonly ManualClock _clock = new(T0.AddSeconds(-1));
    private readonly ScriptWsprDecoder _wspr = new();
    private readonly ScriptFtDecoder _ft = new();
    private readonly SimulatedRig _rig;
    private readonly Transmitter _tx;
    private readonly Session _session;
    private readonly SlotClock _slots;
    private readonly List<(DateTime Slot, IReadOnlyList<WsprSpot> Spots)> _heard = [];
    private readonly List<DateTime> _ftDecoded = [];

    public WsprSessionTests()
    {
        _store = new WsprSpotRepository(SqliteDatabase.InMemory(out _keepAlive));
        var freqs = ReferenceData.LoadFrequencies();
        _rig = new SimulatedRig(_clock);
        _tx = new Transmitter(_rig, new NullAudioOutput { Now = () => _clock.UtcNow }, _clock, freqs);
        var config = new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", Mode = Mode.Wspr, FixedTxOffsetHz = 1500 };
        _session = new Session(config, _ft, new SilentSource(), _tx, _clock, ReferenceData.LoadCountries(), freqs);
        _session.AttachWspr(_wspr, _store);
        _session.WsprHeard += (slot, _, spots) => _heard.Add((slot, spots));
        _session.SlotDecoded += (slot, _, _) => _ftDecoded.Add(slot);
        _slots = new SlotClock(_clock, Mode.Wspr);
        _slots.Event += _session.OnSlotEvent;
        _slots.Poll();
    }

    public void Dispose() => _keepAlive.Dispose();

    private async Task RunUntilAsync(DateTime end, int stepMs = 250)
    {
        while (_clock.UtcNow < end)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(stepMs));
            _slots.Poll();
            _tx.Poll();
            await _session.DecodeIdle;
        }
    }

    private static WsprSpot Spot(DateTime slot, string call, int snr = -10, string? grid = "FN42", int dbm = 30, long freq = 14_097_050) =>
        new(slot, snr, 0.3, freq, 0, call, grid, dbm, false, 14_095_600);

    [Fact]
    public async Task Slot_DecodedSpots_ReachSnapshotStoreAndEvent()
    {
        _wspr.Script(Slot(0), Spot(Slot(0), "K1ABC", -12), Spot(Slot(0), "G8VDQ", -20, "IO91", 37, 14_097_100));

        await RunUntilAsync(Slot(0).AddSeconds(115));

        var view = _session.Snapshot.Wspr;
        view.Should().NotBeNull();
        view!.Recent.Select(r => r.Spot.Callsign).Should().BeEquivalentTo("K1ABC", "G8VDQ");
        view.LastSlotUtc.Should().Be(Slot(0));
        view.LastSlotCount.Should().Be(2);
        view.NextSlotUtc.Should().Be(Slot(1));
        view.Recent.Should().OnlyContain(r => r.DistanceKm > 1000, "FN42 and IO91 are far from DN40");
        _store.Recent("W7LIT", 10).Should().HaveCount(2);
        _heard.Should().ContainSingle().Which.Spots.Should().HaveCount(2);
        _wspr.Dials.Should().Equal(14_095_600);
        _session.Snapshot.Services["decoder"].Health.Should().Be(Ft8Client.Core.Services.ServiceHealth.Ok);
    }

    [Fact]
    public async Task Slot_TwoSlots_NewestFirstAndCapturedOncePerSlot()
    {
        _wspr.Script(Slot(0), Spot(Slot(0), "K1ABC"));
        _wspr.Script(Slot(1), Spot(Slot(1), "VK3XYZ", -5, "QF22", 33, 14_097_020));

        await RunUntilAsync(Slot(1).AddSeconds(115));

        _session.Snapshot.Wspr!.Recent.Select(r => r.Spot.Callsign).Should().Equal("VK3XYZ", "K1ABC");
        _wspr.Slots.Should().Equal(Slot(0), Slot(1));
        _wspr.Lengths.Should().OnlyContain(n => n == 1_440_000 || n == 16, "the recorder returns whole slots");
    }

    [Fact]
    public async Task Slot_NothingHeard_SaysSo()
    {
        await RunUntilAsync(Slot(0).AddSeconds(115));

        _session.Snapshot.Wspr!.Recent.Should().BeEmpty();
        _session.Snapshot.Wspr!.DecoderState.Should().Contain("No WSPR signals");
        _heard.Should().BeEmpty();
    }

    [Fact]
    public async Task Slot_BandChangedDuringSlot_Dropped()
    {
        _wspr.Script(Slot(0), Spot(Slot(0), "K1ABC"));
        await RunUntilAsync(Slot(0).AddSeconds(60));

        _session.SetBand("40m", Mode.Wspr);
        await RunUntilAsync(Slot(0).AddSeconds(115));

        _session.Snapshot.Wspr!.Recent.Should().BeEmpty();
        _store.Recent("W7LIT", 10).Should().BeEmpty();
        _heard.Should().BeEmpty();
        _wspr.Slots.Should().BeEmpty("a slot recorded across a band change is never handed to the decoder");
    }

    [Fact]
    public async Task Slot_DialMovedFar_Dropped()
    {
        _wspr.Script(Slot(0), Spot(Slot(0), "K1ABC"));
        await RunUntilAsync(Slot(0).AddSeconds(60));

        _session.SetDial(14_080_000);
        await RunUntilAsync(Slot(0).AddSeconds(115));

        _session.Snapshot.Wspr!.DecoderState.Should().Contain("dropped");
        _store.Recent("W7LIT", 10).Should().BeEmpty();
    }

    [Fact]
    public async Task Slot_DialNudged_Kept()
    {
        _wspr.Script(Slot(0), Spot(Slot(0), "K1ABC"));
        await RunUntilAsync(Slot(0).AddSeconds(60));

        _session.SetDial(14_096_000);
        await RunUntilAsync(Slot(0).AddSeconds(115));

        _store.Recent("W7LIT", 10).Should().HaveCount(1);
    }

    [Fact]
    public async Task Slot_DecoderFails_ReportsAndKeepsRunning()
    {
        _wspr.Fail = "wsprd exited with code 3.";
        _wspr.Script(Slot(1), Spot(Slot(1), "K1ABC"));
        await RunUntilAsync(Slot(0).AddSeconds(115));

        _session.Snapshot.Wspr!.DecoderState.Should().Contain("code 3");
        _session.Snapshot.Services["decoder"].Health.Should().Be(Ft8Client.Core.Services.ServiceHealth.Degraded);

        _wspr.Fail = null;
        await RunUntilAsync(Slot(1).AddSeconds(115));

        _session.Snapshot.Wspr!.Recent.Should().ContainSingle().Which.Spot.Callsign.Should().Be("K1ABC");
    }

    [Fact]
    public async Task Slot_UploadWanted_StoresPendingOtherwiseNone()
    {
        _wspr.Script(Slot(0), Spot(Slot(0), "K1ABC"));
        _wspr.Script(Slot(1), Spot(Slot(1), "VK3XYZ", -5, "QF22", 33, 14_097_020));
        await RunUntilAsync(Slot(0).AddSeconds(115));
        _session.WsprUploadWanted = () => true;
        await RunUntilAsync(Slot(1).AddSeconds(115));

        _store.Recent("W7LIT", 10).ToDictionary(r => r.Callsign, r => r.UploadState).Should().Equal(
            new Dictionary<string, WsprUploadState> { ["VK3XYZ"] = WsprUploadState.Pending, ["K1ABC"] = WsprUploadState.None });
    }

    [Fact]
    public async Task AttachWspr_StoredSpots_ShownAfterRestart()
    {
        _wspr.Script(Slot(0), Spot(Slot(0), "K1ABC"));
        await RunUntilAsync(Slot(0).AddSeconds(115));

        var freqs = ReferenceData.LoadFrequencies();
        var again = new Session(new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", Mode = Mode.Wspr }, _ft, new SilentSource(), _tx, _clock,
            ReferenceData.LoadCountries(), freqs);
        again.AttachWspr(_wspr, _store);

        again.Snapshot.Wspr!.Recent.Should().BeEmpty("the list is filled when the store is attached and published");
        again.Publish();
        again.Snapshot.Wspr!.Recent.Select(r => r.Spot.Callsign).Should().Equal("K1ABC");
    }

    [Fact]
    public async Task Commands_FtAndKeyboardOnes_RefuseInWsprAndChangeNothing()
    {
        (await _session.CallStationAsync("K1ABC")).Should().BeFalse();
        _session.Snapshot.Fault.Should().Be(Session.WsprRefusal);

        _session.ClearFault(Session.WsprRefusal);
        _session.ToggleCq();
        _session.Snapshot.Fault.Should().Be(Session.WsprRefusal);
        _session.Snapshot.CallingCq.Should().BeFalse();

        _session.ClearFault(Session.WsprRefusal);
        _session.SetAutoCall(true);
        _session.Snapshot.AutoCall.Should().BeFalse();
        _session.Snapshot.Fault.Should().Be(Session.WsprRefusal);

        _session.AnswerCaller("K1ABC");
        _session.Resend();
        _session.SendMacro(1).Should().Be(Session.WsprRefusal);
        _session.SendText("CQ").Should().Be(Session.WsprRefusal);
        _session.KeyEnter("").Should().Be(Session.WsprRefusal);

        _session.Snapshot.Contact.Should().BeNull();
        _session.Snapshot.Transmitting.Should().BeFalse();
        _rig.Ptt.Should().BeFalse();
    }

    [Fact]
    public async Task Run_ThirtyMinutes_NeverKeysTheRadio()
    {
        _wspr.Script(Slot(0), Spot(Slot(0), "K1ABC"));
        await RunUntilAsync(T0.AddMinutes(30), stepMs: 500);

        _rig.Ptt.Should().BeFalse();
        _rig.PttLog.Should().BeEmpty();
    }

    [Fact]
    public async Task SetBand_WsprToFt8AndBack_LeavesFt8Unchanged()
    {
        await RunUntilAsync(Slot(0).AddSeconds(10));

        // To FT8: the clock changes at its next slot boundary, then FT8 slots decode as before.
        _slots.SetMode(Mode.Ft8);
        _session.SetBand("20m", Mode.Ft8);
        var ft8Slot = new DateTime(2026, 11, 26, 2, 4, 15, DateTimeKind.Utc);
        _ft.Script(ft8Slot, "CQ ZL2RPA RF70");
        await RunUntilAsync(ft8Slot.AddSeconds(14.8), stepMs: 50);

        _session.Snapshot.Mode.Should().Be(Mode.Ft8);
        _session.Snapshot.Wspr.Should().BeNull();
        _session.Snapshot.Rank.Line.Should().Contain(x => x.Station.Call == "ZL2RPA");
        _ftDecoded.Should().Contain(ft8Slot);
        _wspr.Slots.Should().NotContain(s => s > Slot(0), "no WSPR slot is decoded after leaving WSPR");
    }

    [Fact]
    public async Task Replay_WsprSamples_YieldsGoldenSpots()
    {
        var wsprd = WsprdLocator.Find();
        Assert.SkipWhen(wsprd is null, "wsprd is not installed; run third_party/fetch or install WSJT-X.");

        var samples = Path.Combine(FindRoot(), "samples", "wspr");
        var tmp = Path.Combine(Path.GetTempPath(), "ft8client-tests", Guid.NewGuid().ToString("N"));
        var freqs = ReferenceData.LoadFrequencies();
        var config = new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", Mode = Mode.Wspr };
        var session = new Session(config, _ft, new SimulatedSlotSource(samples), _tx, _clock, ReferenceData.LoadCountries(), freqs);
        session.AttachWspr(new WsprdDecoder(new WsprdOptions(wsprd!, tmp, TimeSpan.FromSeconds(120))), _store);
        var slots = new SlotClock(_clock, Mode.Wspr);
        slots.Event += session.OnSlotEvent;
        slots.Poll();

        while (_clock.UtcNow < Slot(0).AddSeconds(115))
        {
            _clock.Advance(TimeSpan.FromMilliseconds(250));
            slots.Poll();
        }
        await session.DecodeIdle;

        session.Snapshot.Wspr!.Recent.Select(r => r.Spot.Callsign).Should().BeEquivalentTo("ND6P", "W5BIT", "G8VDQ", "WD4LHT", "NM7J", "KI7CI", "DJ6OL", "W3HH", "W3BI");
        session.Snapshot.Wspr!.Recent.Should().OnlyContain(r => r.Spot.SlotStartUtc == Slot(0));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ft8Client.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class ScriptWsprDecoder : IWsprDecoder
    {
        private readonly Dictionary<DateTime, WsprSpot[]> _script = [];
        private readonly object _gate = new();

        public string? Fail { get; set; }

        public List<DateTime> Slots { get; } = [];

        public List<long> Dials { get; } = [];

        public List<int> Lengths { get; } = [];

        public void Script(DateTime slot, params WsprSpot[] spots)
        {
            lock (_gate) _script[slot] = spots;
        }

        public Task<WsprDecodeResult> DecodeAsync(SlotAudio slot, long dialHz, CancellationToken ct)
        {
            lock (_gate)
            {
                Slots.Add(slot.SlotStartUtc);
                Dials.Add(dialHz);
                Lengths.Add(slot.Samples12k.Length);
                if (Fail is { } why) return Task.FromResult(new WsprDecodeResult([], 0, TimeSpan.Zero, DecodeStatus.Failed, why));
                var spots = _script.GetValueOrDefault(slot.SlotStartUtc) ?? [];
                return Task.FromResult(new WsprDecodeResult(spots, 0, TimeSpan.FromSeconds(2), DecodeStatus.Ok));
            }
        }
    }

    private sealed class ScriptFtDecoder : IDecoder
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
