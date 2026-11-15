// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using FluentAssertions;
using Ft8Client.Core.Time;
using Ft8Client.Rig;
using Xunit;

namespace Ft8Client.Rig.Tests;

public class RigTuningTests
{
    [Fact]
    public async Task TuneAsync_ModeChangeMovesDial_EndsOnTheAskedFrequencyInPktUsb()
    {
        var ct = TestContext.Current.CancellationToken;
        var rig = new SimulatedRig(SystemClock.Instance, 14_074_000);
        await rig.SetModeAsync(RigMode.Usb, ct);
        rig.ModeShiftHz = -700;

        await RigTuning.TuneAsync(rig, 7_074_000, RigMode.PktUsb, ct);

        var state = await rig.GetStateAsync(ct);
        state.FrequencyHz.Should().Be(7_074_000);
        state.Mode.Should().Be("PKTUSB");
    }

    [Fact]
    public async Task TuneAsync_ModeAlreadySet_LeavesDialOnFrequency()
    {
        var ct = TestContext.Current.CancellationToken;
        var rig = new SimulatedRig(SystemClock.Instance, 14_074_000) { ModeShiftHz = -700 };
        await rig.SetModeAsync(RigMode.PktUsb, ct);

        await RigTuning.TuneAsync(rig, 7_074_000, RigMode.PktUsb, ct);

        (await rig.GetStateAsync(ct)).FrequencyHz.Should().Be(7_074_000);
    }
}
