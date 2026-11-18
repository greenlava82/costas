// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Rig.Flex;

namespace Ft8Client.App.ViewModels;

/// <summary>
/// What the bottom bar's amp and tuner button and its flyout show for a FlexRadio's paired amplifier and tuner
/// (docs/02-ui-spec.md, bottom bar "Amp and tuner"). Each button is offered only when it would change something and
/// nothing is on the air.
/// </summary>
public sealed record AccessoryPanel(
    bool HasAmp,
    string AmpName,
    string AmpState,
    TextKind AmpKind,
    bool CanAmpOperate,
    bool CanAmpStandby,
    bool HasTuner,
    string TunerName,
    string TunerState,
    bool CanTunerOperate,
    bool CanTunerStandby,
    bool CanTunerBypass,
    bool CanTunerInLine,
    string BarText,
    TextKind BarKind)
{
    /// <summary>Nothing paired, or not a FlexRadio: the button is hidden.</summary>
    public static AccessoryPanel None { get; } = From(FlexAccessories.Empty, canChange: false);

    /// <summary>True when there is anything to show.</summary>
    public bool Any => HasAmp || HasTuner;

    /// <summary>Builds the panel from what the radio reports; <paramref name="canChange"/> is false while anything is on the air.</summary>
    public static AccessoryPanel From(FlexAccessories a, bool canChange)
    {
        var amp = a.Amplifier;
        var tuner = a.Tuner;
        var ampState = amp is null ? string.Empty : AmpText(amp.State);
        var ampKind = amp?.State switch
        {
            FlexAmpState.Fault => TextKind.Critical,
            FlexAmpState.TransmitA or FlexAmpState.TransmitB => TextKind.Critical,
            FlexAmpState.Idle => TextKind.Caution,
            _ => TextKind.Secondary,
        };
        var starting = amp?.State is FlexAmpState.Unknown or FlexAmpState.PowerUp or FlexAmpState.SelfCheck;
        var tunerState = tuner is null ? string.Empty
            : tuner.Tuning ? "Tuning"
            : !tuner.Operate ? "Standby"
            : tuner.Bypass ? "Bypass" : "Operate";
        var tunerFree = canChange && tuner is { Tuning: false };
        var parts = new List<string>(2);
        if (amp is not null) parts.Add($"Amp {ampState}");
        if (tuner is not null) parts.Add($"Tuner {tunerState}");
        return new AccessoryPanel(
            amp is not null, amp?.Name ?? string.Empty, ampState, ampKind,
            canChange && amp is { Operate: false } && !starting,
            canChange && amp is { Operate: true, Transmitting: false },
            tuner is not null, tuner?.Name ?? string.Empty, tunerState,
            tunerFree && !tuner!.Operate,
            tunerFree && tuner!.Operate,
            tunerFree && tuner!.Operate && !tuner.Bypass,
            tunerFree && tuner!.Operate && tuner.Bypass,
            string.Join(" · ", parts),
            amp is null ? TextKind.Secondary : ampKind);
    }

    private static string AmpText(FlexAmpState s) => s switch
    {
        FlexAmpState.Standby => "Standby",
        FlexAmpState.Idle => "Operate",
        FlexAmpState.TransmitA or FlexAmpState.TransmitB => "Transmitting",
        FlexAmpState.Fault => "Fault",
        FlexAmpState.PowerUp => "Starting",
        FlexAmpState.SelfCheck => "Self-check",
        _ => "Unknown",
    };
}
