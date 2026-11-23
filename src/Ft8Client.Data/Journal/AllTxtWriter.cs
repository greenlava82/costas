// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using System.Text;

namespace Ft8Client.Data.Journal;

/// <summary>
/// An optional copy of the decode journal as WSJT-X style <c>ALL.TXT</c> files in a folder the operator picks, one folder
/// per month (<c>2026-11\ALL.TXT</c>), for tools that read WSJT-X's file (owner request 2026-11-22, from user feedback).
/// Lines are in <see cref="DecodeJournal.Format"/>'s layout, which is WSJT-X's.
/// </summary>
public sealed class AllTxtWriter(string folder)
{
    /// <summary>The file name WSJT-X uses.</summary>
    public const string FileName = "ALL.TXT";

    private readonly Lock _gate = new();

    /// <summary>The folder the month folders go in.</summary>
    public string Folder => folder;

    /// <summary>The file a time belongs in: <c>folder\2026-11\ALL.TXT</c>.</summary>
    public string PathFor(DateTime utc) =>
        Path.Combine(folder, utc.ToString("yyyy-MM", CultureInfo.InvariantCulture), FileName);

    /// <summary>Appends lines to the month file of <paramref name="utc"/>, with this system's line ending. I/O errors propagate.</summary>
    public void Append(DateTime utc, IEnumerable<string> lines)
    {
        var text = new StringBuilder();
        foreach (var l in lines) text.Append(l).Append(Environment.NewLine);
        if (text.Length == 0) return;
        var path = PathFor(utc);
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
        }
    }
}
