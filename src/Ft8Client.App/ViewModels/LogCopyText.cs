// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using Ft8Client.App.Services;

namespace Ft8Client.App.ViewModels;

/// <summary>The words Settings › Logbooks › Backups uses for the ADIF log copy and the daily copies.</summary>
public static class LogCopyText
{
    /// <summary>The copy's file names, or a note when there is none yet.</summary>
    public static string Files(LogCopyStatus s) =>
        s.Files.Count == 0 ? "No file yet: it is written once the log has a contact." : string.Join("\n", s.Files.Select(Path.GetFileName));

    /// <summary>When the copy was last written, or why the last write failed and when it is tried again. Times are local.</summary>
    public static string Written(LogCopyStatus s)
    {
        if (s.Error is not null)
        {
            var again = s.NextTryUtc is { } t ? $" Trying again at {Local(t)}." : " Trying again later.";
            return $"Could not write the copy: {s.Error.TrimEnd('.')}.{again}"
                   + (s.LastWrittenUtc is { } w ? $" Last written {Local(w)}." : string.Empty);
        }
        return s.LastWrittenUtc is { } at ? $"Last written {Local(at)}." : "Not written yet; it is written a few seconds after Costas starts.";
    }

    /// <summary>The newest daily database copy, or a note when there is none.</summary>
    public static string Daily((string Path, DateTime WrittenUtc)? newest, bool simulating) =>
        simulating ? "Daily copies of the database are not taken in simulation."
        : newest is { } n ? $"Newest daily copy: {System.IO.Path.GetFileName(n.Path)}, {Local(n.WrittenUtc)}."
        : "No daily copy yet: the first is taken shortly after Costas starts.";

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture);
}
