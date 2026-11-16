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
/// Windowed receive spectrum at 12 kHz, computed on the capture pump, never the audio callback. Frames overlap by three
/// quarters (a new one every <see cref="Hop"/> samples, about 43 ms), and each waterfall row is the average power of the
/// frames since the last row, as WSJT-X averages several spectra into each of its rows (its <c>WaterfallAvg</c>): the
/// noise comes out even and signals solid instead of speckled.
/// </summary>
public sealed class ReceiveSpectrum
{
    /// <summary>FFT length; frequency resolution is approximately 5.9 Hz.</summary>
    public const int Size = 2048;

    /// <summary>Samples between frames.</summary>
    public const int Hop = Size / 4;

    /// <summary>Most frames averaged into one row; older ones are dropped, so audio from long ago never smears in.</summary>
    public const int MaxAverage = 8;

    private const int Bins = Size / 4 + 1; // 0–3000 Hz
    private readonly float[] _samples = new float[Size];
    private readonly Complex[] _fft = new Complex[Size];
    private readonly double[] _power = new double[Bins];
    private readonly Lock _gate = new();
    private int _count;
    private int _frames;

    /// <summary>
    /// Takes the average spectrum of the frames since the last call, in dBFS, or null when no new frame arrived.
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
            if (_count != Size) continue;
            _count = Size - Hop;
            for (var i = 0; i < Size; i++)
                _fft[i] = new Complex(_samples[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1))), 0);
            // Keep the newest three quarters for the next frame.
            Array.Copy(_samples, Hop, _samples, 0, Size - Hop);
            for (int i = 1, j = 0; i < Size; i++)
            {
                var bit = Size >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) (_fft[i], _fft[j]) = (_fft[j], _fft[i]);
            }
            for (var length = 2; length <= Size; length <<= 1)
            {
                var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
                for (var start = 0; start < Size; start += length)
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
            lock (_gate)
            {
                if (_frames == MaxAverage)
                {
                    Array.Clear(_power);
                    _frames = 0;
                }
                for (var i = 0; i < Bins; i++)
                {
                    var amplitude = _fft[i].Magnitude * 4 / Size;
                    _power[i] += amplitude * amplitude;
                }
                _frames++;
            }
        }
    }
}
