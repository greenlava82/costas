// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;

namespace Ft8Client.Rig.Flex;

/// <summary>
/// A receiver slice as the radio reports it in a <c>slice</c> status line
/// (<c>S…|slice 0 in_use=1 RF_frequency=14.074000 index_letter=A tx=1 dax=1 …</c>).
/// </summary>
/// <param name="Index">The slice number, 0 to 7: what Hamlib's SmartSDR Slice A to H models address.</param>
/// <param name="InUse">A client has the slice open.</param>
/// <param name="Transmit">The slice is the transmit slice.</param>
/// <param name="DaxChannel">The DAX channel its audio goes to, 0 when none.</param>
/// <param name="Letter">The letter a client shows for it, or empty.</param>
public sealed record FlexSlice(int Index, bool InUse, bool Transmit, int DaxChannel, string Letter)
{
    /// <summary>Reads a <c>slice</c> status line, or returns null for any other line.</summary>
    public static FlexSlice? Parse(string line)
    {
        if (line.Length < 2 || line[0] != 'S') return null;
        var bar = line.IndexOf('|', StringComparison.Ordinal);
        if (bar < 0) return null;
        var words = line[(bar + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words[0] != "slice" || !int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in words.Skip(2))
        {
            var eq = w.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0) values[w[..eq]] = w[(eq + 1)..];
        }
        bool Bit(string key) => values.TryGetValue(key, out var v) && v == "1";
        var dax = values.TryGetValue("dax", out var d) && int.TryParse(d, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
        return new FlexSlice(index, Bit("in_use"), Bit("tx"), dax, values.TryGetValue("index_letter", out var l) ? l : string.Empty);
    }

    /// <summary>
    /// Folds status lines into the latest state of each slice (a line can carry only the keys that changed, so an
    /// earlier value stays until a line changes it).
    /// </summary>
    public static IReadOnlyList<FlexSlice> FromLines(IEnumerable<string> lines)
    {
        var state = new SortedDictionary<int, Dictionary<string, string>>();
        foreach (var line in lines)
        {
            if (Parse(line) is not { } s) continue;
            var bar = line.IndexOf('|', StringComparison.Ordinal);
            if (!state.TryGetValue(s.Index, out var values)) state[s.Index] = values = new(StringComparer.OrdinalIgnoreCase);
            foreach (var w in line[(bar + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(2))
            {
                var eq = w.IndexOf('=', StringComparison.Ordinal);
                if (eq > 0) values[w[..eq]] = w[(eq + 1)..];
            }
        }
        return state.Select(kv => Parse($"S0|slice {kv.Key.ToString(CultureInfo.InvariantCulture)} {string.Join(' ', kv.Value.Select(p => $"{p.Key}={p.Value}"))}")!).ToList();
    }

    /// <summary>The slice to operate: the transmit slice if one is open, else the first open slice, else null.</summary>
    public static FlexSlice? Pick(IReadOnlyList<FlexSlice> slices)
    {
        var open = slices.Where(s => s.InUse && s.Index is >= 0 and <= 7).ToList();
        return open.FirstOrDefault(s => s.Transmit) ?? open.FirstOrDefault();
    }
}
