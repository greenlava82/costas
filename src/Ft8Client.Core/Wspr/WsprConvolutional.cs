// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Numerics;

namespace Ft8Client.Core.Wspr;

/// <summary>
/// The rate 1/2, constraint length 32 convolutional encoder of WSPR (Layland-Lushbaugh polynomials). A port of
/// <c>encode</c> and the <c>ENCODE</c> macro in WSJT-X 3.0.1 <c>lib/wsprd/fano.c</c> and <c>fano.h</c> (GPLv3).
/// </summary>
public static class WsprConvolutional
{
    private const uint Poly1 = 0xf2d05351;
    private const uint Poly2 = 0xe4613c47;

    /// <summary>
    /// Encodes <paramref name="data"/>, high bit first: two bits per input bit (the first from polynomial 1, the second from
    /// polynomial 2), one bit per element of the result, so 11 bytes give 176 bits of which a message uses the first 162.
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<byte> data)
    {
        var symbols = new byte[data.Length * 8 * 2];
        uint state = 0;
        var k = 0;
        foreach (var b in data)
        {
            for (var i = 7; i >= 0; i--)
            {
                state = (state << 1) | (uint)((b >> i) & 1);
                symbols[k++] = (byte)(BitOperations.PopCount(state & Poly1) & 1);
                symbols[k++] = (byte)(BitOperations.PopCount(state & Poly2) & 1);
            }
        }
        return symbols;
    }
}
