// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>The 50 payload bits of a WSPR message: a 28-bit callsign part and a 22-bit part.</summary>
/// <param name="N">The callsign part (28 bits).</param>
/// <param name="M">The grid, power and message-type part (22 bits).</param>
public readonly record struct WsprPacked(uint N, uint M)
{
    /// <summary>The 50 bits and 31 zero tail bits as 11 bytes, as the convolutional encoder reads them.</summary>
    public byte[] ToBytes() =>
    [
        (byte)(N >> 20), (byte)(N >> 12), (byte)(N >> 4),
        (byte)(((N & 0x0F) << 4) | ((M >> 18) & 0x0F)), (byte)(M >> 10), (byte)(M >> 2), (byte)((M & 0x03) << 6),
        0, 0, 0, 0,
    ];
}

/// <summary>
/// Packs a WSPR message into its 50 bits: a port of <c>get_wspr_channel_symbols</c>, <c>pack_call</c>,
/// <c>pack_grid4_power</c> and <c>pack_prefix</c> in WSJT-X 3.0.1 <c>lib/wsprd/wsprsim_utils.c</c> (GPLv3). That code
/// makes no checks; this one refuses anything that would not be received exactly as shown (CLAUDE.md rule 4): it never
/// pads, trims, upper-cases or otherwise adjusts what it is given.
/// </summary>
public static class WsprPacker
{
    /// <summary>Packs a message, or says why it cannot be sent as shown.</summary>
    public static bool TryPack(WsprMessage message, out WsprPacked packed, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(message);
        packed = default;
        if (!WsprPower.IsLegal(message.Dbm))
        {
            refusal = $"{message.Dbm} dBm cannot be sent: WSPR power is 0 to 60 dBm ending in 0, 3 or 7.";
            return false;
        }
        return message.Type switch
        {
            WsprMessageType.Type1 => PackType1(message, out packed, out refusal),
            WsprMessageType.Type2 => PackType2(message, out packed, out refusal),
            WsprMessageType.Type3 => PackType3(message, out packed, out refusal),
            _ => Refuse("Unknown WSPR message type.", out refusal),
        };
    }

    private static bool PackType1(WsprMessage m, out WsprPacked packed, out string? refusal)
    {
        packed = default;
        if (m.Grid is null || !IsGrid4(m.Grid)) return Refuse($"\"{m.Grid}\" is not a 4-character locator (two letters A to R, two digits).", out refusal);
        if (!TryPackCall(m.Callsign, out var n, out refusal)) return false;
        var g = m.Grid;
        var grid = (uint)((179 - 10 * (g[0] - 'A') - (g[2] - '0')) * 180 + 10 * (g[1] - 'A') + (g[3] - '0'));
        packed = new WsprPacked(n, grid * 128 + (uint)m.Dbm + 64);
        refusal = null;
        return true;
    }

    private static bool PackType3(WsprMessage m, out WsprPacked packed, out string? refusal)
    {
        packed = default;
        if (m.Grid is null || !IsGrid6(m.Grid))
            return Refuse($"\"{m.Grid}\" is not a 6-character locator (two letters A to R, two digits, two letters A to X).", out refusal);
        if (m.Callsign.Length is < 3 or > 12 || !m.Callsign.All(c => IsUpperOrDigit(c) || c == '/') || !m.Callsign.Any(char.IsAsciiDigit))
            return Refuse($"{m.Callsign} cannot be hashed: a callsign is 3 to 12 letters, digits or slashes with a digit in it.", out refusal);
        // The callsign part carries the grid with its first letter moved to the end, packed as if it were a callsign.
        var rotated = m.Grid[1..] + m.Grid[0];
        if (!TryPackCall(rotated, out var n, out _)) return Refuse($"The locator {m.Grid} cannot be packed.", out refusal);
        var ihash = (uint)WsprHash.Of(m.Callsign);
        // ntype = -(power + 1); m = 128 * hash + ntype + 64.
        packed = new WsprPacked(n, (uint)(128 * ihash - (uint)(m.Dbm + 1) + 64));
        refusal = null;
        return true;
    }

