// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia.Controls;

namespace Ft8Client.App.Views;

/// <summary>One searchable setting: what the result list shows, the words it matches, and where it is.</summary>
internal sealed record SettingsSearchEntry(string Title, string PageTitle, IReadOnlyList<string> Texts, TabItem Page, Control? Target)
{
    /// <summary>True when every word of the query appears in the setting's label, page name or text.</summary>
    public bool Matches(string query) =>
        Words(query).All(w => Title.Contains(w, StringComparison.OrdinalIgnoreCase) || PageTitle.Contains(w, StringComparison.OrdinalIgnoreCase)
                              || Texts.Any(t => t.Contains(w, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// What the result list shows: the label when it matches, otherwise the check box or short line that does
    /// ("Upload each contact as soon as it is logged" rather than "Sync").
    /// </summary>
    public string Display(string query)
    {
        var words = Words(query);
        if (words.All(w => Title.Contains(w, StringComparison.OrdinalIgnoreCase))) return Title;
        return Texts.FirstOrDefault(t => t.Length <= 70 && words.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase))) ?? Title;
    }

    private static string[] Words(string query) => query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
