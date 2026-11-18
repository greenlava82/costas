// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Rig.Flex;

/// <summary>An amplifier paired with the radio, such as a 4O3A PowerGenius XL, as last reported.</summary>
/// <param name="Handle">The radio's handle for it, <c>0x18E5F0AA</c>; commands name it.</param>
/// <param name="Model">The model it registered with, <c>PowerGeniusXL</c>.</param>
/// <param name="State">Its state.</param>
public sealed record FlexAmplifier(string Handle, string Model, FlexAmpState State)
{
    /// <summary>
    /// True when the amplifier is in operate (amplifying when the radio transmits): any known state but standby, as
    /// FlexLib reads it. False while starting or unknown.
    /// </summary>
    public bool Operate => State is FlexAmpState.Idle or FlexAmpState.TransmitA or FlexAmpState.TransmitB or FlexAmpState.Fault;

    /// <summary>True while the amplifier says it is transmitting.</summary>
    public bool Transmitting => State is FlexAmpState.TransmitA or FlexAmpState.TransmitB;

    /// <summary>The model for display, <c>PowerGenius XL</c>.</summary>
    public string Name => FlexModelNames.Display(Model, "Amplifier");
}