    private static bool PackType2(WsprMessage m, out WsprPacked packed, out string? refusal)
    {
        packed = default;
        var call = m.Callsign;
        var slash = call.IndexOf('/');
        if (slash < 0 || slash != call.LastIndexOf('/') || slash == 0 || slash == call.Length - 1)
            return Refuse($"{call} needs exactly one slash with something on each side to be a prefix or suffix call.", out refusal);
        if (!call.All(c => IsUpperOrDigit(c) || c == '/')) return Refuse($"{call} may hold only upper-case letters, digits and one slash.", out refusal);
        var before = call[..slash];
        var after = call[(slash + 1)..];

        uint n;
        int nprefix;
        if (after.Length == 1)
        {
            // CALL/X: one letter or digit after the slash.
            if (!TryPackCall(before, out n, out refusal)) return false;
            nprefix = 60000 + Code(after[0]);
        }
        else if (after.Length == 2)
        {
            // CALL/NN: two digits, 10 to 99 (00 to 09 would read back as a letter).
            if (!char.IsAsciiDigit(after[0]) || !char.IsAsciiDigit(after[1]) || after[0] == '0')
                return Refuse($"The suffix /{after} is not one letter or digit, or a number from 10 to 99.", out refusal);
            if (!TryPackCall(before, out n, out refusal)) return false;
            nprefix = 60026 + 10 * (after[0] - '0') + (after[1] - '0');
        }
        else
        {
            // PFX/CALL: a prefix of one to three letters or digits.
            if (before.Length > 3) return Refuse($"The prefix {before} is more than 3 characters.", out refusal);
            if (!TryPackCall(after, out n, out refusal)) return false;
            // Left-padded with spaces (36) to three characters, base 37.
            nprefix = 0;
            for (var i = 0; i < 3; i++) nprefix = 37 * nprefix + (i < 3 - before.Length ? 36 : Code(before[i - (3 - before.Length)]));
        }

        // A prefix number from 32768 up does not fit the 15 bits: it is sent as the number less 32768 with nadd = 1.
        var nadd = 0;
        if (nprefix >= 32768)
        {
            nprefix -= 32768;
            nadd = 1;
        }
        var ntype = m.Dbm + 1 + nadd;
        packed = new WsprPacked(n, (uint)(128 * nprefix + ntype + 64));
        refusal = null;
        return true;
    }

    /// <summary>
    /// Packs a 3 to 6 character call into 28 bits as <c>pack_call</c> does: the digit must be the third character, or the
    /// second with a space put in front; the characters after the digit are letters or spaces.
    /// </summary>
    internal static bool TryPackCall(string call, out uint n, out string? refusal)
    {
        n = 0;
        refusal = null;
        if (call.Length is < 3 or > 6 || !call.All(IsUpperOrDigit))
            return Refuse($"{call} must be 3 to 6 upper-case letters and digits.", out refusal);
        Span<char> c6 = stackalloc char[6];
        c6.Fill(' ');
        if (char.IsAsciiDigit(call[2])) call.CopyTo(c6);
        else if (char.IsAsciiDigit(call[1]) && call.Length <= 5) call.CopyTo(c6[1..]);
        else return Refuse($"{call} has no digit in the third place (or second, for a call of 5 characters or fewer), which a WSPR callsign needs.", out refusal);
        // Positions: 0 letter, digit or space; 1 letter or digit; 2 digit; 3 to 5 letter or space.
        if (c6[1] == ' ' || !char.IsAsciiDigit(c6[2]) || (c6[0] != ' ' && !IsUpperOrDigit(c6[0])))
            return Refuse($"{call} does not fit the WSPR callsign layout.", out refusal);
        for (var i = 3; i < 6; i++)
        {
            if (c6[i] != ' ' && !char.IsAsciiLetterUpper(c6[i]))
                return Refuse($"{call} has a digit after the one WSPR expects; only letters may follow it.", out refusal);
        }
        uint v = (uint)Code(c6[0]);
        v = v * 36 + (uint)Code(c6[1]);
        v = v * 10 + (uint)Code(c6[2]);
        for (var i = 3; i < 6; i++) v = v * 27 + (uint)(Code(c6[i]) - 10);
        n = v;
        return true;
    }

    // 0 to 9, A to Z as 10 to 35, a space as 36.
    private static int Code(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'Z' => c - 'A' + 10,
        _ => 36,
    };

    private static bool IsUpperOrDigit(char c) => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c);

    private static bool IsGrid4(string g) =>
        g.Length == 4 && g[0] is >= 'A' and <= 'R' && g[1] is >= 'A' and <= 'R' && char.IsAsciiDigit(g[2]) && char.IsAsciiDigit(g[3]);

    private static bool IsGrid6(string g) =>
        g.Length == 6 && IsGrid4(g[..4]) && g[4] is >= 'A' and <= 'X' && g[5] is >= 'A' and <= 'X';

    private static bool Refuse(string why, out string? refusal)
    {
        refusal = why;
        return false;
    }
}
