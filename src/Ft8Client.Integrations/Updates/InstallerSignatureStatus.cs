// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Integrations.Updates;

/// <summary>What Windows makes of a downloaded installer's Authenticode signature.</summary>
public enum InstallerSignatureStatus
{
    /// <summary>Signed, and the signature checks out to a trusted root.</summary>
    Valid,

    /// <summary>Not signed at all.</summary>
    Unsigned,

    /// <summary>Signed, but the signature is broken, untrusted, expired without a timestamp, or cannot be checked.</summary>
    Invalid,
}
