// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Neighbours;

/// <summary>What a band advice line says and the evidence behind it (docs/04-domain-logic.md, section 14a).</summary>
public enum BandAdviceKind
{
    /// <summary>The band I am on is open: PSK Reporter or RBN stations on a far continent heard my own signal.</summary>
    OpenForYou,

    /// <summary>A band is open from my area: stations on a far continent heard stations near me.</summary>
    OpenFromArea,

    /// <summary>The band I am on is closing: far stations heard stations near me 15 to 30 minutes ago, and far fewer since.</summary>
    Closing,
}
