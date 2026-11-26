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
using Ft8Client.Core.Bands;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Time;
using Ft8Client.Core.Wspr;
using Ft8Client.Data.Reference;
using Ft8Client.Data.Settings;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

/// <summary>
/// The WSPR beacon on a simulated radio, a manual clock and a null audio output: nothing here can key a real radio. Covers what
/// the build guide lists for W4: never keys without a command, the PTT lead and tail, faults, refusals, the operator-presence
/// rule, the duty cycle, a restart, and the radio's power put back.
/// </summary>
public sealed class WsprBeaconSessionTests
{
    private static readonly DateTime T0 = new(2026, 11, 26, 2, 2, 0, DateTimeKind.Utc);
    private static DateTime Slot(int n) => T0.AddMinutes(2 * n);

    internal sealed class Rig
    {
        public readonly ManualClock Clock = new(T0.AddSeconds(-10));
        public readonly SimulatedRig Radio;
        public readonly NullAudioOutput Audio;
        public readonly Transmitter Tx;
        public readonly Session Session;
        public readonly SlotClock Slots;
        public readonly FakePower Power = new();
        public readonly ScriptSpots Spots = new();
        public readonly FrequencyTable Frequencies = ReferenceData.LoadFrequencies();
        public WsprOptions Options = new() { TxPercent = 100, ReportedDbm = 37, PowerPercent = 5 };
        public readonly List<(DateTime At, string Band)> Hops = [];
        public readonly List<string> NotesSeen = [];
        private readonly List<(DateTime At, string Band)> _bandLog = [];

        /// <summary>The band the session was on at a time, from the band changes seen so far.</summary>
        public string BandAt(DateTime t) => _bandLog.LastOrDefault(b => b.At <= t).Band ?? Session.Config.Band;

        public Rig(Mode mode = Mode.Wspr, int watchdogMinutes = 6, string call = "W7LIT", string grid = "DN40")
        {
            Radio = new SimulatedRig(Clock, 14_095_600);
            Audio = new NullAudioOutput { Now = () => Clock.UtcNow };
            Tx = new Transmitter(Radio, Audio, Clock, Frequencies);
            var config = new SessionConfig { MyCall = call, MyGrid = grid, Mode = mode, Contact = new Ft8Client.Core.Contacts.ContactSettings { WatchdogMinutes = watchdogMinutes } };
            Session = new Session(config, new NoFt(), new Silent(), Tx, Clock, ReferenceData.LoadCountries(), Frequencies);
            Session.AttachWspr(Spots, null);
            Session.WsprSettings = () => Options;
            Session.BeaconPower = Power;
            Session.SetWsprRandom(new Random(3));
            _bandLog.Add((DateTime.MinValue, Session.Config.Band));
            Session.Changed += s =>
            {
                lock (_bandLog)
                {
                    if (_bandLog[^1].Band != s.Band) _bandLog.Add((Clock.UtcNow, s.Band));
                    if (s.Wspr?.Beacon.NextSlotNote is { Length: > 0 } n && !NotesSeen.Contains(n)) NotesSeen.Add(n);
                }
            };
            Slots = new SlotClock(Clock, mode == Mode.Wspr ? Mode.Wspr : Mode.Ft8);
            Slots.Event += Session.OnSlotEvent;
            Slots.Poll();
        }

