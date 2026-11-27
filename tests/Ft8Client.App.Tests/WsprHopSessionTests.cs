// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.Core;
using Ft8Client.Core.Bands;
using Ft8Client.Core.Wspr;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

/// <summary>
/// WSPR band hopping on a simulated radio and a manual clock (docs/10-wspr.md section 7): the band changes between slots, the first
/// slot after a hop never transmits, a band the guards refuse only receives, a hop is not operator input, and the slot just recorded
/// keeps its spots.
/// </summary>
public class WsprHopSessionTests
{
    private static readonly DateTime T0 = new(2026, 11, 26, 2, 2, 0, DateTimeKind.Utc);
    private static DateTime Slot(int n) => T0.AddMinutes(2 * n);

    private static WsprBeaconSessionTests.Rig HoppingRig(WsprOptions options, string startBand = "20m", int watchdogMinutes = 60)
    {
        var r = new WsprBeaconSessionTests.Rig(watchdogMinutes: watchdogMinutes) { Options = options };
        r.Session.SetBand(startBand, Mode.Wspr);
        r.Radio.SetFrequencyAsync(r.Frequencies.Find(startBand, Mode.Wspr)!.DialHz, CancellationToken.None).GetAwaiter().GetResult();
        r.Session.SetDial(r.Frequencies.Find(startBand, Mode.Wspr)!.DialHz);
        r.Hops.Clear();
        r.Session.HopBand = async (band, mode) =>
        {
            r.Hops.Add((r.Clock.UtcNow, band));
            // As the app's band switch does: the session first, then the radio, then the dial the radio reports.
            r.Session.SetBand(band, mode);
            var dial = r.Frequencies.Find(band, mode)!.DialHz;
            await r.Radio.SetFrequencyAsync(dial, CancellationToken.None);
            r.Session.SetDial(dial);
        };
        return r;
    }

    // The band each slot is on, by the same schedule the session follows, given where the beacon started.
    private static List<string> Bands(WsprOptions o, string start, int slots) =>
        Enumerable.Range(0, slots).Select(i => WsprHopSchedule.BandFor(Slot(i), o.HopBands, o.HopCoordinated, o.HopDwellSlots) ?? start).ToList();

    [Fact]
    public async Task Beacon_HopsBetweenSlots_AndTheFirstSlotOnANewBandNeverTransmits()
    {
        var o = new WsprOptions { TxPercent = 100, HopBands = ["20m", "40m"], HopDwellSlots = 3 };
        var r = HoppingRig(o);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(11).AddSeconds(116), stepMs: 250, activityEvery: TimeSpan.FromMinutes(3));

