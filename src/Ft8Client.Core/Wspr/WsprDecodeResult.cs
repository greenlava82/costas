// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Decoding;

namespace Ft8Client.Core.Wspr;

/// <summary>Outcome of decoding one WSPR slot.</summary>
/// <param name="Spots">The signals heard, in decoder order.</param>
/// <param name="SkippedLines">Output lines that were not decodes (garbage), counted and not thrown on.</param>
/// <param name="Elapsed">Wall time spent decoding.</param>
/// <param name="Status">Whether the run completed, timed out or failed.</param>
/// <param name="Message">Detail for a failure; empty on success.</param>
public sealed record WsprDecodeResult(IReadOnlyList<WsprSpot> Spots, int SkippedLines, TimeSpan Elapsed, DecodeStatus Status, string Message = "");
