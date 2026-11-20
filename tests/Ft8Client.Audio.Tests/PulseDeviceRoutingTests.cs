// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Audio.Backends;

namespace Ft8Client.Audio.Tests;

/// <summary>
/// A PulseAudio sink and source picked by name get the audio, not the default devices: what an SDR client's virtual
/// DAX devices need (RimuDAX's rimu.dax.tx and rimu.dax.rx1). Runs only when COSTAS_PULSE_ROUTE names a sink and a
/// source fed by it, as "sink|source" (in CI, a null sink and a remap of its monitor), with ALSA's "pulse" device
/// defaulting somewhere else.
/// </summary>
// Shares the sound server with the loopback test, whose tone on the default device would reach the fallback input.
[Collection("Sound server")]
public class PulseDeviceRoutingTests
{
    [Fact]
    public async Task PlayToNamedSink_ArrivesOnNamedSource_NotOnDefault()
    {
        var route = Environment.GetEnvironmentVariable("COSTAS_PULSE_ROUTE")?.Split('|');
        Assert.SkipWhen(!OperatingSystem.IsLinux() || route is not { Length: 2 }, "No PulseAudio route (COSTAS_PULSE_ROUTE).");
        if (!OperatingSystem.IsLinux()) return;

        var backend = PortAudioBackend.TryCreate(out var why);
        backend.Should().NotBeNull(why);
        var sinkId = PulseDevices.Prefix + route![0];
        var sourceId = PulseDevices.Prefix + route[1];
        backend!.ListOutputs().Select(d => d.Id).Should().Contain(sinkId);
        backend.ListInputs().Select(d => d.Id).Should().Contain(sourceId);

        using var output = backend.OpenOutput(sinkId, 48_000);
        using var named = backend.OpenInput(sourceId, 48_000);
        using var fallback = backend.OpenInput("ALSA:pulse", 48_000);
        named.Start();
        fallback.Start();
        await Task.Delay(500, TestContext.Current.CancellationToken);
        named.Buffer.Clear();
        fallback.Buffer.Clear();

        var tone = new float[output.SampleRate * 2];
        for (var i = 0; i < tone.Length; i++) tone[i] = 0.3f * MathF.Sin(2 * MathF.PI * 1000 * i / output.SampleRate);
        output.Play(tone);
        await Task.Delay(2500, TestContext.Current.CancellationToken);

        Rms(named).Should().BeGreaterThan(0.05, "the tone played to the named sink comes back on the named source");
        Rms(fallback).Should().BeLessThan(0.01, "the default device hears none of it");
    }

    private static double Rms(IAudioInput input)
    {
        var buf = new float[input.SampleRate * 4];
        var n = input.Buffer.Read(buf);
        double sum = 0;
        for (var i = 0; i < n; i++) sum += buf[i] * buf[i];
        return n == 0 ? 0 : Math.Sqrt(sum / n);
    }
}
