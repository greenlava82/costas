// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;

namespace Ft8Client.Rig.Flex;

/// <summary>A reply to a command, <c>R&lt;seq&gt;|&lt;hex code&gt;|&lt;message&gt;</c> (SmartSDR API wiki, "Response Format").</summary>
/// <param name="Sequence">The command's sequence number.</param>
/// <param name="Code">The result code; 0 is success.</param>
/// <param name="Message">The message after the code, often empty.</param>
public sealed record FlexReply(int Sequence, uint Code, string Message)
{
    /// <summary>The code for "Invalid amplifier - could not find by handle".</summary>
    public const uint UnknownAmplifier = 0x500000A7;

    /// <summary>True when the radio took the command.</summary>
    public bool Ok => Code == 0;

    /// <summary>Reads a reply line, or returns null for any other line.</summary>
    public static FlexReply? Parse(string line)
    {
        if (line.Length < 2 || line[0] != 'R') return null;
        var parts = line[1..].Split('|');
        if (parts.Length < 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var seq)
            || !uint.TryParse(parts[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
            return null;
        return new FlexReply(seq, code, parts.Length > 2 ? parts[2] : string.Empty);
    }
}
