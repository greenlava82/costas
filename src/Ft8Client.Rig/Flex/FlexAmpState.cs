// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Rig.Flex;

/// <summary>
/// An amplifier's state as a FlexRadio reports it in the <c>state=</c> key of its <c>amplifier</c> status
/// (FlexLib <c>Amplifier.cs</c>; docs/05-integrations.md section 2a).
/// </summary>
public enum FlexAmpState
{
    /// <summary>Not reported yet, or a value Costas does not know.</summary>
    Unknown,

    /// <summary><c>POWERUP</c>: starting.</summary>
    PowerUp,

    /// <summary><c>SELFCHECK</c>: checking itself after power-up.</summary>
    SelfCheck,

    /// <summary><c>STANDBY</c>: bypassed; the radio goes out at its own power.</summary>
    Standby,

    /// <summary><c>IDLE</c>: in operate, receiving.</summary>
    Idle,

    /// <summary><c>TRANSMIT_A</c>: in operate, transmitting from the radio on input A.</summary>
    TransmitA,

    /// <summary><c>TRANSMIT_B</c>: in operate, transmitting from the radio on input B.</summary>
    TransmitB,

    /// <summary><c>FAULT</c>: the amplifier has tripped.</summary>
    Fault,
}
