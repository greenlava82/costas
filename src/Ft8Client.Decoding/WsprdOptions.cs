// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Decoding;

/// <summary>How to run <c>wsprd</c>.</summary>
/// <param name="ExecutablePath">Full path of <c>wsprd</c>.</param>
/// <param name="TempRoot">Folder under which a scratch folder is created per run.</param>
/// <param name="Timeout">Kill the run after this long. Defaults to 60 s.</param>
public sealed record WsprdOptions(string ExecutablePath, string TempRoot, TimeSpan? Timeout = null)
{
    /// <summary>The default limit for one run (a decode takes about 3 s; docs/08-open-questions.md V-WSPR2).</summary>
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(60);
}
