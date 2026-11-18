// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Settings;

/// <summary>
/// A station profile's LoTW account: the login that downloads its confirmations and the TQSL Station Location that signs
/// its uploads (a location belongs to one callsign's certificate). The password is in the credential store, per profile.
/// </summary>
public sealed class LotwAccountOptions
{
    /// <summary>Download this profile's LoTW confirmations.</summary>
    public bool Enabled { get; set; }

    /// <summary>LoTW login name (usually the callsign).</summary>
    public string? Username { get; set; }

    /// <summary>The TQSL Station Location to sign with; null when TQSL has only one.</summary>
    public string? StationLocation { get; set; }

    /// <summary>Sign and upload each contact through TQSL when it is logged.</summary>
    public bool UploadOnComplete { get; set; }
}
