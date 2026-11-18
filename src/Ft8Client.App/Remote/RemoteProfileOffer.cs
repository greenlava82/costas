// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.App.Remote;

/// <summary>
/// The station profile a signed-in remote user is linked to, for the phone's "Operate as" button (owner decision D25):
/// switching to it is a deliberate tap, never a side effect of another command.
/// </summary>
/// <param name="Mine">The user's own profile, as "K1GLV (Home profile)".</param>
/// <param name="MineCall">The user's own callsign, for the button: "K1GLV".</param>
/// <param name="InUse">The profile in use now, as "K1UTA (Ash profile)".</param>
/// <param name="OnMine">True when the user's own profile is the one in use.</param>
/// <param name="Busy">Why the station cannot switch now ("while calling CQ as K1UTA"), or null when it is idle.</param>
public sealed record RemoteProfileOffer(string Mine, string MineCall, string InUse, bool OnMine, string? Busy);
