// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
namespace Ft8Client.App.Services;

/// <summary>
/// Adds up full FT8/FT4 decodes for the "decode_health" usage event: per mode, how many slots were decoded, how many
/// failed, the average number of decodes and the median time a decode took. Reported once an hour of slots has been
/// seen, then counted again. Counts and times only.
/// </summary>
public sealed class DecodeHealthStats
{
    /// <summary>Slots in a report: an hour of FT8.</summary>
    public const int SlotsPerReport = 240;

    private readonly Dictionary<string, Bucket> _byMode = new(StringComparer.Ordinal);

    /// <summary>
    /// Adds one slot. Returns the properties to send when this slot completes a report for its mode, otherwise null.
    /// </summary>
    public Dictionary<string, object>? Add(string mode, int decodes, TimeSpan elapsed, bool succeeded)
    {
        lock (_byMode)
        {
            if (!_byMode.TryGetValue(mode, out var b)) _byMode[mode] = b = new Bucket();
            b.Slots++;
            if (succeeded)
            {
                b.Decodes += decodes;
                b.Millis.Add((int)Math.Min(int.MaxValue, elapsed.TotalMilliseconds));
            }
            else
            {
                b.Failed++;
            }
            if (b.Slots < SlotsPerReport) return null;
            _byMode.Remove(mode);
            return Report(mode, b);
        }
    }

    private static Dictionary<string, object> Report(string mode, Bucket b)
    {
        var ok = b.Slots - b.Failed;
        var p = new Dictionary<string, object>
        {
            ["mode"] = mode,
            ["slots"] = b.Slots,
            ["failed_pct"] = (int)Math.Round(100.0 * b.Failed / b.Slots),
            ["avg_decodes"] = ok == 0 ? 0 : (int)Math.Round((double)b.Decodes / ok),
        };
        if (b.Millis.Count > 0)
        {
            b.Millis.Sort();
            // Rounded to a tenth of a second: enough to see a slow PC, nothing more.
            p["median_decode_ms"] = (int)(Math.Round(b.Millis[b.Millis.Count / 2] / 100.0) * 100);
        }
        return p;
    }

    private sealed class Bucket
    {
        public int Slots;
        public int Failed;
        public long Decodes;
        public List<int> Millis { get; } = [];
    }
}
