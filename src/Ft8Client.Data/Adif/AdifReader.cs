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
/// Tolerates unknown fields, junk between fields and records, and a missing header. A length may count characters or
/// UTF-8 bytes: loggers differ for non-ASCII text, which ADIF 3.1 does not allow in ADI files (see
/// <see cref="AdifLengthUnit"/>), so both are accepted.
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
            len = Value(text, pos, len);
            current[name] = text.Substring(pos, len);
            pos += len;
        }
        if (current.Fields.Count > 0) records.Add(current);
        return records;
    }

    // How many characters of text from pos make up a value of the given length. For ASCII the two counts agree. Otherwise
    // the length is taken as UTF-8 bytes when that ends the value where the next field or tag starts (only white space
    // between), and as characters when it does not.
    private static int Value(string text, int pos, int len)
    {
        var chars = Math.Min(len, text.Length - pos);
        var bytes = 0;
        var n = 0;
        while (n < chars && bytes < len)
        {
            int size;
            if (char.IsHighSurrogate(text[pos + n]) && pos + n + 1 < text.Length && char.IsLowSurrogate(text[pos + n + 1])) size = 2;
            else size = 1;
            bytes += System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(pos + n, size));
            n += size;
        }
        if (n == chars) return chars;
        if (bytes == len && EndsField(text, pos + n)) return n;
        return chars;
    }

    private static bool EndsField(string text, int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
        return at == text.Length || text[at] == '<';
    }
}
