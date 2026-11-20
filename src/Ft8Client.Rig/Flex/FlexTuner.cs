// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Rig.Flex;

/// <summary>An antenna tuner paired with the radio, a 4O3A TunerGenius XL, as last reported.</summary>
/// <param name="Handle">The radio's handle for it; commands name it.</param>
/// <param name="Model">The model it registered with, <c>TunerGeniusXL</c>.</param>
/// <param name="Operate">True in operate, false in standby (<c>operate=</c>).</param>
/// <param name="Bypass">True when the matching network is bypassed (<c>bypass=</c>); only means something in operate.</param>
/// <param name="Tuning">True while it is tuning (<c>tuning=</c>).</param>
public sealed record FlexTuner(string Handle, string Model, bool Operate, bool Bypass, bool Tuning)
{
    /// <summary>The model for display, <c>TunerGenius XL</c>.</summary>
    public string Name => FlexModelNames.Display(Model, "Tuner");
}
