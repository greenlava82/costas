// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>
/// The bit-reversal interleaver of WSPR: bit <c>p</c> of the coded stream goes to the position whose 8-bit index is the
/// reverse of a counter, skipping positions of 162 or more. A port of <c>interleave</c> in WSJT-X 3.0.1
/// <c>lib/wsprd/wsprsim_utils.c</c> (GPLv3).
/// </summary>
public static class WsprInterleaver
{
    /// <summary>Interleaves the first 162 elements of <paramref name="bits"/> in place.</summary>
    public static void Interleave(Span<byte> bits)
    {
        if (bits.Length < WsprTiming.SymbolCount) throw new ArgumentException("An interleaver needs 162 bits.", nameof(bits));
        Span<byte> tmp = stackalloc byte[WsprTiming.SymbolCount];
        var p = 0;
        for (var i = 0; i < 256 && p < WsprTiming.SymbolCount; i++)
        {
            var j = Reverse8(i);
            if (j < WsprTiming.SymbolCount) tmp[j] = bits[p++];
        }
        tmp.CopyTo(bits);
    }

    /// <summary>The 8-bit reversal of <paramref name="i"/> (bit 0 becomes bit 7).</summary>
    public static int Reverse8(int i)
    {
        var r = 0;
        for (var b = 0; b < 8; b++) r |= ((i >> b) & 1) << (7 - b);
        return r;
    }
}
