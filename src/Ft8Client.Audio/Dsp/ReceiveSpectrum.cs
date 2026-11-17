// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Numerics;

namespace Ft8Client.Audio.Dsp;

/// <summary>
/// Receive spectrum for the waterfall at 12 kHz, computed on the capture pump, never the audio callback. Built the way
/// WSJT-X builds its wide graph (<c>lib/symspec.f90</c>, <c>widgets/widegraph.cpp</c>): a long FFT with a Nuttall
/// window, whose sidelobes are about 93 dB down, so each signal keeps sharp edges and the gap between two close signals
/// stays dark; adjacent bins are then combined (keeping the strongest) to about one per screen pixel. Frames come every <see cref="Hop"/>
/// samples (85 ms) and each row is the average power of the frames since the last row, as WSJT-X averages several
/// spectra into each of its rows (<c>WaterfallAvg</c>), so the noise is even and signals solid.
/// </summary>
public sealed class ReceiveSpectrum
{
    /// <summary>Sample rate of the audio fed in.</summary>
    public const int SampleRate = 12_000;

    /// <summary>FFT length: 0.68 s, 1.46 Hz bins. WSJT-X uses 16384 (1.37 s); half keeps signal starts sharper.</summary>
    public const int FftSize = 8192;

    /// <summary>FFT bins combined into one row bin.</summary>
    public const int Combine = 2;

    /// <summary>Hertz per row bin (about 2.9).</summary>
    public const double BinHz = (double)SampleRate * Combine / FftSize;

    /// <summary>Bins in a row, covering 0–3000 Hz.</summary>
    public const int Bins = FftSize / 4 / Combine + 1;

    /// <summary>Samples between frames.</summary>
    public const int Hop = 1024;

    /// <summary>Most frames averaged into one row; older ones are dropped, so audio from long ago never smears in.</summary>
    public const int MaxAverage = 8;

    // WSJT-X lib/nuttal_window.f90 (periodic, four terms).
    private static readonly double[] Window = Enumerable.Range(0, FftSize).Select(i =>
        0.3635819 - 0.4891775 * Math.Cos(2 * Math.PI * i / FftSize) + 0.1365995 * Math.Cos(4 * Math.PI * i / FftSize)
        - 0.0106411 * Math.Cos(6 * Math.PI * i / FftSize)).ToArray();

    // A full-scale sine reads 0 dBFS: the window's coherent gain is its first coefficient.
    private const double Scale = 2.0 / (FftSize * 0.3635819);

    private readonly float[] _samples = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly double[] _power = new double[Bins];
    private readonly Lock _gate = new();
    private int _count;
    private int _frames;

    /// <summary>
    /// Takes the average spectrum of the frames since the last call, in dBFS, <see cref="Bins"/> bins of
    /// <see cref="BinHz"/> from 0 Hz, or null when no new frame arrived.
    /// </summary>
    public float[]? TakeLatest()
    {
        lock (_gate)
        {
            if (_frames == 0) return null;
            var row = new float[Bins];
            for (var i = 0; i < Bins; i++)
            {
                row[i] = (float)(10 * Math.Log10(Math.Max(1e-12, _power[i] / _frames)));
                _power[i] = 0;
            }
            _frames = 0;
            return row;
        }
    }

    /// <summary>Feeds resampled audio from the capture pump.</summary>
    public void Append(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            _samples[_count++] = sample;
            if (_count != FftSize) continue;
            _count = FftSize - Hop;
            for (var i = 0; i < FftSize; i++) _fft[i] = new Complex(_samples[i] * Window[i], 0);
            // Keep all but the oldest hop for the next frame.
            Array.Copy(_samples, Hop, _samples, 0, FftSize - Hop);
            Transform();
            lock (_gate)
            {
                if (_frames == MaxAverage)
                {
                    Array.Clear(_power);
                    _frames = 0;
                }
                for (var i = 0; i < Bins; i++)
                {
                    // The strongest of the combined bins, so a tone reads within 1 dB of its level wherever it falls
                    // (a mean reads up to 3 dB low when the tone sits on one bin).
                    double max = 0;
                    for (var k = 0; k < Combine; k++)
                    {
                        var j = Math.Min(i * Combine + k, FftSize / 2);
                        var amplitude = _fft[j].Magnitude * Scale;
                        max = Math.Max(max, amplitude * amplitude);
                    }
                    _power[i] += max;
                }
                _frames++;
            }
        }
    }

    private void Transform()
    {
        for (int i = 1, j = 0; i < FftSize; i++)
        {
            var bit = FftSize >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (_fft[i], _fft[j]) = (_fft[j], _fft[i]);
        }
        for (var length = 2; length <= FftSize; length <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (var start = 0; start < FftSize; start += length)
            {
                var phase = Complex.One;
                for (var k = 0; k < length / 2; k++)
                {
                    var a = _fft[start + k];
                    var b = phase * _fft[start + k + length / 2];
                    _fft[start + k] = a + b;
                    _fft[start + k + length / 2] = a - b;
                    phase *= step;
                }
            }
        }
    }
}
