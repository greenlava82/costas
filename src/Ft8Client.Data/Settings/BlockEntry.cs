// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Settings;

/// <summary>One block list entry as stored.</summary>
public sealed class BlockEntry
{
    /// <summary>The call, upper case, without portable suffixes.</summary>
    public string Call { get; set; } = string.Empty;

    /// <summary>ADIF band ("40m"), or null for every band.</summary>
    public string? Band { get; set; }

    /// <summary>Mode name ("FT8"), or null for every mode.</summary>
    public string? Mode { get; set; }
}
