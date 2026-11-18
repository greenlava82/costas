// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Data.Files;
using Ft8Client.Data.Log;

namespace Ft8Client.Data.Adif;

/// <summary>
/// A plain ADIF copy of the whole log in the backups folder, readable by any logger and by people: one file per
/// station callsign (<c>costas-log-W7LIT.adi</c>, see <see cref="AdifLogExport"/>). Each file is swapped in whole
/// (<see cref="AtomicFile"/>), so a crash never leaves half a file. A file for a callsign no longer in the log is
/// removed; the dated database copies (<see cref="Database.LogBackup"/>) still hold those contacts.
/// When to write is decided by the caller (the app debounces changes and writes off the UI and slot threads).
/// </summary>
public sealed class AdifLogMirror(Func<IReadOnlyList<QsoRecord>> contacts, string folder)
{
    /// <summary>The start of every file name.</summary>
    public const string Stem = "costas-log";

    /// <summary>The backups folder.</summary>
    public string Folder { get; } = folder;

    /// <summary>
    /// Writes every file and removes stale ones. Returns the paths written, in callsign order. Throws
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when a file cannot be written; files already
    /// swapped in stay, the others keep their previous content.
    /// </summary>
    public IReadOnlyList<string> Write(DateTime nowUtc)
    {
        var groups = AdifLogExport.ByStation(contacts());
        Directory.CreateDirectory(Folder);
        var written = new List<string>();
        foreach (var g in groups)
        {
            var path = Path.Combine(Folder, AdifLogExport.FileName(Stem, g.StationCallsign));
            AtomicFile.WriteAllText(path, AdifLogExport.Document(g, nowUtc, "log copy"));
            written.Add(path);
        }
        foreach (var old in Existing())
        {
            if (!written.Contains(old, StringComparer.OrdinalIgnoreCase)) File.Delete(old);
        }
        return written;
    }

    /// <summary>The copy's files now in the folder, in name order.</summary>
    public IReadOnlyList<string> Existing() =>
        Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, Stem + "-*.adi").Order(StringComparer.Ordinal).ToList()
            : [];
}
