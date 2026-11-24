// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Settings;

/// <summary>The Log view's Export ADIF choices, kept between exports.</summary>
public sealed class LogExportOptions
{
    /// <summary>Write plain ASCII text (for DXKeeper and other loggers that read the file as Windows text).</summary>
    public bool PlainAscii { get; set; }

    /// <summary>Leave out name, QTH and comment, so the receiving logger fills them itself.</summary>
    public bool LeaveOutNameQthComment { get; set; }
}
