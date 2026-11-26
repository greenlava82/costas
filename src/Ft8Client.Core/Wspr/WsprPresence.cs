// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>
/// The operator-presence rule of the WSPR beacon (docs/10-wspr.md section 6.1): under 47 CFR 97.203(d) a WSPR beacon on the
/// HF bands needs a control operator present, so the beacon runs only while the operator keeps giving input at the desk or
/// on the phone. The beacon's own transmissions, band hops and decodes are not input and never come here.
/// </summary>
public sealed class WsprPresence
{
    /// <summary>The shortest presence time: one WSPR slot.</summary>
    public static readonly TimeSpan Minimum = TimeSpan.FromMinutes(2);

    private DateTime? _lastInputUtc;

    /// <summary>When the operator last gave input, or null when there has been none since the beacon started.</summary>
    public DateTime? LastInputUtc => _lastInputUtc;

    /// <summary>Starts a run: the operator has just pressed Start beacon, which counts as input.</summary>
    public void Start(DateTime utc) => _lastInputUtc = utc;

    /// <summary>Records operator input (a key, click or tap at the desk, or the phone's "I'm here").</summary>
    public void NoteOperatorInput(DateTime utc)
    {
        if (_lastInputUtc is not { } last || utc > last) _lastInputUtc = utc;
    }

    /// <summary>Forgets the input: the beacon is off.</summary>
    public void Clear() => _lastInputUtc = null;

    /// <summary>
    /// The presence time: the Tx watchdog minutes, but never less than one WSPR slot, <c>max(watchdogMinutes, 2)</c>.
    /// </summary>
    public static TimeSpan Limit(int watchdogMinutes) => TimeSpan.FromMinutes(Math.Max(watchdogMinutes, (int)Minimum.TotalMinutes));

    /// <summary>True when more than the presence time has passed since the last input (and a run has started).</summary>
    public bool Expired(DateTime nowUtc, int watchdogMinutes) =>
        _lastInputUtc is not { } last || nowUtc - last >= Limit(watchdogMinutes);

    /// <summary>The sentence the bottom bar shows when the beacon stops for lack of input.</summary>
    public static string StoppedText(int watchdogMinutes) =>
        $"Beacon stopped: no operator input for {Limit(watchdogMinutes).TotalMinutes:0} minutes (Tx watchdog).";
}
