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
    private static float[] Tones(int length, params (double Hz, double Amplitude)[] tones) =>
        Enumerable.Range(0, length).Select(i => (float)tones.Sum(t => t.Amplitude * Math.Sin(2 * Math.PI * t.Hz * i / ReceiveSpectrum.SampleRate))).ToArray();

    private static int Bin(double hz) => (int)Math.Round(hz / ReceiveSpectrum.BinHz);

    [Theory]
    [InlineData(600)]
    [InlineData(1500)]
    [InlineData(2800)]
    public void Append_Tone_PeakMatchesFrequency(int hz)
    {
        var spectrum = new ReceiveSpectrum();
        var samples = Tones(ReceiveSpectrum.FftSize, (hz, 0.5));
        // Audio arrives in chunks, not aligned to FFT frames.
        spectrum.Append(samples.AsSpan(0, 357));
        spectrum.TakeLatest().Should().BeNull();
        spectrum.Append(samples.AsSpan(357));
        var row = spectrum.TakeLatest()!;
        row.Should().HaveCount(ReceiveSpectrum.Bins);
        var peak = Array.IndexOf(row, row.Max());
        (peak * ReceiveSpectrum.BinHz).Should().BeApproximately(hz, ReceiveSpectrum.BinHz);
        row[peak].Should().BeApproximately(-6, 2, "a sine of amplitude 0.5 is 6 dB below full scale");
        spectrum.TakeLatest().Should().BeNull();
    }

    [Fact]
    public void Append_StrongTone_NoLeakage20HzAway()
    {
        // Each FT8 signal keeps sharp edges: a Hann window left a strong signal's skirt 30 to 40 dB down at this distance.
        var spectrum = new ReceiveSpectrum();
        spectrum.Append(Tones(ReceiveSpectrum.FftSize, (1500, 0.5)));
        var row = spectrum.TakeLatest()!;
        var peak = row[Bin(1500)];
        row[Bin(1480)].Should().BeLessThan(peak - 80);
        row[Bin(1520)].Should().BeLessThan(peak - 80);
    }

    [Fact]
    public void Append_TwoTones30HzApart_DarkGapBetween()
    {
        var spectrum = new ReceiveSpectrum();
        spectrum.Append(Tones(ReceiveSpectrum.FftSize, (1500, 0.3), (1530, 0.3)));
        var row = spectrum.TakeLatest()!;
        row[Bin(1515)].Should().BeLessThan(row[Bin(1500)] - 60, "two stations side by side stay two");
    }

    [Fact]
    public void Append_OverlappingFrames_NewFrameEveryHop()
    {
        var spectrum = new ReceiveSpectrum();
        spectrum.Append(new float[ReceiveSpectrum.FftSize]);
        spectrum.TakeLatest().Should().NotBeNull();
        spectrum.Append(new float[ReceiveSpectrum.Hop - 1]);
        spectrum.TakeLatest().Should().BeNull();
        spectrum.Append(new float[1]);
        spectrum.TakeLatest().Should().NotBeNull();
    }

    [Fact]
    public void TakeLatest_SeveralFrames_AveragesTheirPower()
    {
        // A frame of tone then six hops of silence: seven frames hold less and less of the tone, so their average sits
        // below the full tone's level (−6 dB) but far above silence.
        var spectrum = new ReceiveSpectrum();
        var samples = Tones(ReceiveSpectrum.FftSize, (1500, 0.5)).Concat(new float[6 * ReceiveSpectrum.Hop]).ToArray();
        spectrum.Append(samples);
        spectrum.TakeLatest()![Bin(1500)].Should().BeLessThan(-6.5f).And.BeGreaterThan(-15);
    }

    [Fact]
    public void TakeLatest_NotTakenForLong_KeepsOnlyRecentFrames()
    {
        // A tone, then much more silence than MaxAverage frames: the row must show silence, not a smear of the tone.
        var spectrum = new ReceiveSpectrum();
        spectrum.Append(Tones(ReceiveSpectrum.FftSize, (1500, 0.5)));
        spectrum.Append(new float[ReceiveSpectrum.FftSize * 2 + ReceiveSpectrum.Hop * ReceiveSpectrum.MaxAverage]);
        spectrum.TakeLatest()!.Max().Should().BeLessThan(-100);
    }

    [Fact]
    public void Append_Silence_ProducesFiniteFloor()
    {
        var spectrum = new ReceiveSpectrum();
        spectrum.Append(new float[ReceiveSpectrum.FftSize]);
        spectrum.TakeLatest().Should().OnlyContain(v => float.IsFinite(v) && v == -120);
    }
}
