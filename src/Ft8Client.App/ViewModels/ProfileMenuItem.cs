// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using CommunityToolkit.Mvvm.Input;

namespace Ft8Client.App.ViewModels;

/// <summary>A station profile in the desk's callsign menu.</summary>
/// <param name="Id">The profile's id.</param>
/// <param name="Label">"K1UTA · Ash".</param>
/// <param name="InUse">True for the profile in use, which the menu ticks.</param>
/// <param name="Switch">Switches to the profile (with its id as the parameter).</param>
public sealed record ProfileMenuItem(string Id, string Label, bool InUse, IRelayCommand<string> Switch)
{
    /// <summary>The tick beside the profile in use.</summary>
    public string Tick => InUse ? "✓" : string.Empty;
}
