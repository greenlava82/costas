// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Services;
using Ft8Client.Rig.Flex;

namespace Ft8Client.App.ViewModels;

/// <summary>
/// One line under the FlexRadio address in Settings saying whether Costas reached the radio and what it found, so a
/// wrong address or an amplifier not paired with the radio is obvious at once.
/// </summary>
public static class FlexStatusText
{
    /// <summary>The line and its colour; empty when no address is set.</summary>
    public static (string Text, TextKind Kind) For(ServiceStatus status, FlexAccessories found)
    {
        if (status.Health == ServiceHealth.Off)
            return status.Message.StartsWith("No FlexRadio address", StringComparison.Ordinal) || status.Message == "Not started"
                ? (string.Empty, TextKind.Secondary)
                : (status.Message + ".", TextKind.Caution);
        if (status.Health == ServiceHealth.Down) return (status.Message + ". Check the address, and that the radio is on.", TextKind.Critical);
        if (status.Health == ServiceHealth.Degraded) return (status.Message + "…", TextKind.Caution);
        var names = found.Amplifiers.Select(a => a.Name).Concat(found.Tuners.Select(t => t.Name)).ToList();
        return names.Count == 0
            ? ("Connected to the radio, but it reports no amplifier or tuner. Check in SmartSDR that they are paired with it.", TextKind.Caution)
            : ($"Connected. Found {string.Join(" and ", names)}; switch them from the bottom bar.", TextKind.Success);
    }
}
