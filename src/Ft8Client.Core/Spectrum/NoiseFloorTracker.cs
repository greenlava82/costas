// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Spectrum;

/// <summary>
/// Follows the noise floor of the receive spectrum, so the waterfall can be coloured relative to the noise rather than
/// to full scale and look the same whatever the audio level. Each row's floor is a low percentile of its bins, which
/// stays on the noise when a busy band fills much of the passband with signals; it is smoothed over a couple of seconds
/// so the background does not flicker. Pure; fed once a row on the UI thread.
/// </summary>
public sealed class NoiseFloorTracker
{
    /// <summary>The fraction of bins below the floor estimate: low, so signals covering much of the band do not lift it.</summary>
    public const double Percentile = 0.3;

    /// <summary>How far each row moves the floor towards its own estimate; at five rows a second this follows in about 2 s.</summary>
    public const double Smoothing = 0.1;

    private float[] _scratch = [];

    /// <summary>The smoothed floor in the row's units (dBFS), or null before the first row.</summary>
    public double? Floor { get; private set; }

    /// <summary>Takes a row and returns the updated floor.</summary>
    /// <param name="row">Levels in dB, one per bin.</param>
    /// <param name="firstBin">First bin of the range that carries receive audio.</param>
    /// <param name="lastBin">Last bin of that range, inclusive; clamped to the row.</param>
    public double Update(ReadOnlySpan<float> row, int firstBin, int lastBin)
    {
        firstBin = Math.Clamp(firstBin, 0, Math.Max(0, row.Length - 1));
        lastBin = Math.Clamp(lastBin, firstBin, Math.Max(0, row.Length - 1));
        var count = lastBin - firstBin + 1;
        if (row.Length == 0) return Floor ?? 0;
        if (_scratch.Length < count) _scratch = new float[count];
        var span = _scratch.AsSpan(0, count);
        row.Slice(firstBin, count).CopyTo(span);
        span.Sort();
        double estimate = span[(int)(Percentile * (count - 1))];
        Floor = Floor is { } f ? f + (estimate - f) * Smoothing : estimate;
        return Floor.Value;
    }

    /// <summary>Copies another tracker's floor, so a newly shown waterfall starts on the same scale.</summary>
    public void CopyFrom(NoiseFloorTracker other) => Floor = other.Floor;

    /// <summary>Forgets the floor, for a band or receive device change.</summary>
    public void Reset() => Floor = null;
}
