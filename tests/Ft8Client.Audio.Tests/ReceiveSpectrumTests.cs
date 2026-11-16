// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Audio.Dsp;

namespace Ft8Client.Audio.Tests;

public class ReceiveSpectrumTests
{
    [Theory]
    [InlineData(600)]
    [InlineData(1500)]
    [InlineData(2800)]
    public void Append_Tone_PeakMatchesFrequency(int hz)
    {
        var spectrum = new ReceiveSpectrum();
        var samples = Enumerable.Range(0, ReceiveSpectrum.Size)
            .Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * hz * i / 12000))).ToArray();
        // Audio arrives in chunks, not aligned to FFT frames.
        spectrum.Append(samples.AsSpan(0, 357));
        spectrum.TakeLatest().Should().BeNull();
        spectrum.Append(samples.AsSpan(357));
        var row = spectrum.TakeLatest()!;
        var peak = Array.IndexOf(row, row.Max());
        (peak * 12000.0 / ReceiveSpectrum.Size).Should().BeApproximately(hz, 6);
        row[peak].Should().BeApproximately(-6, 2);
        spectrum.TakeLatest().Should().BeNull();
    }

    [Fact]
    public void Append_OverlappingFrames_NewFrameEveryHop()
    {
        var spectrum = new ReceiveSpectrum();
        spectrum.Append(new float[ReceiveSpectrum.Size]);
        spectrum.TakeLatest().Should().NotBeNull();
        spectrum.Append(new float[ReceiveSpectrum.Hop - 1]);
        spectrum.TakeLatest().Should().BeNull();
        spectrum.Append(new float[1]);
        spectrum.TakeLatest().Should().NotBeNull();
    }

    [Fact]
    public void TakeLatest_SeveralFrames_AveragesTheirPower()
    {
        // A full frame of tone then half a frame of silence: three frames hold all, three quarters and half of the tone,
        // so their average sits below the full tone's level (−6 dB) but far above silence.
        var spectrum = new ReceiveSpectrum();
        var samples = Enumerable.Range(0, ReceiveSpectrum.Size + 2 * ReceiveSpectrum.Hop)
            .Select(i => i < ReceiveSpectrum.Size ? (float)(0.5 * Math.Sin(2 * Math.PI * 1500 * i / 12000)) : 0f).ToArray();
        spectrum.Append(samples);
        var row = spectrum.TakeLatest()!;
        var bin = (int)Math.Round(1500.0 * ReceiveSpectrum.Size / 12000);
        row[bin].Should().BeLessThan(-6.5f).And.BeGreaterThan(-15);
    }

    [Fact]
    public void TakeLatest_NotTakenForLong_KeepsOnlyRecentFrames()
    {
        // A tone, then much more silence than MaxAverage frames: the row must show silence, not a smear of the tone.
        var spectrum = new ReceiveSpectrum();
        spectrum.Append(Enumerable.Range(0, ReceiveSpectrum.Size).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 1500 * i / 12000))).ToArray());
        spectrum.Append(new float[ReceiveSpectrum.Size * 2 + ReceiveSpectrum.Hop * ReceiveSpectrum.MaxAverage]);
        spectrum.TakeLatest()!.Max().Should().BeLessThan(-100);
    }

    [Fact]
    public void Append_Silence_ProducesFiniteFloor()
    {
        var spectrum = new ReceiveSpectrum();
        spectrum.Append(new float[ReceiveSpectrum.Size]);
        spectrum.TakeLatest().Should().OnlyContain(v => float.IsFinite(v) && v == -120);
    }
}
