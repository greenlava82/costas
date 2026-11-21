// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Rig.Flex;

/// <summary>A FlexRadio 6000 or 8000 series radio found on the network by its discovery broadcast.</summary>
/// <param name="Model">The model, as the radio names it: <c>FLEX-8600</c>.</param>
/// <param name="Serial">Its serial number, or empty.</param>
/// <param name="Nickname">The name the owner gave it, or empty.</param>
/// <param name="Callsign">The callsign set in the radio, or empty.</param>
/// <param name="Ip">Its IP address.</param>
/// <param name="Port">Its SmartSDR API TCP port, normally 4992.</param>
public sealed record FlexRadioInfo(string Model, string Serial, string Nickname, string Callsign, string Ip, int Port)
{
    /// <summary>The address to give Hamlib's SmartSDR models: <c>192.168.1.20:4992</c>.</summary>
    public string Address => Ip.Contains(':', StringComparison.Ordinal) ? $"[{Ip}]:{Port}" : $"{Ip}:{Port}";
}
