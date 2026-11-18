// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App;
using Ft8Client.Audio.Backends;
using Ft8Client.Audio.Dsp;
using Ft8Client.Audio.Timing;

namespace Ft8Client.App.Tests;

/// <summary>A capture pump made after start-up (a USB re-plug, a Setup input change) still feeds PSK and RTTY.</summary>
public class CapturePumpWiringTests
{
    [Fact]
    public void NewPump_KeyboardSessionRunning_FeedsItsContinuousStream()
    {
        var keyboard = new SpscRingBuffer(1024);
        using var input = new NullAudioBackend().OpenInput(NullAudioBackend.DeviceId, 48_000);

        var pump = AppHost.NewPump(input, new SlotRecorder(), keyboard);

        pump.Continuous.Should().BeSameAs(keyboard);
    }

    [Fact]
    public void NewPump_BeforeKeyboardSession_HasNoContinuousStream()
    {
        using var input = new NullAudioBackend().OpenInput(NullAudioBackend.DeviceId, 48_000);

        AppHost.NewPump(input, new SlotRecorder(), null).Continuous.Should().BeNull();
    }
}
