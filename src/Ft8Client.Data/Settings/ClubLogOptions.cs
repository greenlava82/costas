// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Data.Settings;

/// <summary>Optional real-time uploads to Club Log. The Application Password is in the secret store.</summary>
public sealed class ClubLogOptions
{
    /// <summary>Upload each newly logged contact. Off until the operator turns it on.</summary>
    public bool UploadOnComplete { get; set; }

    /// <summary>The email address the Club Log account is registered with (not a callsign).</summary>
    public string Email { get; set; } = string.Empty;
}
