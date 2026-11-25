// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.App.Views;

/// <summary>A callsign tag's text to be placed by <see cref="WaterfallView.LayoutCallTags"/>.</summary>
/// <param name="CenterX">The signal's x position.</param>
/// <param name="Top">The top of the row the tag belongs to.</param>
/// <param name="Width">Text width.</param>
/// <param name="Height">Text height.</param>
public readonly record struct CallTagBox(double CenterX, double Top, double Width, double Height);
