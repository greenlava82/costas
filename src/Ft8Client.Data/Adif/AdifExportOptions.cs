// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Text;

namespace Ft8Client.Data.Adif;

/// <summary>
/// How a Log view export is written for the program that will read it.
/// </summary>
/// <param name="PlainAscii">
/// Write every value in plain ASCII, as the ADIF specification asks of <c>.adi</c> files: German umlauts and ß become
/// <c>ae</c>, <c>oe</c>, <c>ue</c>, <c>ss</c>, other accented letters lose their accent, anything else becomes <c>?</c>, and line
/// breaks become spaces. Loggers that read the file as Windows text (DXKeeper and others) otherwise show <c>MÃ¼ller</c>
/// for <c>Müller</c>.
/// </param>
/// <param name="LeaveOutNameQthComment">
/// Leave out <c>NAME</c>, <c>QTH</c> and <c>COMMENT</c>, so the receiving logger fills name and QTH from its own callbook
/// lookup and keeps its own comment.
/// </param>
public sealed record AdifExportOptions(bool PlainAscii = false, bool LeaveOutNameQthComment = false)
{
    /// <summary>The fields <see cref="LeaveOutNameQthComment"/> removes.</summary>
    public static readonly IReadOnlyList<string> PersonalFields = ["name", "qth", "comment"];

    /// <summary>UTF-8 text, every field kept: what Costas, QRZ, LoTW and Club Log read.</summary>
    public static AdifExportOptions Default { get; } = new();

    /// <summary>The record as this export writes it. The record passed in is not changed.</summary>
    public AdifRecord Apply(AdifRecord r)
    {
        if (!PlainAscii && !LeaveOutNameQthComment) return r;
        var copy = new AdifRecord();
        foreach (var (k, v) in r.Fields)
        {
            if (LeaveOutNameQthComment && PersonalFields.Contains(k, StringComparer.OrdinalIgnoreCase)) continue;
            copy[k] = PlainAscii ? ToAscii(v) : v;
        }
        return copy;
    }

    /// <summary>
    /// <paramref name="text"/> in printable ASCII (codes 32 to 126): <c>Müller</c> becomes <c>Mueller</c>, <c>Café</c>
    /// <c>Cafe</c>, <c>Łódź</c> <c>Lodz</c>; a line break or tab becomes a space; a character with no plain form becomes
    /// <c>?</c>.
    /// </summary>
    public static string ToAscii(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is >= ' ' and <= '~') { sb.Append(ch); continue; }
            if (char.IsControl(ch) || char.IsWhiteSpace(ch)) { sb.Append(' '); continue; }
            var spelled = ch switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'Ä' => "Ae", 'Ö' => "Oe", 'Ü' => "Ue", 'ß' => "ss",
                'æ' => "ae", 'Æ' => "AE", 'ø' => "o", 'Ø' => "O", 'å' => "a", 'Å' => "A", 'œ' => "oe", 'Œ' => "OE",
                'ł' => "l", 'Ł' => "L", 'đ' => "d", 'Đ' => "D", 'ð' => "d", 'Ð' => "D", 'þ' => "th", 'Þ' => "Th",
                'ı' => "i", '‘' or '’' or '‚' or '′' => "'", '“' or '”' or '„' or '″' => "\"", '–' or '—' => "-", '…' => "...",
                _ => null,
            };
            if (spelled is not null) { sb.Append(spelled); continue; }
            sb.Append(BaseLetter(ch));
        }
        return sb.ToString();
    }

    // é → e. The app runs with invariant globalization, where string normalisation does not decompose accents, so the
    // Latin-1 and Latin Extended-A letters (Western, Central and Northern European names) are listed here.
    private const string Latin1 = "AAAAAAACEEEEIIIIDNOOOOOxOUUUUYTsaaaaaaaceeeeiiiidnooooo/ouuuuyty"; // U+00C0 to U+00FF

    private const string LatinExtendedA = // U+0100 to U+017F
        "AaAaAaCcCcCcCcDdDdEeEeEeEeEeGgGgGgGgHhHhIiIiIiIiIiIiJjKkkLlLlLlLlLlNnNnNnnNnOoOoOoOoRrRrRrSsSsSsSsTtTtTtUuUuUuUuUuUuWwYyYZzZzZzs";

    private static char BaseLetter(char ch) => ch switch
    {
        >= '\u00C0' and <= '\u00FF' => Latin1[ch - 0xC0],
        >= '\u0100' and <= '\u017F' => LatinExtendedA[ch - 0x100],
        _ => '?',
    };
}
