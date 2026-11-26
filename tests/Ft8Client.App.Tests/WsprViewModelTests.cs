// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.App.ViewModels;
using Ft8Client.Core;
using Ft8Client.Core.Services;
using Ft8Client.Core.Wspr;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

public class WsprViewModelTests
{
    private static readonly DateTime Slot = new(2026, 11, 26, 14, 2, 0, DateTimeKind.Utc);

    private sealed class Commands : IWsprCommands
    {
        public WsprOptions Options { get; set; } = new() { TxPercent = 20, ReportedDbm = 37 };

        public string? StartResult { get; set; }

        public int Started { get; private set; }

        public int Stopped { get; private set; }

        public int Here { get; private set; }

        public List<(string Band, Mode Mode)> Left { get; } = [];

        public Task<string?> StartBeaconAsync()
        {
            Started++;
            return Task.FromResult(StartResult);
        }

        public void StopBeacon() => Stopped++;

        public void ImHere() => Here++;

        public void UpdateOptions(Action<WsprOptions> change) => change(Options);

        public void LeaveWspr(string band, Mode mode) => Left.Add((band, mode));
    }

    private static WsprHeardSpot Heard(DateTime slot, string call, int snr = -10, double? km = 1000, string? grid = "FN42", long freq = 14_097_050) =>
        new(new WsprSpot(slot, snr, 0.1, freq, 0, call, grid, 30, false, 14_095_600), km);

    private static SessionSnapshot Snap(WsprView view, bool transmitting = false, string? sending = null, Dictionary<string, ServiceStatus>? services = null) =>
        new()
        {
            Mode = Mode.Wspr, Band = "20m", DialHz = 14_095_600, Transmitting = transmitting, TransmittingMessage = sending,
            Wspr = view, Services = services ?? new Dictionary<string, ServiceStatus>(),
        };

    private static WsprView View(WsprBeaconView? beacon = null, params WsprHeardSpot[] spots) =>
        new(spots, Slot.AddMinutes(2), Slot, spots.Length, "ok") { Beacon = beacon ?? WsprBeaconView.Off };

    private static WsprBeaconView On(bool? nextTx = null, string note = "", DateTime? presenceEnds = null, string? last = null, string? warning = null) =>
        new(true, nextTx, note, presenceEnds, null, last, 20, 37, 0, null, null, warning);

