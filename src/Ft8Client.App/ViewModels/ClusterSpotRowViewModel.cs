// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
namespace Ft8Client.App.ViewModels;

/// <summary>One needed station spotted on the DX cluster, on the Awards view. Display only.</summary>
/// <param name="Call">"JA1XYZ".</param>
/// <param name="Entity">"Japan".</param>
/// <param name="Need">"New DXCC" or "New DXCC 15 m".</param>
/// <param name="NewCountry">True for a country not in the log yet.</param>
/// <param name="Spotted">"21074.0 · Spotted 4 min ago on 15 m by DL1ABC · FT8 -12 dB".</param>
/// <param name="Band">The spot's band, ADIF name (<c>15m</c>).</param>
/// <param name="GoText">"Go to 15 m", or empty when already there.</param>
public sealed record ClusterSpotRowViewModel(string Call, string Entity, string Need, bool NewCountry, string Spotted, string Band, string GoText)
{
    /// <summary>The Go to button is shown: the spot is on another band.</summary>
    public bool CanGo => GoText.Length > 0;

    /// <summary>Need colour: caution for a new country, accent for a new band, as on station rows.</summary>
    public TextKind NeedKind => NewCountry ? TextKind.Caution : TextKind.Accent;
}
