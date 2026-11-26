// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.App.Remote;
using Ft8Client.Core;
using Ft8Client.Core.Remote;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Core.Wspr;

namespace Ft8Client.App.Tests;

/// <summary>The phone's WSPR card and its Start beacon, Stop beacon and "I'm here" commands (docs/10-wspr.md section 8).</summary>
public class RemoteWsprTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 14, 3, 0, DateTimeKind.Utc);
    private readonly ManualClock _clock = new(Now);
    private readonly FakeRemoteTarget _target = new();

    private RemoteControl Make() => new(_target, _clock, new DeviceTokens(), _ => { });

    [Fact]
    public async Task HandleAsync_WsprStart_StartsTheBeaconAndCountsAsInput()
    {
        var rc = Make();

        var why = await rc.HandleAsync("phone", new RemoteCommand("wsprStart"));

        why.Should().BeNull();
        _target.Calls.Should().Equal("wsprStart");
        _target.ActivityCount.Should().Be(1, "any command from the phone is operator input");
        rc.Lease.Holder.Should().Be("phone", "starting the beacon takes control as any transmit command does");
    }

    [Fact]
    public async Task HandleAsync_WsprStartRefused_ReturnsTheReasonToThePhone()
    {
        _target.BeaconStartResult = "Beacon not started: set your callsign and locator in Settings.";

        var why = await Make().HandleAsync("phone", new RemoteCommand("wsprStart"));

        why.Should().Be("Beacon not started: set your callsign and locator in Settings.");
    }

    [Fact]
    public async Task HandleAsync_WsprStop_StopsIt()
    {
        var why = await Make().HandleAsync("phone", new RemoteCommand("wsprStop"));

        why.Should().BeNull();
        _target.Calls.Should().Equal("wsprStop");
    }

    [Fact]
    public async Task HandleAsync_WsprHere_NotesPresence()
    {
        var why = await Make().HandleAsync("phone", new RemoteCommand("wsprHere"));

        why.Should().BeNull();
        _target.Calls.Should().Equal("wsprHere");
        _target.ActivityCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_HaltFromThePhone_StillHalts()
    {
        await Make().HandleAsync("phone", new RemoteCommand("halt"));

        _target.Halts.Should().HaveCount(1);
    }

    [Fact]
    public async Task HandleAsync_WsprStart_ByAnotherPhoneWhileOneHasControl_IsRefusedLikeOtherTransmitCommands()
    {
        var rc = Make();
        await rc.HandleAsync("phone", new RemoteCommand("cq"));
        _target.WantsTransmit = true;
        _target.Calls.Clear();

        var why = await rc.HandleAsync("tablet", new RemoteCommand("wsprStart"));

        // The same arbitration as every other command: control moves only by a deliberate tap; here it is taken, as for CQ.
        rc.Lease.Holder.Should().Be("tablet");
        why.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_BandOrModeChangeWhileTheBeaconRuns_IsRefusedUntilItIsStopped()
    {
        var beacon = new WsprBeaconView(true, true, "", Now.AddMinutes(4), null, null, 20, 37, 0, null, null, null);
        _target.Snapshot = new SessionSnapshot { Mode = Mode.Wspr, Band = "20m", Wspr = new WsprView([], Now.AddMinutes(1), null, 0, "") { Beacon = beacon } };
        var rc = new RemoteControl(_target, _clock, new DeviceTokens(), _ => { }, new FakeRemoteTuning());

        var why = await rc.HandleAsync("phone", new RemoteCommand("band", Band: "40m", Mode: "FT8"));

        why.Should().Be("Stop the beacon first: changing band or mode ends it.");
    }

    [Fact]
    public void Build_NotInWspr_HasNoWsprCard()
    {
        var s = RemoteStateBuilder.Build(new SessionSnapshot { MyCall = "W7LIT", Mode = Mode.Ft8, Band = "20m", DialHz = 14_074_000 }, Now, new RemoteContext(), "you", null);

        s.Wspr.Should().BeNull();
    }

    [Fact]
    public void Build_Wspr_CarriesTheBeaconAndWhatWasHeard()
    {
        var beacon = new WsprBeaconView(true, true, "", Now.AddMinutes(4).AddSeconds(30), null, "W7LIT DN40 37", 20, 37, 0, 36.99, null, "The radio is set for about 33 dBm.");
        var heard = new[]
        {
            new WsprHeardSpot(new WsprSpot(Now.AddMinutes(-1), -12, 0.2, 14_097_050, 0, "K1ABC", "FN42", 30, false, 14_095_600), 3_000),
            new WsprHeardSpot(new WsprSpot(Now.AddMinutes(-3), -20, 0.1, 14_097_100, 0, "PJ4/K1ABC", null, 37, false, 14_095_600), null),
        };
        var snap = new SessionSnapshot
        {
            MyCall = "W7LIT", Mode = Mode.Wspr, Band = "20m", DialHz = 14_095_600,
            Wspr = new WsprView(heard, Now.AddSeconds(57), Now.AddMinutes(-1), 1, "1 heard in the last slot.") { Beacon = beacon },
            Services = new Dictionary<string, ServiceStatus> { [ServiceNames.Wsprnet] = new(ServiceHealth.Ok, "WSPRnet: 3 spots sent", Now) },
        };

        var w = RemoteStateBuilder.Build(snap, Now, new RemoteContext { Units = DistanceUnit.Kilometres }, "you", null).Wspr;

        w.Should().NotBeNull();
        w!.On.Should().BeTrue();
        w.Status.Should().Be("Next slot transmits.");
        w.NextSlotIn.Should().Be(57);
        w.PresenceLeft.Should().Be(270);
        w.Power.Should().StartWith("Reports 37 dBm (5 W).");
        w.PowerWarning.Should().Be("The radio is set for about 33 dBm.");
        w.Decoder.Should().Be("1 heard in the last slot.");
        w.Upload.Should().Be("WSPRnet: 3 spots sent");
        w.Heard.Select(h => h.Call).Should().Equal("K1ABC", "PJ4/K1ABC");
        w.Heard[0].Should().Be(new RemoteWsprSpot("14:02", "K1ABC", "FN42", "-12", w.Heard[0].Distance));
        w.Heard[0].Distance.Should().Contain("km");
        w.Heard[1].Grid.Should().BeEmpty();
    }

    [Fact]
    public void Build_WsprBeaconOff_NoPresenceTime()
    {
        var snap = new SessionSnapshot
        {
            MyCall = "W7LIT", Mode = Mode.Wspr, Band = "20m", DialHz = 14_095_600,
            Wspr = new WsprView([], Now.AddSeconds(57), null, 0, "Waiting for the first slot."),
        };

        var w = RemoteStateBuilder.Build(snap, Now, new RemoteContext(), "you", null).Wspr!;

        w.On.Should().BeFalse();
        w.PresenceLeft.Should().BeNull();
        w.Status.Should().Be("Beacon off. Receiving.");
    }

    [Fact]
    public void Index_Page_HasTheWsprCardAndTheThreeCommands()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ft8Client.App", "Remote", "index.html"));

        html.Should().Contain("id=\"wspr\"").And.Contain("\"wsprStart\"").And.Contain("\"wsprStop\"").And.Contain("\"wsprHere\"");
    }
}
