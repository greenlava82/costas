// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>The outcome of encoding a WSPR message.</summary>
/// <param name="Symbols">The 162 channel symbols, each 0 to 3; empty when the message was refused.</param>
/// <param name="Message">The message as it will be received (<see cref="WsprMessage.Text"/>).</param>
/// <param name="Refusal">Why the message cannot be sent exactly as shown, or null.</param>
public sealed record WsprEncodeResult(byte[] Symbols, string Message, string? Refusal)
{
    /// <summary>True when <see cref="Symbols"/> holds the message.</summary>
    public bool Ok => Refusal is null;
}

/// <summary>
/// Encodes a WSPR message to its 162 channel symbols: packs the 50 bits (<see cref="WsprPacker"/>), adds the 31-bit tail,
/// encodes with the rate 1/2 convolutional code (<see cref="WsprConvolutional"/>), interleaves
/// (<see cref="WsprInterleaver"/>) and combines each bit with the sync vector (<see cref="WsprSync"/>): symbol = 2 ×
/// bit + sync. Checked against <c>wsprsim -c</c> from WSJT-X 3.0.1 (tests/Ft8Client.Core.Tests/Vectors/wspr.tsv).
/// </summary>
public static class WsprEncoder
{
    /// <summary>Encodes a message, or refuses it with the reason. Never adjusts the message.</summary>
    public static WsprEncodeResult Encode(WsprMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!WsprPacker.TryPack(message, out var packed, out var refusal)) return new WsprEncodeResult([], message.Text, refusal);
        var coded = WsprConvolutional.Encode(packed.ToBytes());
        WsprInterleaver.Interleave(coded);
        var symbols = new byte[WsprTiming.SymbolCount];
        for (var i = 0; i < symbols.Length; i++) symbols[i] = (byte)(2 * coded[i] + WsprSync.Vector[i]);
        return new WsprEncodeResult(symbols, message.Text, null);
    }

    /// <summary>Encodes a message given as text (<c>K1ABC FN42 37</c>); a text of no known shape is refused.</summary>
    public static WsprEncodeResult Encode(string text) =>
        WsprMessage.TryParse(text, out var message)
            ? Encode(message)
            : new WsprEncodeResult([], text, $"\"{text}\" is not a WSPR message (CALL GRID DBM, CALL/SUFFIX DBM, PREFIX/CALL DBM or <CALL> GRID6 DBM).");
}
