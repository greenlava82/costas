// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.App.Services;

/// <summary>Where the ADIF log copy is and how its last write went (<see cref="LogCopyService"/>).</summary>
/// <param name="Folder">The backups folder.</param>
/// <param name="Files">The copy's files after the last write, full paths.</param>
/// <param name="LastWrittenUtc">When the copy was last written in full, or null if not yet this session.</param>
/// <param name="Error">Why the last write failed, or null when it worked.</param>
/// <param name="NextTryUtc">When a failed write is tried again, or null.</param>
public sealed record LogCopyStatus(string Folder, IReadOnlyList<string> Files, DateTime? LastWrittenUtc, string? Error, DateTime? NextTryUtc);