        var bands = Bands(o, "20m", 12);
        var expectedTx = Enumerable.Range(0, 12).Where(i => bands[i] == (i == 0 ? "20m" : bands[i - 1])).ToList();
        expectedTx.Should().NotBeEmpty().And.HaveCountLessThan(12);
        var actual = r.KeyDowns.Select(d => (int)((d - T0).TotalMinutes / 2)).ToList();
        actual.Should().Equal(expectedTx, "it transmits in every slot that is not the first on a new band, and in no other");
        r.Hops.Select(h => h.Band).Should().Equal(Enumerable.Range(0, 12).Where(i => bands[i] != (i == 0 ? "20m" : bands[i - 1])).Select(i => bands[i]));
        r.Session.BeaconOn.Should().BeTrue();
        r.Power.Restored.Should().Be(0, "a hop does not end the run, so the radio's power is not put back");
    }

    [Fact]
    public async Task Beacon_HopsAtTheBoundary_NotInTheMiddleOfASlot()
    {
        var o = new WsprOptions { TxPercent = 100, HopBands = ["20m", "40m"], HopDwellSlots = 2 };
        var r = HoppingRig(o);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(7).AddSeconds(116), stepMs: 250, activityEvery: TimeSpan.FromMinutes(3));

        r.Hops.Should().NotBeEmpty();
        foreach (var (at, _) in r.Hops)
        {
            var into = (at - T0).TotalSeconds % 120;
            (into is >= 113.5 or < 1.0).Should().BeTrue($"a hop at {into:0.0} s into a slot is at the boundary: after the cut-off, or at the start of the beacon");
        }
    }

    [Fact]
    public async Task Beacon_Hop_DoesNotCutATransmissionOffAndNeverKeysDuringIt()
    {
        var o = new WsprOptions { TxPercent = 100, HopBands = ["20m", "40m"], HopDwellSlots = 2 };
        var r = HoppingRig(o);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(7).AddSeconds(116), stepMs: 250, activityEvery: TimeSpan.FromMinutes(3));

        r.KeyDowns.Count().Should().Be(r.KeyUps.Count(), "every key-down has its key-up: nothing was cut off or left keyed");
        r.Radio.Ptt.Should().BeFalse();
        foreach (var (at, _) in r.Hops) r.Radio.PttLog.Where(p => p.On && p.TimeUtc > at.AddSeconds(-3) && p.TimeUtc < at.AddSeconds(0.5)).Should().BeEmpty("no PTT edge at a hop");
    }

    [Fact]
    public async Task Beacon_SlotRecordedBeforeAHop_KeepsItsSpots()
    {
        var o = new WsprOptions { TxPercent = 1, HopBands = ["20m", "40m"], HopDwellSlots = 1 };
        var r = HoppingRig(o);
        var spot = new WsprSpot(Slot(0), -12, 0.1, 14_097_050, 0, "K1ABC", "FN42", 30, false, 14_095_600);
        r.Spots.Script[Slot(0)] = [spot];
        r.Spots.Script[Slot(1)] = [spot with { SlotStartUtc = Slot(1), Callsign = "VK3XYZ" }];
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(1).AddSeconds(116), stepMs: 250, activityEvery: TimeSpan.FromMinutes(3));

        r.Session.Snapshot.Wspr!.Recent.Select(h => h.Spot.Callsign).Should().Contain(["K1ABC", "VK3XYZ"], "the slot recorded before a hop is not dropped as a dial move");
    }

    [Fact]
    public async Task Beacon_HopsToABandTheLicenceRefuses_ReceivesThereSaysSoAndCarriesOn()
    {
        var o = new WsprOptions { TxPercent = 100, HopBands = ["10m", "20m"], HopDwellSlots = 2 };
        var r = HoppingRig(o, startBand: "10m");
        r.Frequencies.Licence = UsLicenceClass.Technician; // data on 10 m, none on 20 m
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(7).AddSeconds(116), stepMs: 250, activityEvery: TimeSpan.FromMinutes(3));

        r.Session.BeaconOn.Should().BeTrue("a band the guards refuse is not a fault");
        var on20 = r.Radio.PttLog.Where(p => p.On).Select(p => p.TimeUtc).Where(t => r.BandAt(t) == "20m");
        on20.Should().BeEmpty("nothing was sent on 20 m");
        r.KeyDowns.Should().NotBeEmpty("it still transmits on 10 m");
        r.NotesSeen.Should().Contain(n => n.StartsWith("Receive only on 20 m", StringComparison.Ordinal));
    }


    [Fact]
    public async Task Beacon_HopFails_StopsWithTheReason()
    {
        var o = new WsprOptions { TxPercent = 100, HopBands = ["20m", "40m"], HopDwellSlots = 1 };
        var r = HoppingRig(o);
        r.Session.HopBand = (_, _) => throw new InvalidOperationException("the radio did not answer");
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(2).AddSeconds(10), stepMs: 250, activityEvery: TimeSpan.FromMinutes(1));

        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Fault.Should().Contain("could not change band").And.Contain("the radio did not answer");
        r.Power.Restored.Should().Be(1);
    }

    [Fact]
    public async Task Beacon_HopDoesNotArrive_Stops()
    {
        var o = new WsprOptions { TxPercent = 100, HopBands = ["20m", "40m"], HopDwellSlots = 1 };
        var r = HoppingRig(o);
        r.Session.HopBand = (_, _) => Task.CompletedTask; // returns without changing band
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(2).AddSeconds(10), stepMs: 250, activityEvery: TimeSpan.FromMinutes(1));

        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Fault.Should().Contain("did not change band");
    }

    [Fact]
    public async Task Beacon_ManualBandChangeWhileHopping_StillStopsIt()
    {
        var o = new WsprOptions { TxPercent = 100, HopBands = ["20m", "40m"], HopDwellSlots = 5 };
        var r = HoppingRig(o);
        await r.Session.StartBeaconAsync();
        await r.RunUntilAsync(Slot(0).AddSeconds(30), stepMs: 250);

        r.Session.SetBand("30m", Mode.Wspr);

        r.Session.BeaconOn.Should().BeFalse();
    }

    [Fact]
    public async Task Beacon_NoHopBands_StaysOnItsBand()
    {
        var o = new WsprOptions { TxPercent = 100 };
        var r = HoppingRig(o);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(3).AddSeconds(116), stepMs: 250, activityEvery: TimeSpan.FromMinutes(3));

        r.Hops.Should().BeEmpty();
        r.KeyDowns.Should().HaveCount(4);
    }

    [Fact]
    public async Task Beacon_OneHopBand_StaysAndTransmitsEverySlot()
    {
        var o = new WsprOptions { TxPercent = 100, HopBands = ["20m"] };
        var r = HoppingRig(o);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(3).AddSeconds(116), stepMs: 250, activityEvery: TimeSpan.FromMinutes(3));

        r.Hops.Should().BeEmpty();
        r.KeyDowns.Should().HaveCount(4);
    }

    [Fact]
    public async Task Coordinated_EverySlotIsAHop_SoTheBeaconOnlyReceives()
    {
        var o = new WsprOptions { TxPercent = 100, HopBands = ["160m", "80m", "40m", "30m", "20m", "17m", "15m", "12m", "10m"], HopCoordinated = true };
        var r = HoppingRig(o, startBand: "20m");
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(7).AddSeconds(116), stepMs: 250, activityEvery: TimeSpan.FromMinutes(3));

        r.Hops.Count.Should().BeGreaterThanOrEqualTo(6, "the coordinated schedule names a new band every 2 minutes");
        r.KeyDowns.Should().BeEmpty("the first slot on every new band receives only, and every slot is a first slot");
    }

    [Theory]
    [InlineData("60m", "no WSPR frequency for 60m")]
    [InlineData("20m, nonsense", "no WSPR frequency for nonsense")]
    public async Task StartBeacon_HopBandWithNoWsprFrequency_RefusesAndSaysWhich(string list, string reasonHas)
    {
        var o = new WsprOptions { TxPercent = 100, HopBands = [.. list.Split(',', StringSplitOptions.TrimEntries)] };
        var r = HoppingRig(o);

        (await r.Session.StartBeaconAsync()).Should().Contain(reasonHas);

        r.Session.BeaconOn.Should().BeFalse();
    }

    [Fact]
    public async Task StartBeacon_DwellOutOfRange_Refuses()
    {
        var o = new WsprOptions { TxPercent = 100, HopBands = ["20m", "40m"], HopDwellSlots = 0 };
        var r = HoppingRig(o);

        (await r.Session.StartBeaconAsync()).Should().Contain("1 to 100 slots");
    }
}
