// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Rig.Flex;

/// <summary>
/// One <c>amplifier</c> status line from the radio's SmartSDR TCP API, <c>S&lt;client&gt;|amplifier &lt;handle&gt; key=value …</c>
/// (SmartSDR API wiki, "Status"; FlexLib <c>Radio.cs</c> <c>ParseAmplifierStatus</c>). A TunerGenius XL reports through
/// the same object as the amplifiers. A line can carry only the keys that changed, or the word <c>removed</c> when
/// the device has gone.
/// </summary>
/// <param name="Handle">The device's handle, <c>0x18E5F0AA</c>.</param>
/// <param name="Values">The keys given, lower case, with their values.</param>
/// <param name="Removed">True when the line says the device has gone.</param>
public sealed record FlexStatusLine(string Handle, IReadOnlyDictionary<string, string> Values, bool Removed)
{
    /// <summary>
    /// Reads an <c>amplifier</c> status line, or returns null for any other line (replies, messages, other objects,
    /// a handle that is not hexadecimal).
    /// </summary>
    public static FlexStatusLine? Parse(string line)
    {
        if (line.Length < 2 || line[0] != 'S') return null;
        var bar = line.IndexOf('|', StringComparison.Ordinal);
        if (bar < 0) return null;
        var words = line[(bar + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words[0] != "amplifier" || !FlexCommands.IsHandle(words[1])) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var removed = false;
        foreach (var w in words.Skip(2))
        {
            var eq = w.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                if (w == "removed") removed = true;
                continue;
            }
            if (eq > 0) values[w[..eq].ToLowerInvariant()] = w[(eq + 1)..];
        }
        return new FlexStatusLine(words[1], values, removed);
    }
}
