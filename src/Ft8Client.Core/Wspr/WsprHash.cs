// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Buffers.Binary;
using System.Numerics;

namespace Ft8Client.Core.Wspr;

/// <summary>
/// The 15-bit callsign hash of a type 3 message. A port of <c>nhash</c> in WSJT-X 3.0.1 <c>lib/wsprd/nhash.c</c>
/// (Bob Jenkins' lookup3 <c>hashlittle</c>, public domain, with WSPR's initial value 146 and the result masked to 15 bits).
/// </summary>
public static class WsprHash
{
    /// <summary>The initial value WSPR hashes callsigns with.</summary>
    public const uint InitialValue = 146;

    /// <summary>The hash of an ASCII callsign: 0 to 32767.</summary>
    public static int Of(string callsign)
    {
        ArgumentNullException.ThrowIfNull(callsign);
        return (int)(Lookup3(System.Text.Encoding.ASCII.GetBytes(callsign), InitialValue) & 0x7FFF);
    }

    // Port of nhash(): the little-endian path of hashlittle. The 15-bit mask is applied by the caller of this method.
    private static uint Lookup3(ReadOnlySpan<byte> key, uint initval)
    {
        var length = key.Length;
        uint a, b, c;
        a = b = c = 0xdeadbeef + (uint)length + initval;
        var offset = 0;
        while (length > 12)
        {
            a += BinaryPrimitives.ReadUInt32LittleEndian(key[offset..]);
            b += BinaryPrimitives.ReadUInt32LittleEndian(key[(offset + 4)..]);
            c += BinaryPrimitives.ReadUInt32LittleEndian(key[(offset + 8)..]);
            Mix(ref a, ref b, ref c);
            length -= 12;
            offset += 12;
        }
        if (length == 0) return c;
        // The last, partial block: bytes 0 to 3 go into a, 4 to 7 into b, 8 to 11 into c, each little-endian.
        for (var i = 0; i < length; i++)
        {
            var word = (uint)key[offset + i] << (8 * (i % 4));
            if (i < 4) a += word;
            else if (i < 8) b += word;
            else c += word;
        }
        Final(ref a, ref b, ref c);
        return c;
    }

    private static void Mix(ref uint a, ref uint b, ref uint c)
    {
        a -= c; a ^= BitOperations.RotateLeft(c, 4); c += b;
        b -= a; b ^= BitOperations.RotateLeft(a, 6); a += c;
        c -= b; c ^= BitOperations.RotateLeft(b, 8); b += a;
        a -= c; a ^= BitOperations.RotateLeft(c, 16); c += b;
        b -= a; b ^= BitOperations.RotateLeft(a, 19); a += c;
        c -= b; c ^= BitOperations.RotateLeft(b, 4); b += a;
    }

    private static void Final(ref uint a, ref uint b, ref uint c)
    {
        c ^= b; c -= BitOperations.RotateLeft(b, 14);
        a ^= c; a -= BitOperations.RotateLeft(c, 11);
        b ^= a; b -= BitOperations.RotateLeft(a, 25);
        c ^= b; c -= BitOperations.RotateLeft(b, 16);
        a ^= c; a -= BitOperations.RotateLeft(c, 4);
        b ^= a; b -= BitOperations.RotateLeft(a, 14);
        c ^= b; c -= BitOperations.RotateLeft(b, 24);
    }
}
