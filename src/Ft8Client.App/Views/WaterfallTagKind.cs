// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.App.Views;

/// <summary>What a callsign tag on the waterfall says about its station, which picks its colour (as in the station list).</summary>
public enum WaterfallTagKind
{
    /// <summary>Nothing special: white.</summary>
    Normal,

    /// <summary>Worked before: grey.</summary>
    Worked,

    /// <summary>A new band or grid: blue.</summary>
    NewBand,

    /// <summary>A new country (DXCC): amber.</summary>
    NewCountry,

    /// <summary>Calling me, or the station I am in a contact with: red.</summary>
    You,
}
