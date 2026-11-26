// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>What the beacon does in one slot.</summary>
/// <param name="Transmit">True to transmit, false to receive.</param>
/// <param name="Reason">Why it receives, or a short note for a transmit slot; shown beside the countdown.</param>
public sealed record WsprSlotDecision(bool Transmit, string Reason)
{
    /// <summary>A transmit slot.</summary>
    public static WsprSlotDecision Tx { get; } = new(true, "Transmit");

    /// <summary>A receive slot, with why.</summary>
    public static WsprSlotDecision Rx(string reason) => new(false, reason);
}

/// <summary>What the scheduler needs to know about the beacon for one slot.</summary>
/// <param name="BeaconOn">The operator started the beacon and nothing has stopped it.</param>
/// <param name="PresenceExpired">No operator input within the presence time.</param>
/// <param name="FirstSlotAfterHop">The radio changed band just before this slot, so it is receive only (owner decision 2026-11-26).</param>
/// <param name="TransmitRefused">A guard refused the last plan; the beacon is stopped.</param>
/// <param name="Percent">The Tx percentage, 1 to 100.</param>
public readonly record struct WsprSlotFacts(bool BeaconOn, bool PresenceExpired, bool FirstSlotAfterHop, bool TransmitRefused, int Percent);

/// <summary>
/// Decides, before each slot, whether the beacon transmits or receives: the duty cycle (<see cref="WsprTxSchedule"/>, WSJT-X's
/// method) after the rules that always win. It receives when the beacon is off, the operator has not been present, it is the
/// first slot after a band hop, or a guard refused the last plan.
/// </summary>
public sealed class WsprBeaconScheduler(WsprTxSchedule schedule)
{
    /// <summary>Decides one slot.</summary>
    public WsprSlotDecision Decide(DateTime slotStartUtc, WsprSlotFacts facts)
    {
        if (!facts.BeaconOn) return WsprSlotDecision.Rx("Beacon off");
        if (facts.PresenceExpired) return WsprSlotDecision.Rx("No operator input");
        if (facts.TransmitRefused) return WsprSlotDecision.Rx("A transmit guard refused");
        if (facts.FirstSlotAfterHop) return WsprSlotDecision.Rx("First slot after a band change");
        return schedule.IsTx(slotStartUtc, facts.Percent) ? WsprSlotDecision.Tx : WsprSlotDecision.Rx("Receive slot");
    }
}
