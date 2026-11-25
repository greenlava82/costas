// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.App.Views;

/// <summary>A callsign drawn on the waterfall over the signal it was decoded from.</summary>
/// <param name="Hz">Audio frequency of the middle of the signal, in hertz.</param>
/// <param name="Label">The callsign.</param>
/// <param name="Kind">Its colour.</param>
public readonly record struct WaterfallTag(int Hz, string Label, WaterfallTagKind Kind);
