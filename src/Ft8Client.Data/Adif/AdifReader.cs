// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;

namespace Ft8Client.Data.Adif;

/// <summary>
/// Reads ADIF 3.1 text (<c>&lt;name:len[:type]&gt;value</c>, <c>&lt;eor&gt;</c>, header ending <c>&lt;eoh&gt;</c>).
/// Tolerates unknown fields, junk between fields and records, and a missing header. Field lengths are taken as UTF-8
/// bytes, as most loggers and services write them; a file that counts characters instead is read correctly too (see
/// <c>ValueEnd</c>).
/// </summary>
public static class AdifReader
{
    /// <summary>Reads all records.</summary>
    public static IReadOnlyList<AdifRecord> Read(string text)
    {
        var records = new List<AdifRecord>();
        var pos = 0;
        var eoh = text.IndexOf("<eoh>", StringComparison.OrdinalIgnoreCase);
        if (eoh >= 0 && !text.TrimStart().StartsWith('<')) pos = eoh + 5;
        else if (eoh >= 0 && text.IndexOf('<') == eoh) pos = eoh + 5;

        var current = new AdifRecord();
        while (pos < text.Length)
        {
            var lt = text.IndexOf('<', pos);
            if (lt < 0) break;
            var gt = text.IndexOf('>', lt + 1);
            if (gt < 0) break;
            var tag = text.AsSpan(lt + 1, gt - lt - 1);
            pos = gt + 1;

            if (tag.Equals("eor", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Fields.Count > 0) records.Add(current);
                current = new AdifRecord();
                continue;
            }
            if (tag.Equals("eoh", StringComparison.OrdinalIgnoreCase))
            {
                current = new AdifRecord();
                continue;
            }

            var colon = tag.IndexOf(':');
            if (colon <= 0) continue;
            var name = tag[..colon].ToString().Trim();
            var lenPart = tag[(colon + 1)..];
            var colon2 = lenPart.IndexOf(':');
            if (colon2 >= 0) lenPart = lenPart[..colon2];
            if (!int.TryParse(lenPart, NumberStyles.None, CultureInfo.InvariantCulture, out var len) || len < 0) continue;
            var end = ValueEnd(text, pos, len);
            current[name] = text[pos..end];
            pos = end;
        }
        if (current.Fields.Count > 0) records.Add(current);
        return records;
    }

    // Where a value of length len starting at start ends. Plain ASCII reads the same either way. Otherwise len is taken
    // as UTF-8 bytes, unless that cannot be right and a character count can: the byte count ends inside a character
    // (a file that counts characters, or a non-UTF-8 file decoded with replacement characters), or only the character
    // count is followed by the next tag (with nothing but white space between).
    private static int ValueEnd(string text, int start, int len)
    {
        var charEnd = start + Math.Min(len, text.Length - start);
        var ascii = true;
        for (var i = start; i < charEnd && ascii; i++) ascii = text[i] < 0x80;
        if (ascii) return charEnd;

        var byteEnd = ByteEnd(text, start, len);
        if (byteEnd < 0) return charEnd;
        if (!NextTagFollows(text, byteEnd) && NextTagFollows(text, charEnd)) return charEnd;
        return byteEnd;
    }

    // The index after len UTF-8 bytes of text from start, the end of text if it is shorter, or -1 when the count ends
    // inside a character.
    private static int ByteEnd(string text, int start, int len)
    {
        var bytes = 0;
        var i = start;
        while (bytes < len && i < text.Length)
        {
            var c = text[i];
            if (c < 0x80) bytes += 1;
            else if (c < 0x800) bytes += 2;
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                bytes += 4;
                i++;
            }
            else bytes += 3;
            i++;
        }
        return bytes <= len ? i : -1;
    }

    // True when only white space lies between at and the next '<' (or the end of text).
    private static bool NextTagFollows(string text, int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
        return at == text.Length || text[at] == '<';
    }
}
