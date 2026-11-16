// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Stations;

/// <summary>What the online check of a SuperFox signature said.</summary>
public enum FoxCheck
{
    /// <summary>Not checked: no signature heard yet, the check is off, or the service did not answer.</summary>
    None,

    /// <summary>The signature belongs to the DXpedition's registered key: the real station.</summary>
    Verified,

    /// <summary>The signature did not check out: possibly a pirate using the DXpedition's call.</summary>
    Invalid,
}
