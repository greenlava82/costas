// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Adif;

/// <summary>
/// What the length in <c>&lt;name:len&gt;</c> counts. The two agree for ASCII text, which is all ADIF 3.1 allows in an
/// ADI file; they differ only for a name or comment with accented or other non-ASCII letters (docs/08-open-questions.md,
/// "ADIF lengths").
/// </summary>
public enum AdifLengthUnit
{
    /// <summary>.NET string length (UTF-16 code units). Used for records handed to online services, as before.</summary>
    Utf16,

    /// <summary>Bytes of the UTF-8 encoding. Used for ADIF files Costas writes (export and the log copy).</summary>
    Utf8Bytes,
}
