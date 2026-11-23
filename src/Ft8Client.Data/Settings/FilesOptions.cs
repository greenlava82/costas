// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Settings;

/// <summary>Slot audio saving and the ALL.TXT copy of the decode journal.</summary>
public sealed class FilesOptions
{
    /// <summary><c>none</c>, <c>decoded</c> or <c>all</c>.</summary>
    public string SaveWav { get; set; } = "none";

    /// <summary>Days to keep saved WAV files.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Also write every decode and transmission to WSJT-X style ALL.TXT files, one folder per month.</summary>
    public bool AllTxt { get; set; }

    /// <summary>Where the ALL.TXT month folders go; blank for Documents\Costas.</summary>
    public string AllTxtFolder { get; set; } = string.Empty;
}
