// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Rig.Flex;

/// <summary>Model names as FlexRadio accessories register them, and how they are shown.</summary>
public static class FlexModelNames
{
    /// <summary>The model a 4O3A TunerGenius XL registers with; every other model is an amplifier (FlexLib <c>Radio.cs</c>).</summary>
    public const string TunerGeniusXl = "TunerGeniusXL";

    /// <summary>The model a 4O3A PowerGenius XL registers with.</summary>
    public const string PowerGeniusXl = "PowerGeniusXL";

    /// <summary>A model for display: the two 4O3A names spaced as 4O3A writes them, any other as sent, or <paramref name="fallback"/> when none came.</summary>
    public static string Display(string model, string fallback) => model switch
    {
        "" => fallback,
        PowerGeniusXl => "PowerGenius XL",
        TunerGeniusXl => "TunerGenius XL",
        _ => model,
    };
}