    [Fact]
    public void Tick_NextSlot_ShowsTimeAndCountdown()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot.AddSeconds(37));

        vm.Apply(Snap(View()));
        vm.Tick(Slot.AddSeconds(37));

        vm.Countdown.Should().Be("Next slot 14:04 UTC in 1:23");
        vm.SlotProgress.Should().BeApproximately(37.0 / 120, 0.001);
    }

    [Fact]
    public void Tick_PastTheSlotEnd_ClampsAtZero()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot.AddSeconds(125));
        vm.Apply(Snap(View()));

        vm.Tick(Slot.AddSeconds(125));

        vm.Countdown.Should().Be("Next slot 14:04 UTC in 0:00");
        vm.SlotProgress.Should().Be(1);
    }

    [Fact]
    public void Status_BeaconOff_SaysReceiving()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);
        vm.Apply(Snap(View()));

        vm.Status.Should().Be("Beacon off. Receiving.");
        vm.BeaconOn.Should().BeFalse();
        vm.StartStopText.Should().Be("Start beacon");
        vm.CanEditSettings.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, "", "Next slot transmits.")]
    [InlineData(false, "Receive slot", "Next slot receives (receive slot).")]
    [InlineData(false, "First slot after a band change", "Next slot receives (first slot after a band change).")]
    [InlineData(false, "", "Next slot receives.")]
    [InlineData(null, "", "Beacon on. Planning the next slot.")]
    public void Status_BeaconOn_SaysWhatTheNextSlotDoes(bool? tx, string note, string expected)
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);
        vm.Apply(Snap(View(On(tx, note))));

        vm.Status.Should().Be(expected);
        vm.BeaconOn.Should().BeTrue();
        vm.StartStopText.Should().Be("Stop beacon");
        vm.CanEditSettings.Should().BeFalse("the settings are fixed while the beacon runs");
    }

    [Fact]
    public void Status_Transmitting_ShowsTheMessageOnTheAir()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);
        vm.Apply(Snap(View(On(true, last: "W7LIT DN40 37")), transmitting: true, sending: "W7LIT DN40 37"));

        vm.OnAir.Should().BeTrue();
        vm.Status.Should().Be("Transmitting W7LIT DN40 37");
    }

    [Fact]
    public void Presence_BeaconOn_CountsDownTheTimeTheOperatorHas()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);
        vm.Apply(Snap(View(On(presenceEnds: Slot.AddMinutes(6)))));

        vm.Tick(Slot.AddMinutes(1).AddSeconds(30));

        vm.Presence.Should().Be("Stops in 4:30 unless you press a key or tap I'm here.");
    }

    [Fact]
    public void Presence_BeaconOff_IsBlank()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);
        vm.Apply(Snap(View()));

        vm.Presence.Should().BeEmpty();
    }

    [Fact]
    public void PowerLine_ReportedAndEstimate_ShownAndWarningPassedOn()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);
        var b = new WsprBeaconView(true, null, "", null, null, null, 20, 37, 0, 33.0, 40.0, "The radio's meter reads about 40 dBm, but the message says 37 dBm.");

        vm.Apply(Snap(View(b)));

        vm.PowerLine.Should().Be("Reports 37 dBm (5 W). Radio and attenuation give about 33.0 dBm. Meter reads about 40.0 dBm.");
        vm.PowerWarning.Should().Contain("meter reads about 40 dBm");
    }

    [Fact]
    public void PowerLine_LowPower_IsInMilliwatts()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);
        vm.Apply(Snap(View(WsprBeaconView.Off with { ReportedDbm = 10 })));

        vm.PowerLine.Should().StartWith("Reports 10 dBm (10 mW)");
    }

    [Fact]
    public void Spots_Heard_ListedAsTheSnapshotHasThemNewestFirst()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot) { Units = DistanceUnit.Kilometres };
        vm.Apply(Snap(View(null, Heard(Slot.AddMinutes(2), "VK3XYZ", -5, 15_000, "QF22"), Heard(Slot, "K1ABC", -12, 3_000), Heard(Slot, "PJ4/K1ABC", -20, null, null))));

        vm.Spots.Select(s => s.Call).Should().Equal("VK3XYZ", "K1ABC", "PJ4/K1ABC");
        vm.Spots[0].Should().Be(new WsprSpotRowViewModel("14:04", "VK3XYZ", "QF22", "-5", "0", vm.Spots[0].Distance, "30 dBm", "1450"));
        vm.Spots[0].Distance.Should().Contain("15").And.Contain("km");
        vm.Spots[2].Distance.Should().BeEmpty("no locator, no distance");
        vm.Spots[2].Grid.Should().BeEmpty();
    }

    [Fact]
    public void Spots_Miles_UsesTheOperatorsUnit()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot) { Units = DistanceUnit.Miles };
        vm.Apply(Snap(View(null, Heard(Slot, "K1ABC", -12, 1609.344))));

        vm.Spots[0].Distance.Should().MatchRegex("1,?000");
        vm.Spots[0].Distance.Should().Contain("mi");
    }

    [Fact]
    public void Spots_SameSnapshotAgain_NotRebuilt()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);
        var changes = 0;
        vm.Spots.CollectionChanged += (_, _) => changes++;
        var snap = Snap(View(null, Heard(Slot, "K1ABC")));

        vm.Apply(snap);
        var after = changes;
        vm.Apply(Snap(View(null, Heard(Slot, "K1ABC"))));

        changes.Should().Be(after);
    }

    [Fact]
    public void UploadStatus_FromTheServiceTable()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);
        var services = new Dictionary<string, ServiceStatus> { [ServiceNames.Wsprnet] = new(ServiceHealth.Ok, "WSPRnet: 3 spots sent", Slot) };

        vm.Apply(Snap(View(), services: services));

        vm.UploadStatus.Should().Be("WSPRnet: 3 spots sent");
    }

    [Fact]
    public void Apply_SavedOptions_ShownWithoutBeingWrittenBack()
    {
        var c = new Commands { Options = new WsprOptions { TxPercent = 50, ReportedDbm = 30, AttenuationDb = -10, PowerPercent = 10, OffsetHz = 1450, PreferType1 = true, UploadEnabled = true } };
        var writes = 0;
        var vm = new WsprViewModel(new CountingCommands(c, () => writes++), () => Slot);

        vm.Apply(Snap(View()));

        (vm.TxPercent, vm.ReportedDbm, vm.AttenuationDb, vm.PowerPercent, vm.OffsetHz, vm.PreferType1, vm.UploadEnabled)
            .Should().Be((50m, 30, -10m, 10m, 1450m, true, true));
        writes.Should().Be(0, "loading the saved values is not an edit");
    }

    [Fact]
    public void Edit_Settings_AreSaved()
    {
        var c = new Commands();
        var vm = new WsprViewModel(c, () => Slot);
        vm.Apply(Snap(View()));

        vm.TxPercent = 25;
        vm.ReportedDbm = 30;
        vm.AttenuationDb = -12;
        vm.PowerPercent = 8;
        vm.OffsetHz = 1520;
        vm.PreferType1 = true;
        vm.UploadEnabled = true;

        (c.Options.TxPercent, c.Options.ReportedDbm, c.Options.AttenuationDb, c.Options.PowerPercent, c.Options.OffsetHz, c.Options.PreferType1, c.Options.UploadEnabled)
            .Should().Be((25, 30, -12, 8, 1520, true, true));
    }

    [Fact]
    public void Edit_HopSettings_AreSavedAsAList()
    {
        var c = new Commands();
        var vm = new WsprViewModel(c, () => Slot);
        vm.Apply(Snap(View()));

        vm.HopBandsText = "20m, 40m  30m;17m";
        vm.HopDwellSlots = 3;
        vm.HopCoordinated = true;

        c.Options.HopBands.Should().Equal("20m", "40m", "30m", "17m");
        (c.Options.HopDwellSlots, c.Options.HopCoordinated).Should().Be((3, true));
    }

    [Fact]
    public void Apply_SavedHopSettings_ShownAndNotWrittenBack()
    {
        var c = new Commands { Options = new WsprOptions { HopBands = ["20m", "40m"], HopDwellSlots = 7, HopCoordinated = true } };
        var vm = new WsprViewModel(c, () => Slot);

        vm.Apply(Snap(View()));

        (vm.HopBandsText, vm.HopDwellSlots, vm.HopCoordinated).Should().Be(("20m, 40m", 7m, true));
    }

    [Fact]
    public void Status_ReceiveOnlyOnABand_KeepsTheReasonsCapitals()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);
        vm.Apply(Snap(View(On(false, "Receive only on 20 m: Not sent: your Technician licence doesn't allow data on 20 m."))));

        vm.Status.Should().Be("Next slot receives (receive only on 20 m: Not sent: your Technician licence doesn't allow data on 20 m.).");
    }

    [Fact]
    public void Edit_OutOfRange_IsClamped()
    {
        var c = new Commands();
        var vm = new WsprViewModel(c, () => Slot);
        vm.Apply(Snap(View()));

        vm.TxPercent = 400;
        vm.AttenuationDb = -50;
        vm.PowerPercent = 0;

        (c.Options.TxPercent, c.Options.AttenuationDb, c.Options.PowerPercent).Should().Be((100, -20, 1));
    }

    [Fact]
    public async Task StartStop_BeaconOff_Starts()
    {
        var c = new Commands();
        var vm = new WsprViewModel(c, () => Slot);
        vm.Apply(Snap(View()));

        await vm.StartStopCommand.ExecuteAsync(null);

        c.Started.Should().Be(1);
        c.Stopped.Should().Be(0);
        vm.Notice.Should().BeEmpty();
    }

    [Fact]
    public async Task StartStop_StartRefused_ShowsWhy()
    {
        var c = new Commands { StartResult = "Beacon not started: set your callsign and locator in Settings." };
        var vm = new WsprViewModel(c, () => Slot);
        vm.Apply(Snap(View()));

        await vm.StartStopCommand.ExecuteAsync(null);

        vm.Notice.Should().Be("Beacon not started: set your callsign and locator in Settings.");
    }

    [Fact]
    public async Task StartStop_BeaconOn_Stops()
    {
        var c = new Commands();
        var vm = new WsprViewModel(c, () => Slot);
        vm.Apply(Snap(View(On())));

        await vm.StartStopCommand.ExecuteAsync(null);

        c.Stopped.Should().Be(1);
        c.Started.Should().Be(0);
    }

    [Fact]
    public void ImHere_TellsTheBeacon()
    {
        var c = new Commands();
        var vm = new WsprViewModel(c, () => Slot);

        vm.ImHereCommand.Execute(null);

        c.Here.Should().Be(1);
    }

    [Fact]
    public void AskLeave_ThenYes_StopsAndChanges()
    {
        var c = new Commands();
        var vm = new WsprViewModel(c, () => Slot);

        vm.AskLeave("20m", Mode.Ft8);
        vm.LeaveQuestion.Should().Be("Stop the beacon and switch to FT8?");
        c.Left.Should().BeEmpty("nothing changes until the operator says yes");
        vm.ConfirmLeaveCommand.Execute(null);

        c.Left.Should().Equal([("20m", Mode.Ft8)]);
        vm.LeaveQuestion.Should().BeEmpty();
    }

    [Fact]
    public void AskLeave_BandChange_NamesTheBand()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);

        vm.AskLeave("40m", Mode.Wspr);

        vm.LeaveQuestion.Should().Be("Stop the beacon and change band to 40m?");
    }

    [Fact]
    public void AskLeave_ThenNo_KeepsTheBeacon()
    {
        var c = new Commands();
        var vm = new WsprViewModel(c, () => Slot);

        vm.AskLeave("20m", Mode.Ft8);
        vm.KeepBeaconCommand.Execute(null);
        vm.ConfirmLeaveCommand.Execute(null);

        vm.LeaveQuestion.Should().BeEmpty();
        c.Left.Should().BeEmpty();
    }

    [Fact]
    public void Apply_NotInWspr_HidesTheBeacon()
    {
        var vm = new WsprViewModel(new Commands(), () => Slot);
        vm.Apply(Snap(View(On())));

        vm.Apply(new SessionSnapshot { Mode = Mode.Ft8 });

        vm.BeaconOn.Should().BeFalse();
    }

    private sealed class CountingCommands(Commands inner, Action onWrite) : IWsprCommands
    {
        public WsprOptions Options => inner.Options;

        public Task<string?> StartBeaconAsync() => inner.StartBeaconAsync();

        public void StopBeacon() => inner.StopBeacon();

        public void ImHere() => inner.ImHere();

        public void UpdateOptions(Action<WsprOptions> change)
        {
            onWrite();
            inner.UpdateOptions(change);
        }

        public void LeaveWspr(string band, Mode mode) => inner.LeaveWspr(band, mode);
    }
}
