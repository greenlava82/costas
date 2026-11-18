// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Data.Settings;

/// <summary>
/// Club Log refused the login (HTTP 403) and real-time uploads are stopped, as Club Log requires: a client that keeps
/// sending after a 403 has its address firewalled. Kept in settings so a restart does not send again. Holds the email
/// and callsign that were refused, never the password or anything derived from it; a new password is noticed when the
/// operator saves it in Settings, which clears the stop.
/// </summary>
public sealed class ClubLogStop
{
    /// <summary>The account email that was refused.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The callsign that was refused.</summary>
    public string Callsign { get; set; } = string.Empty;

    /// <summary>When Club Log refused it, UTC.</summary>
    public DateTime StoppedUtc { get; set; }

    /// <summary>What Club Log said.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>True while the stop still covers this email and callsign; a different one is a credentials change.</summary>
    public bool Covers(string email, string callsign) =>
        string.Equals(Email.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(Callsign.Trim(), callsign.Trim(), StringComparison.OrdinalIgnoreCase);
}
