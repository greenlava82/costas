// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Integrations.ClubLog;

/// <summary>What one real-time upload came to.</summary>
public enum ClubLogOutcome
{
    /// <summary>Stored, already there, or stored with changes (HTTP 200).</summary>
    Stored,

    /// <summary>The record was refused as bad (HTTP 400); sending it again cannot help.</summary>
    Rejected,

    /// <summary>Login or key refused (HTTP 403): stop sending until the operator changes the credentials.</summary>
    Stop,

    /// <summary>A network or service fault: try again later.</summary>
    Retry,
}