        public async Task RunAsync(TimeSpan duration, int stepMs = 100, TimeSpan? activityEvery = null)
        {
            var end = Clock.UtcNow + duration;
            var lastActivity = Clock.UtcNow;
            while (Clock.UtcNow < end)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(stepMs));
                if (activityEvery is { } every && Clock.UtcNow - lastActivity >= every)
                {
                    Session.Activity();
                    lastActivity = Clock.UtcNow;
                }
                Slots.Poll();
                Tx.Poll();
                await Session.DecodeIdle;
            }
        }

        public async Task RunUntilAsync(DateTime end, int stepMs = 100, TimeSpan? activityEvery = null) =>
            await RunAsync(end - Clock.UtcNow, stepMs, activityEvery);

        public IEnumerable<DateTime> KeyDowns => Radio.PttLog.Where(p => p.On).Select(p => p.TimeUtc);

        public IEnumerable<DateTime> KeyUps => Radio.PttLog.Where(p => !p.On).Select(p => p.TimeUtc);
    }

    internal sealed class FakePower : IBeaconPower
    {
        public List<int> Applied { get; } = [];

        public int Restored { get; private set; }

        public string? Note { get; set; }

        public Task<string?> ApplyAsync(int percent)
        {
            Applied.Add(percent);
            return Task.FromResult(Note);
        }

        public Task RestoreAsync()
        {
            Restored++;
            return Task.CompletedTask;
        }
    }

    internal sealed class ScriptSpots : IWsprDecoder
    {
        public Dictionary<DateTime, WsprSpot[]> Script { get; } = [];

        public List<(DateTime Slot, long Dial)> Decoded { get; } = [];

        public Task<WsprDecodeResult> DecodeAsync(SlotAudio slot, long dialHz, CancellationToken ct)
        {
            lock (Script) Decoded.Add((slot.SlotStartUtc, dialHz));
            return Task.FromResult(new WsprDecodeResult(Script.GetValueOrDefault(slot.SlotStartUtc) ?? [], 0, TimeSpan.Zero, DecodeStatus.Ok));
        }
    }

    internal sealed class NoFt : IDecoder
    {
        public Task<DecodeResult> DecodeAsync(SlotAudio slot, DecodeContext ctx, CancellationToken ct) =>
            Task.FromResult(new DecodeResult([], TimeSpan.Zero, DecodeStatus.Ok));
    }

    internal sealed class Silent : ISlotSource
    {
        public void BeginSlot(DateTime slotStartUtc, Mode mode)
        {
        }

        public void CancelSlot()
        {
        }

        public SlotAudio? Cutoff(DateTime slotStartUtc, Mode mode) => new(slotStartUtc, mode, new short[16]);
    }

    // --- never keys on its own

    [Fact]
    public async Task Session_NoBeaconCommand_NeverKeys()
    {
        var r = new Rig();
        r.Options.TxPercent = 100;

        await r.RunAsync(TimeSpan.FromMinutes(30), stepMs: 250);

        r.Radio.PttLog.Should().BeEmpty();
        r.Audio.Played.Should().BeEmpty();
        r.Session.BeaconOn.Should().BeFalse();
    }

    [Fact]
    public void Session_New_BeaconIsOffWhateverTheSettingsHold()
    {
        var r = new Rig();
        r.Options = new WsprOptions { TxPercent = 100, UploadEnabled = true };

        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Wspr!.Beacon.On.Should().BeFalse();
        typeof(WsprOptions).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("BeaconOn") || n.Contains("Running"),
            "the settings never store the beacon as on");
    }

    [Fact]
    public async Task Restart_NewSessionOverTheSameSettings_StartsOffAndStaysOff()
    {
        var first = new Rig();
        (await first.Session.StartBeaconAsync()).Should().BeNull();
        first.Session.BeaconOn.Should().BeTrue();

        var second = new Rig { Options = first.Options };
        second.Session.BeaconOn.Should().BeFalse();
        await second.RunAsync(TimeSpan.FromMinutes(10), stepMs: 250);
        second.Radio.PttLog.Should().BeEmpty();
    }

    // --- a transmission

    [Fact]
    public async Task Beacon_FullPercent_KeysWithLeadThenAudioThenTailAndRestores()
    {
        var r = new Rig();
        (await r.Session.StartBeaconAsync()).Should().BeNull();
        r.Power.Applied.Should().Equal(5);

        await r.RunUntilAsync(Slot(0).AddSeconds(116));

        var down = r.KeyDowns.Should().ContainSingle().Subject;
        var up = r.KeyUps.Should().ContainSingle().Subject;
        (down - Slot(0)).TotalSeconds.Should().BeApproximately(0.8, 0.15, "PTT leads the audio by 200 ms, the audio starts 1 s in");
        var audio = r.Audio.Played.Should().ContainSingle().Subject;
        (audio.StartedUtc - Slot(0)).TotalSeconds.Should().BeApproximately(1.0, 0.15);
        audio.Samples.Length.Should().Be(WsprWaveform.Length(48_000));
        (up - audio.StartedUtc).TotalSeconds.Should().BeApproximately(110.592 + 0.1, 0.2, "PTT is released a 100 ms tail after the audio ends");
        r.Session.Snapshot.Wspr!.Beacon.LastMessage.Should().Be("W7LIT DN40 37");
    }

    [Fact]
    public async Task Beacon_TransmitSlot_IsNotRecordedForDecoding()
    {
        var r = new Rig();
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(1).AddSeconds(5));

        r.Session.Snapshot.Wspr!.LastSlotUtc.Should().BeNull("the slot it transmitted in was not received");
    }

    [Fact]
    public async Task Beacon_SixCharacterGrid_AlternatesTheTwoMessages()
    {
        var r = new Rig(grid: "DN40AB");
        await r.Session.StartBeaconAsync();
        var sent = new List<string>();
        r.Session.Transmitting += (tx, _) => sent.Add(tx.Plan.Message);

        await r.RunUntilAsync(Slot(2).AddSeconds(116));

        sent.Should().Equal("W7LIT DN40 37", "<W7LIT> DN40AB 37", "W7LIT DN40 37");
    }

    [Fact]
    public async Task Beacon_StoppedByTheOperator_RestoresPowerAndSendsNothingMore()
    {
        var r = new Rig();
        await r.Session.StartBeaconAsync();
        await r.RunUntilAsync(Slot(0).AddSeconds(116));
        r.KeyDowns.Should().HaveCount(1);

        r.Session.StopBeacon(null);
        await r.RunUntilAsync(Slot(3).AddSeconds(116));

        r.KeyDowns.Should().HaveCount(1);
        r.Power.Restored.Should().Be(1);
        r.Session.BeaconOn.Should().BeFalse();
    }

    [Fact]
    public async Task Beacon_StoppedDuringATransmission_LetsItFinishThenSendsNothingMore()
    {
        var r = new Rig();
        await r.Session.StartBeaconAsync();
        await r.RunUntilAsync(Slot(0).AddSeconds(30));
        r.Radio.Ptt.Should().BeTrue();

        r.Session.StopBeacon(null);
        await r.RunUntilAsync(Slot(0).AddSeconds(60));
        r.Radio.Ptt.Should().BeTrue("Stop beacon does not cut a transmission on the air; Halt Tx does");
        await r.RunUntilAsync(Slot(2).AddSeconds(5));

        r.Radio.Ptt.Should().BeFalse();
        r.KeyDowns.Should().HaveCount(1);
    }

    // --- refusals at start

    [Fact]
    public async Task StartBeacon_NotInWspr_Refuses()
    {
        var r = new Rig(mode: Mode.Ft8);

        (await r.Session.StartBeaconAsync()).Should().Be(Session.WsprRefusal);

        r.Session.BeaconOn.Should().BeFalse();
        r.Power.Applied.Should().BeEmpty();
    }

    [Theory]
    [InlineData("W7LIT", "DN40", 36, 1500, 20, "cannot be sent")]
    [InlineData("W7LIT", "DN40", 37, 1399, 20, "outside the WSPR window")]
    [InlineData("W7LIT", "DN40", 37, 1601, 20, "outside the WSPR window")]
    [InlineData("W7LIT", "DN40", 37, 1500, 0, "Tx percent")]
    [InlineData("W7LIT", "DN40", 37, 1500, 101, "Tx percent")]
    [InlineData("ABCDEF", "DN40", 37, 1500, 20, "digit")]
    [InlineData("PJ4/K1ABC", "FK52", 37, 1500, 20, "6-character locator")]
    [InlineData("W7LIT", "ZZ99", 37, 1500, 20, "locator")]
    [InlineData("W7LIT", "", 37, 1500, 20, "locator")]
    public async Task StartBeacon_SettingsThatCannotBeSentAsShown_RefusesWithTheReasonAndNeverKeys(string call, string grid, int dbm, int offset, int percent, string reasonHas)
    {
        var r = new Rig(call: call, grid: grid);
        r.Options = new WsprOptions { TxPercent = percent, ReportedDbm = dbm, OffsetHz = offset };

        var why = await r.Session.StartBeaconAsync();

        why.Should().Contain(reasonHas);
        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Fault.Should().Be(why);
        await r.RunAsync(TimeSpan.FromMinutes(5), stepMs: 250);
        r.Radio.PttLog.Should().BeEmpty();
        r.Power.Applied.Should().BeEmpty("the radio's power is not touched for a beacon that did not start");
    }

    // --- guards at plan time

    [Fact]
    public async Task Beacon_DialAtTheBandTop_RefusesTheWholeWindowNotJustTheTone()
    {
        var r = new Rig();
        r.Session.SetDial(14_349_000); // the tone would fit, the top of the 200 Hz window would not
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(1).AddSeconds(10));

        r.Radio.PttLog.Should().BeEmpty();
        r.Session.BeaconOn.Should().BeFalse("a guard that says no stops the beacon; it does not retry by itself");
        r.Session.Snapshot.Fault.Should().Contain("outside").And.Contain("transmit limits");
    }

    [Fact]
    public async Task Beacon_LicenceWithoutDataOnTheBand_Refuses()
    {
        var r = new Rig();
        r.Frequencies.Licence = UsLicenceClass.Technician;
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(1).AddSeconds(10));

        r.Radio.PttLog.Should().BeEmpty();
        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Fault.Should().Contain("Technician").And.Contain("receive only");
    }

    [Fact]
    public async Task Beacon_GeneralOnASegmentItLacks_Refuses()
    {
        var r = new Rig();
        r.Frequencies.Licence = UsLicenceClass.General;
        r.Session.SetDial(14_000_000); // 20 m bottom: below the General data segment
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(1).AddSeconds(10));

        r.Radio.PttLog.Should().BeEmpty();
        r.Session.Snapshot.Fault.Should().Contain("General");
    }

    [Fact]
    public async Task Beacon_ClockOffByThreeSeconds_RefusesAndNeverKeys()
    {
        var r = new Rig();
        r.Session.SetClockOffset(3.0);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(1).AddSeconds(10));

        r.Radio.PttLog.Should().BeEmpty();
        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Fault.Should().Contain("Your clock is off by 3.0 s");
    }

    [Fact]
    public async Task Beacon_NoAudioOutput_Refuses()
    {
        var r = new Rig();
        r.Tx.SetOutput(null);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(1).AddSeconds(10));

        r.Radio.PttLog.Should().BeEmpty();
        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Fault.Should().Contain("audio");
    }

    [Fact]
    public async Task Beacon_TransmitterDisabled_Refuses()
    {
        var r = new Rig();
        r.Tx.DisabledReason = "This build cannot transmit.";
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(1).AddSeconds(10));

        r.Radio.PttLog.Should().BeEmpty();
        r.Session.Snapshot.Fault.Should().Contain("This build cannot transmit.");
    }

    // --- faults and halt

    [Fact]
    public async Task Beacon_RigFaultMidTransmission_ReleasesPttStopsAndNeverSendsAgain()
    {
        var r = new Rig();
        await r.Session.StartBeaconAsync();
        await r.RunUntilAsync(Slot(0).AddSeconds(20));
        r.Radio.Ptt.Should().BeTrue();

        r.Radio.InjectFault("USB cable removed");
        await r.RunUntilAsync(Slot(4).AddSeconds(10));

        r.Radio.Ptt.Should().BeFalse();
        r.KeyDowns.Should().HaveCount(1, "no later slot transmits");
        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Fault.Should().Contain("USB cable removed");
        r.Power.Restored.Should().Be(1);
    }

    [Fact]
    public async Task Beacon_AudioFaultMidTransmission_ReleasesPttAndStops()
    {
        var r = new Rig();
        await r.Session.StartBeaconAsync();
        await r.RunUntilAsync(Slot(0).AddSeconds(20));

        r.Audio.RaiseFault("sound card removed");
        await r.RunUntilAsync(Slot(3).AddSeconds(10));

        r.Radio.Ptt.Should().BeFalse();
        r.KeyDowns.Should().HaveCount(1);
        r.Session.BeaconOn.Should().BeFalse();
    }

    [Fact]
    public async Task Beacon_HaltTx_ReleasesPttStopsAndRestoresPower()
    {
        var r = new Rig();
        await r.Session.StartBeaconAsync();
        await r.RunUntilAsync(Slot(0).AddSeconds(20));

        await r.Session.HaltAsync("Halted.");
        await r.RunUntilAsync(Slot(3).AddSeconds(10));

        r.Radio.Ptt.Should().BeFalse();
        r.KeyDowns.Should().HaveCount(1);
        r.Session.BeaconOn.Should().BeFalse();
        r.Power.Restored.Should().Be(1);
    }

    [Fact]
    public async Task Beacon_ManualBandChange_StopsItAndRestoresPower()
    {
        var r = new Rig();
        await r.Session.StartBeaconAsync();
        await r.RunUntilAsync(Slot(0).AddSeconds(20));

        r.Session.SetBand("40m", Mode.Wspr);
        await r.RunUntilAsync(Slot(3).AddSeconds(10));

        r.Radio.Ptt.Should().BeFalse();
        r.KeyDowns.Should().HaveCount(1);
        r.Session.BeaconOn.Should().BeFalse();
        r.Power.Restored.Should().Be(1);
    }

    [Fact]
    public async Task Beacon_LeavingWspr_StopsItAndRestoresPower()
    {
        var r = new Rig();
        await r.Session.StartBeaconAsync();
        await r.RunUntilAsync(Slot(0).AddSeconds(20));

        r.Slots.SetMode(Mode.Ft8);
        r.Session.SetBand("20m", Mode.Ft8);
        await r.RunUntilAsync(Slot(3).AddSeconds(10));

        r.Session.BeaconOn.Should().BeFalse();
        r.Power.Restored.Should().Be(1);
        r.Radio.Ptt.Should().BeFalse();
    }

    [Fact]
    public async Task Beacon_DialMovedFar_Stops()
    {
        var r = new Rig();
        await r.Session.StartBeaconAsync();
        await r.RunUntilAsync(Slot(0).AddSeconds(125));

        r.Session.SetDial(14_080_000);

        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Fault.Should().Contain("dial moved");
    }

    [Fact]
    public async Task Tune_WhileTheBeaconRuns_IsRefused()
    {
        var r = new Rig();
        await r.Session.StartBeaconAsync();

        (await r.Session.TuneAsync(5)).Should().Contain("Tune is only available");

        r.Radio.PttLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Power_RadioWithNoPowerControl_BeaconRunsAndTheOperatorIsTold()
    {
        var r = new Rig();
        r.Power.Note = BeaconPowerNote;

        await r.Session.StartBeaconAsync();

        r.Session.Snapshot.Wspr!.Beacon.PowerNote.Should().Be(BeaconPowerNote);
        r.Session.BeaconOn.Should().BeTrue();
    }

    private const string BeaconPowerNote = "Set the power on the radio; Costas cannot.";

    // --- operator presence

    [Fact]
    public async Task Presence_NoInputForSixMinutes_StopsAfterTheCurrentTransmission()
    {
        var r = new Rig(watchdogMinutes: 6);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(4).AddSeconds(10));

        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Fault.Should().Be("Beacon stopped: no operator input for 6 minutes (Tx watchdog).");
        r.KeyDowns.Should().HaveCount(3, "slots 0, 1 and 2 went out; the one at 6 minutes was not planned");
        r.KeyUps.Should().HaveCount(3, "no transmission was cut off");
        r.Radio.Ptt.Should().BeFalse();
        r.Power.Restored.Should().Be(1);
    }

    [Fact]
    public async Task Presence_ShortWatchdog_NeverLessThanTwoMinutes()
    {
        var r = new Rig(watchdogMinutes: 1);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(3).AddSeconds(10));

        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Fault.Should().Be("Beacon stopped: no operator input for 2 minutes (Tx watchdog).");
        r.KeyDowns.Should().HaveCount(1, "the first transmission finished, and the second slot was not planned");
        r.KeyUps.Should().HaveCount(1);
    }

    [Fact]
    public async Task Presence_ShortWatchdogLimit_IsOneSlotNotOneMinute()
    {
        var r = new Rig(watchdogMinutes: 1);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(0).AddSeconds(100));

        r.Session.BeaconOn.Should().BeTrue("100 seconds into the run is inside the 2 minute floor");
    }

    [Fact]
    public async Task Presence_OperatorKeepsGivingInput_KeepsItRunning()
    {
        var r = new Rig(watchdogMinutes: 6);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(14).AddSeconds(10), stepMs: 250, activityEvery: TimeSpan.FromMinutes(3));

        r.Session.BeaconOn.Should().BeTrue();
        r.KeyDowns.Should().HaveCount(15, "a transmission in every one of 15 slots at 100 %");
    }

    [Fact]
    public async Task Presence_PhoneImHere_CountsAsInput()
    {
        var r = new Rig(watchdogMinutes: 6);
        await r.Session.StartBeaconAsync();
        for (var i = 0; i < 6; i++)
        {
            await r.RunUntilAsync(r.Clock.UtcNow + TimeSpan.FromMinutes(3), stepMs: 250);
            r.Session.NoteWsprPresence();
        }

        r.Session.BeaconOn.Should().BeTrue();
    }

    [Fact]
    public async Task Presence_BeaconsOwnTransmissionsAndDecodes_DoNotCount()
    {
        var r = new Rig(watchdogMinutes: 2);
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(3).AddSeconds(10), stepMs: 250);

        r.Session.BeaconOn.Should().BeFalse("it transmitted and decoded all along and still stopped");
    }

    [Fact]
    public async Task Presence_ExpiresBetweenPlanAndSlotStart_PendingTransmissionDroppedBeforePtt()
    {
        var r = new Rig(watchdogMinutes: 2);
        await r.Session.StartBeaconAsync(); // 10 s before slot 0, so the 2 minute limit would end 110 s into it
        await r.RunUntilAsync(Slot(0).AddSeconds(-4), stepMs: 250);
        r.Session.Activity(); // input 4 s before slot 0: the limit now ends 116 s into it, after slot 1 is planned (114 s) and before it starts (120 s)

        await r.RunUntilAsync(Slot(1).AddSeconds(5), stepMs: 250);

        r.KeyDowns.Should().HaveCount(1, "only slot 0 went out");
        r.Tx.Pending.Should().BeNull();
        r.Session.BeaconOn.Should().BeFalse();
        r.Session.Snapshot.Fault.Should().Be("Beacon stopped: no operator input for 2 minutes (Tx watchdog).");
        r.Radio.Ptt.Should().BeFalse();
    }

    // --- the duty cycle in the session

    [Fact]
    public async Task Beacon_TwentyPercent_TransmitsInTheSlotsTheTableChooses()
    {
        var r = new Rig();
        r.Options = new WsprOptions { TxPercent = 20 };
        await r.Session.StartBeaconAsync();

        await r.RunUntilAsync(Slot(30).AddSeconds(10), stepMs: 500, activityEvery: TimeSpan.FromMinutes(2));

        var expected = new WsprTxSchedule(new Random(3));
        var slots = Enumerable.Range(0, 30).Where(i => expected.IsTx(Slot(i), 20)).Select(Slot).ToList();
        slots.Should().NotBeEmpty();
        var actual = r.KeyDowns.Select(d => Slot((int)((d - T0).TotalMinutes / 2))).ToList();
        actual.Should().Equal(slots);
    }

}
