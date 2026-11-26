// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Wspr;

namespace Ft8Client.Core.Tests;

public class WsprWaveformTests
{
    private static double Peak(float[] x) => x.Max(v => (double)Math.Abs(v));

    private static byte[] All(byte symbol) => Enumerable.Repeat(symbol, 162).ToArray();

    private static double Power(float[] x, int from, int count, double hz, int rate)
    {
        // Goertzel-style single-bin energy.
        double re = 0, im = 0;
        for (var i = from; i < from + count; i++)
        {
            var phase = 2 * Math.PI * hz * i / rate;
            re += x[i] * Math.Cos(phase);
            im += x[i] * Math.Sin(phase);
        }
        return (re * re + im * im) / ((double)count * count);
    }

    [Theory]
    [InlineData(12_000, 1_327_104)]
    [InlineData(48_000, 5_308_416)]
    [InlineData(24_000, 2_654_208)]
    public void Length_Rate_Is162SymbolsOf8192Samples(int rate, int expected) => WsprWaveform.Length(rate).Should().Be(expected);

    [Fact]
    public void Render_At44100_LengthIsWholeTransmissionToTheSample()
    {
        var w = WsprWaveform.Render(All(0), 1500, 44_100);
        w.Length.Should().Be(WsprWaveform.Length(44_100));
        (w.Length / 44_100.0).Should().BeApproximately(110.592, 0.001);
    }

    [Fact]
    public void ToneHz_FourTones_AreCentredOnTheOffsetAtTheToneSpacing()
    {
        WsprWaveform.ToneHz(0, 1500).Should().BeApproximately(1500 - 1.5 * 12000.0 / 8192, 1e-9);
        WsprWaveform.ToneHz(3, 1500).Should().BeApproximately(1500 + 1.5 * 12000.0 / 8192, 1e-9);
        (WsprWaveform.ToneHz(2, 1500) - WsprWaveform.ToneHz(1, 1500)).Should().BeApproximately(WsprTiming.ToneSpacingHz, 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Render_ConstantSymbol_PutsItsEnergyAtThatTone(byte symbol)
    {
        const int rate = 12_000;
        var w = WsprWaveform.Render(All(symbol), 1500, rate);
        var mid = w.Length / 2;
        var own = Power(w, mid, 24_000, WsprWaveform.ToneHz(symbol, 1500), rate);
        var other = Power(w, mid, 24_000, WsprWaveform.ToneHz(3 - symbol, 1500), rate);
        own.Should().BeGreaterThan(0.2 * 0.2 * 0.9, "a unit-amplitude sine has a bin power of 1/4");
        other.Should().BeLessThan(own / 100);
    }

    [Fact]
    public void Render_Attenuation_ScalesAmplitudeAsTenToTheDbOverTwenty()
    {
        var full = WsprWaveform.Render(All(1), 1500, 12_000);
        var down = WsprWaveform.Render(All(1), 1500, 12_000, attenuationDb: -20);

        (Peak(down) / Peak(full)).Should().BeApproximately(0.1, 0.002);
        Peak(full).Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void Render_PeakScalesTheWholeTransmission() =>
        Peak(WsprWaveform.Render(All(1), 1500, 12_000, 0, 0.25)).Should().BeApproximately(0.25, 0.001);

    [Fact]
    public void Render_Ends_RampFromAndToSilence()
    {
        var w = WsprWaveform.Render(All(2), 1500, 12_000);
        Math.Abs(w[0]).Should().BeLessThan(0.001f);
        Math.Abs(w[^1]).Should().BeLessThan(0.001f);
        Peak(w.Take(240).ToArray()).Should().BeGreaterThan(0.3, "the 20 ms ramp ends and the tone is at full level");
        Peak(w.Take(12).ToArray()).Should().BeLessThan(0.02);
    }

    [Fact]
    public void Render_AnyMessage_HasNoJumpsBetweenSamples()
    {
        var symbols = WsprEncoder.Encode("K1ABC FN42 37").Symbols;
        var w = WsprWaveform.Render(symbols, 1500, 12_000);
        var maxStep = 0.0;
        for (var i = 1; i < w.Length; i++) maxStep = Math.Max(maxStep, Math.Abs(w[i] - w[i - 1]));
        // A sine of frequency f changes by at most 2 pi f / rate per sample: continuous phase has no larger step.
        maxStep.Should().BeLessThan(2 * Math.PI * 1505 / 12_000 + 1e-3);
    }

    [Fact]
    public void Render_Symbols_ChangeToneAtTheSymbolBoundary()
    {
        var symbols = new byte[162];
        symbols[0] = 0;
        symbols[1] = 3;
        for (var i = 2; i < 162; i++) symbols[i] = 3;
        var w = WsprWaveform.Render(symbols, 1500, 12_000);
        // Symbol 0 runs samples 0 to 8191; its middle holds tone 0, and the next symbol's middle holds tone 3.
        Power(w, 2_000, 4_096, WsprWaveform.ToneHz(0, 1500), 12_000).Should().BeGreaterThan(Power(w, 2_000, 4_096, WsprWaveform.ToneHz(3, 1500), 12_000));
        Power(w, 10_000, 4_096, WsprWaveform.ToneHz(3, 1500), 12_000).Should().BeGreaterThan(Power(w, 10_000, 4_096, WsprWaveform.ToneHz(0, 1500), 12_000));
    }

    [Fact]
    public void Render_PositiveAttenuation_Throws() =>
        FluentActions.Invoking(() => WsprWaveform.Render(All(0), 1500, 12_000, attenuationDb: 3)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Render_WrongSymbols_Throws()
    {
        FluentActions.Invoking(() => WsprWaveform.Render(new byte[161], 1500, 12_000)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => WsprWaveform.Render(All(4), 1500, 12_000)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
