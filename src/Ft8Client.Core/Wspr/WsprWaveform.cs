// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>
/// The WSPR transmit audio: continuous-phase 4-FSK, four tones 12000/8192 Hz apart, 8192 samples (at 12 kHz) per symbol.
/// It is made once before the slot, never in an audio callback, and allocates only its output.
/// </summary>
public static class WsprWaveform
{
    /// <summary>Length of the raised-cosine ramp at each end, which keeps the key clicks out of the transmission.</summary>
    public static readonly TimeSpan RampLength = TimeSpan.FromMilliseconds(20);

    /// <summary>Number of samples a transmission takes at <paramref name="sampleRate"/>.</summary>
    public static int Length(int sampleRate) =>
        (int)Math.Round((double)WsprTiming.SymbolCount * WsprTiming.SymbolSamples12k * sampleRate / WsprTiming.SampleRate);

    /// <summary>The audio frequency of channel symbol <paramref name="symbol"/> for a transmission centred on <paramref name="offsetHz"/>.</summary>
    public static double ToneHz(int symbol, double offsetHz) => offsetHz + (symbol - 1.5) * WsprTiming.ToneSpacingHz;

    /// <summary>Renders a transmission to mono float samples.</summary>
    /// <param name="symbols">The 162 channel symbols, each 0 to 3.</param>
    /// <param name="offsetHz">Audio frequency of the centre of the four tones, as WSJT-X's Tx frequency is: tone <c>k</c> is at
    /// <c>offset + (k - 1.5) × 1.4648</c> Hz.</param>
    /// <param name="sampleRate">The output rate.</param>
    /// <param name="attenuationDb">Attenuation in dB, zero or negative, applied to the amplitude as <c>10^(dB/20)</c>.</param>
    /// <param name="peak">Amplitude at zero attenuation (1.0 = full scale).</param>
    public static float[] Render(ReadOnlySpan<byte> symbols, double offsetHz, int sampleRate, double attenuationDb = 0, double peak = 1.0)
    {
        if (symbols.Length != WsprTiming.SymbolCount) throw new ArgumentException("A WSPR transmission has 162 symbols.", nameof(symbols));
        if (attenuationDb > 0) throw new ArgumentOutOfRangeException(nameof(attenuationDb), attenuationDb, "Attenuation is zero or negative.");
        foreach (var s in symbols)
        {
            if (s > 3) throw new ArgumentOutOfRangeException(nameof(symbols), s, "A channel symbol is 0 to 3.");
        }
        var n = Length(sampleRate);
        var amplitude = peak * Math.Pow(10, attenuationDb / 20);
        var output = new float[n];
        var samplesPerSymbol = (double)WsprTiming.SymbolSamples12k * sampleRate / WsprTiming.SampleRate;
        var phase = 0.0;
        var twoPiOverRate = 2 * Math.PI / sampleRate;
        for (var k = 0; k < n; k++)
        {
            var sym = Math.Min((int)(k / samplesPerSymbol), symbols.Length - 1);
            output[k] = (float)(amplitude * Math.Sin(phase));
            phase += twoPiOverRate * ToneHz(symbols[sym], offsetHz);
            if (phase > 2 * Math.PI) phase -= 2 * Math.PI;
        }
        var ramp = Math.Min((int)(RampLength.TotalSeconds * sampleRate), n / 2);
        for (var i = 0; i < ramp; i++)
        {
            var env = (float)((1 - Math.Cos(Math.PI * i / ramp)) / 2);
            output[i] *= env;
            output[n - 1 - i] *= env;
        }
        return output;
    }
}
