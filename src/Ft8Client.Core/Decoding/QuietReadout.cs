// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Decoding;

/// <summary>The Operate screen's "why is it quiet" line.</summary>
/// <param name="Cause">The most likely reason.</param>
/// <param name="Text">The line shown to the operator.</param>
/// <param name="GoToDialHz">
/// The usual dial frequency to offer as a "Go to 14.074" button, or null when the fix is not a frequency change. Nothing
/// tunes the radio until the operator clicks it.
/// </param>
public sealed record QuietReadout(QuietCause Cause, string Text, long? GoToDialHz = null)
{
    /// <summary>The button's label, "Go to 14.074", or null when there is no button.</summary>
    public string? GoToText => GoToDialHz is { } hz ? $"Go to {Bands.DialText.Mhz(hz)}" : null;
}
