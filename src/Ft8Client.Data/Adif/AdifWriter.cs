// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using System.Text;
using Ft8Client.Core;

namespace Ft8Client.Data.Adif;

/// <summary>Writes ADIF 3.1 text.</summary>
public static class AdifWriter
{
    /// <summary>The header block.</summary>
    public static string Header(DateTime nowUtc) => Header(nowUtc, $"{AppInfo.ProductName} ADIF export");

    /// <summary>
    /// The header block with <paramref name="text"/> as its opening lines. The text must not contain <c>&lt;</c>, which
    /// would start a field.
    /// </summary>
    public static string Header(DateTime nowUtc, string text)
    {
        if (text.Contains('<', StringComparison.Ordinal)) throw new ArgumentException("Header text must not contain '<'.", nameof(text));
        var sb = new StringBuilder();
        sb.Append(text.TrimEnd()).Append('\n');
        Field(sb, "adif_ver", "3.1.4");
        Field(sb, "created_timestamp", nowUtc.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture));
        Field(sb, "programid", AppInfo.Id);
        Field(sb, "programversion", AppInfo.Version);
        sb.Append("<eoh>\n\n");
        return sb.ToString();
    }

    /// <summary>One record ending in <c>&lt;eor&gt;</c>, lengths counted in UTF-16 code units.</summary>
    public static string Record(AdifRecord r) => Record(r, AdifLengthUnit.Utf16);

    /// <summary>One record ending in <c>&lt;eor&gt;</c>, lengths counted in <paramref name="unit"/>.</summary>
    public static string Record(AdifRecord r, AdifLengthUnit unit)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in r.Fields)
        {
            if (v.Length > 0) Field(sb, k, v, unit);
        }
        sb.Append("<eor>\n");
        return sb.ToString();
    }

    /// <summary>A header and the records.</summary>
    public static string Document(IEnumerable<AdifRecord> records, DateTime nowUtc)
    {
        var sb = new StringBuilder(Header(nowUtc));
        foreach (var r in records) sb.Append(Record(r));
        return sb.ToString();
    }

    /// <summary>
    /// A file: a header opening with <paramref name="headerText"/>, then the records separated by blank lines, lengths in
    /// UTF-8 bytes (see <see cref="AdifLengthUnit"/>).
    /// </summary>
    public static string FileText(IEnumerable<AdifRecord> records, DateTime nowUtc, string headerText)
    {
        var sb = new StringBuilder(Header(nowUtc, headerText));
        var first = true;
        foreach (var r in records)
        {
            if (!first) sb.Append('\n');
            first = false;
            sb.Append(Record(r, AdifLengthUnit.Utf8Bytes));
        }
        return sb.ToString();
    }

    /// <summary>The length of <paramref name="value"/> in <paramref name="unit"/>.</summary>
    public static int Length(string value, AdifLengthUnit unit) =>
        unit == AdifLengthUnit.Utf8Bytes ? Encoding.UTF8.GetByteCount(value) : value.Length;

    private static void Field(StringBuilder sb, string name, string value, AdifLengthUnit unit = AdifLengthUnit.Utf16) =>
        sb.Append(CultureInfo.InvariantCulture, $"<{name}:{Length(value, unit)}>{value}\n");
}
