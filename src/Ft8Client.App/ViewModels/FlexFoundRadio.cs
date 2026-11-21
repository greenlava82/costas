// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Rig.Flex;

namespace Ft8Client.App.ViewModels;

/// <summary>A FlexRadio Setup found on the network, as offered on the Radio step.</summary>
/// <param name="Info">What the radio's discovery broadcast said.</param>
public sealed record FlexFoundRadio(FlexRadioInfo Info)
{
    /// <summary>"FLEX-8600 “Shack” at 192.168.1.20".</summary>
    public string Label => Info.Nickname.Length > 0 ? $"{Info.Model} “{Info.Nickname}” at {Info.Ip}" : $"{Info.Model} at {Info.Ip}";
}
