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
using Ft8Client.Data.Log;

namespace Ft8Client.Data.Adif;

/// <summary>
/// The ADIF files Costas writes from the log: one per station callsign, so each can go to QRZ, LoTW or Club Log, which
/// keep one logbook per callsign. Profiles that share a callsign share a file (<c>MY_GRIDSQUARE</c> tells their
/// locations apart). Contacts are oldest first and carry their QSL state (<see cref="AdifMapper.ToFileAdif"/>).
/// Used by the Log view's export and by the log copy in the backups folder (<see cref="AdifLogMirror"/>).
/// </summary>
public static class AdifLogExport
{
    /// <summary>One file's worth: a station callsign and its contacts, oldest first.</summary>
    public sealed record Group(string StationCallsign, IReadOnlyList<QsoRecord> Contacts);

    /// <summary>Splits contacts by station callsign (upper case), in callsign order; contacts oldest first.</summary>
    public static IReadOnlyList<Group> ByStation(IEnumerable<QsoRecord> qsos) =>
        qsos.GroupBy(q => Normalise(q.StationCallsign), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Group(g.Key, g.OrderBy(q => q.QsoDateOn).ThenBy(q => q.Id).ToList()))
            .ToList();

    /// <summary>The ADIF text of one group.</summary>
    public static string Document(Group group, DateTime nowUtc, string purpose)
    {
        var header = string.Create(CultureInfo.InvariantCulture,
            $"{AppInfo.ProductName} {purpose} for {Display(group.StationCallsign)}, {group.Contacts.Count} contacts, written {nowUtc:yyyy-MM-dd HH:mm} UTC.");
        return AdifWriter.FileText(group.Contacts.Select(AdifMapper.ToFileAdif), nowUtc, header);
    }

    /// <summary>
    /// A file name for a group: <paramref name="stem"/>, a dash and the callsign, then <paramref name="extension"/>
    /// (<c>costas-log-W7LIT.adi</c>). A slash in a compound call becomes an underscore (<c>VE3_W7LIT</c>); a contact with no
    /// station callsign goes to <c>…-unknown</c>.
    /// </summary>
    public static string FileName(string stem, string stationCallsign, string extension = ".adi")
    {
        var sb = new StringBuilder();
        foreach (var ch in Display(stationCallsign))
            sb.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '_');
        return stem + "-" + sb + extension;
    }

    private static string Normalise(string? call) => (call ?? string.Empty).Trim().ToUpperInvariant();

    private static string Display(string call) => call.Length == 0 ? "unknown" : call;
}
