// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core;
using Ft8Client.Core.Contacts;
using Ft8Client.Core.Services;
using Ft8Client.Core.Transmit;
using Ft8Client.Core.Time;
using Ft8Client.Core.Wspr;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Engine;

/// <summary>Sets the radio's power for the WSPR beacon and puts it back (docs/10-wspr.md section 6.3).</summary>
public interface IBeaconPower
{
    /// <summary>
    /// Remembers the radio's power and sets <paramref name="percent"/> of its maximum (at least 1 W). Returns null when the radio
    /// took it, else a line for the operator ("Set the power on the radio; Costas cannot"). Never blocks the slot thread.
    /// </summary>
    Task<string?> ApplyAsync(int percent);

    /// <summary>Puts the power back as it was before <see cref="ApplyAsync"/>, once nothing is being transmitted.</summary>
    Task RestoreAsync();
}

// The WSPR beacon (docs/10-wspr.md section 6): started only by the operator, on only while the operator keeps giving input,
// planned before every slot, with every guard the other modes have.
public sealed partial class Session
{
    private bool _beaconOn;
    private readonly WsprPresence _presence = new();
    private WsprBeaconScheduler _beaconScheduler = new(new WsprTxSchedule(Random.Shared));
    private WsprMessageCycle? _cycle;
    private DateTime? _beaconPlannedSlot;
    private DateTime? _receiveOnlySlot; // set by band hopping (W6): this slot only receives
    private WsprSlotDecision? _nextDecision;
    private DateTime? _nextDecisionSlot;
    private string? _lastBeaconMessage;
    private string? _beaconPowerNote;
    private bool _beaconTransmitRefused;
    private Task _beaconTask = Task.CompletedTask;

    /// <summary>The beacon's settings for the active profile, read when it starts and before each slot.</summary>
    public Func<WsprOptions> WsprSettings { get; set; } = () => new WsprOptions();

    /// <summary>Sets and restores the radio's power around a beacon run. Null when there is no radio power control.</summary>
    public IBeaconPower? BeaconPower { get; set; }

    /// <summary>The radio's power setting in watts if it reports it, for the estimate shown beside the reported dBm.</summary>
    public Func<double?> RadioWatts { get; set; } = () => null;

    /// <summary>Replaces the random numbers the Tx slot table draws, so a test (or a seeded run) is repeatable.</summary>
    public void SetWsprRandom(Random random)
    {
        lock (_gate) _beaconScheduler = new WsprBeaconScheduler(new WsprTxSchedule(random));
    }

    /// <summary>True while the beacon is running.</summary>
    public bool BeaconOn
    {
        get { lock (_gate) return _beaconOn; }
    }

    /// <summary>
    /// Starts the beacon (an operator action: Start beacon at the desk or on the phone). Returns null once started, or why it
    /// did not: not in WSPR mode, my callsign, locator or power cannot be sent exactly as they are, or the audio offset is
    /// outside the WSPR window. It starts nothing else: the first transmission is planned before its slot, through every guard.
    /// </summary>
    public async Task<string?> StartBeaconAsync()
    {
        var options = WsprSettings();
        string call, grid;
        lock (_gate)
        {
            if (!ModeInfo.IsWspr(_config.Mode)) return Refuse(WsprRefusal);
            if (_beaconOn) return null;
            (call, grid) = (_config.MyCall, _config.MyGrid);
        }
        if (Check(options, call, grid) is { } why) return Refuse(why);
        var cycle = new WsprMessageCycle(call, grid, options.ReportedDbm, options.PreferType1);
        var now = _clock.UtcNow;
        lock (_gate)
        {
            if (_beaconOn) return null;
            _beaconOn = true;
            _cycle = cycle;
            _presence.Start(now);
            _beaconPlannedSlot = null;
            _beaconTransmitRefused = false;
            _lastBeaconMessage = null;
            _beaconPowerNote = null;
            _fault = null;
        }
        var power = BeaconPower;
        if (power is not null)
        {
            var note = await power.ApplyAsync(options.PowerPercent).ConfigureAwait(false);
            lock (_gate) _beaconPowerNote = note;
        }
        // The next slot is planned now if there is time to render it; otherwise at the cut-off of the slot running.
        var next = SlotMath.SlotStart(now, Mode.Wspr) + WsprTiming.SlotLength;
        if (next - now > TimeSpan.FromSeconds(2)) PlanBeaconSlot(next);
        Publish();
        return null;
    }

    // Why the beacon cannot start with these settings, or null: every message it would send is checked, so a refusal is
    // heard now and not on the air (CLAUDE.md rule 4), and the offset is never moved for the operator.
    private static string? Check(WsprOptions o, string call, string grid)
    {
        if (call.Length == 0 || grid.Length == 0) return "Beacon not started: set your callsign and locator in Settings.";
        if (o.TxPercent is < 1 or > 100) return $"Beacon not started: Tx percent is {o.TxPercent}; use 1 to 100.";
        if (o.AttenuationDb is > 0 or < -20) return $"Beacon not started: attenuation is {o.AttenuationDb} dB; use 0 to -20.";
        if (o.PowerPercent is < 1 or > 100) return $"Beacon not started: power is {o.PowerPercent} %; use 1 to 100.";
        if (!WsprPower.IsLegal(o.ReportedDbm)) return $"Beacon not started: {o.ReportedDbm} dBm cannot be sent. WSPR power is 0 to 60 dBm ending in 0, 3 or 7.";
        if (o.OffsetHz < WsprTiming.WindowLowHz || o.OffsetHz > WsprTiming.WindowHighHz)
            return $"Beacon not started: the audio offset {o.OffsetHz} Hz is outside the WSPR window, {WsprTiming.WindowLowHz} to {WsprTiming.WindowHighHz} Hz.";
        foreach (var m in new WsprMessageCycle(call, grid, o.ReportedDbm, o.PreferType1).Messages)
        {
            var r = WsprEncoder.Encode(m);
            if (!r.Ok) return $"Beacon not started: \"{m.Text}\" cannot be sent. {r.Refusal}";
        }
        return null;
    }

    private string Refuse(string why)
    {
        lock (_gate) _fault = why;
        Publish();
        return why;
    }

    /// <summary>
    /// Stops the beacon: nothing more is planned or sent, and the radio's power goes back. A transmission already on the air
    /// finishes (Halt Tx cuts it). <paramref name="reason"/> goes in the bottom bar; null for the operator's own Stop.
    /// </summary>
    public void StopBeacon(string? reason)
    {
        lock (_gate)
        {
            if (!_beaconOn) return;
            _beaconOn = false;
            _cycle = null;
            _presence.Clear();
            _beaconPlannedSlot = null;
            _nextDecision = null;
            _nextDecisionSlot = null;
            _receiveOnlySlot = null;
            if (reason is not null) _fault = reason;
        }
        // A transmission planned for the slot that has not started is dropped; one on the air is left to finish.
        _tx.CancelPending();
        var power = BeaconPower;
        if (power is not null) _ = power.RestoreAsync();
        Publish();
    }

    /// <summary>The phone's "I'm here": operator input for the beacon, nothing else.</summary>
    public void NoteWsprPresence()
    {
        lock (_gate)
        {
            if (_beaconOn) _presence.NoteOperatorInput(_clock.UtcNow);
        }
    }

    // Operator input counts for the beacon too (Activity above feeds the Tx watchdog the same way). Under _gate.
    private void NotePresenceLocked()
    {
        if (_beaconOn) _presence.NoteOperatorInput(_clock.UtcNow);
    }

    // The presence time: the Tx watchdog minutes, with a remote device's cap, never under one slot. Under _gate.
    private int PresenceMinutes => CappedContact().WatchdogMinutes;

    // Under _gate: true when the beacon is on and the operator has been away too long.
    private bool PresenceExpiredLocked(DateTime now) => _beaconOn && _presence.Expired(now, PresenceMinutes);

    // At the start of a slot: the operator left since the slot was planned, so a pending transmission is dropped before PTT.
    private void CheckPresenceAtStart()
    {
        string? text = null;
        lock (_gate)
        {
            if (PresenceExpiredLocked(_clock.UtcNow)) text = WsprPresence.StoppedText(PresenceMinutes);
        }
        if (text is null) return;
        _tx.CancelPending();
        StopBeacon(text);
    }

    // Plans the slot that starts at slotStartUtc, on its own task so a long render never holds up the slot clock.
    private void PlanBeaconSlot(DateTime slotStartUtc)
    {
        lock (_gate)
        {
            if (!_beaconOn || _beaconPlannedSlot == slotStartUtc) return;
            _beaconPlannedSlot = slotStartUtc;
            DecodesStarted++;
            _beaconTask = Task.WhenAll(_beaconTask, Task.Run(() => PlanBeacon(slotStartUtc)));
        }
    }

    private void PlanBeacon(DateTime slotStartUtc)
    {
        try
        {
            string? stop = null;
            WsprMessage? message = null;
            WsprOptions options;
            TxGuardInput guard;
            lock (_gate)
            {
                if (!_beaconOn) return;
                options = WsprSettings();
                var now = _clock.UtcNow;
                if (PresenceExpiredLocked(now))
                {
                    stop = WsprPresence.StoppedText(PresenceMinutes);
                }
                else
                {
                    var facts = new WsprSlotFacts(true, false, _receiveOnlySlot == slotStartUtc, _beaconTransmitRefused, options.TxPercent);
                    var decision = _beaconScheduler.Decide(slotStartUtc, facts);
                    _nextDecision = decision;
                    _nextDecisionSlot = slotStartUtc;
                    if (decision.Transmit) message = _cycle?.Next();
                }
                guard = new TxGuardInput(_config.Band, _dialHz, ClockEstimate.Offset(_sntpOffset, null), _config.ClockBlockSeconds,
                    _services[ServiceNames.Radio].Health == ServiceHealth.Down ? _services[ServiceNames.Radio].Message : null, true);
            }
            if (stop is not null)
            {
                StopBeacon(stop);
                return;
            }
            if (message is not null) PrepareBeaconTx(slotStartUtc, message, options, guard);
            Publish();
        }
        finally
        {
            Interlocked.Increment(ref _decodesApplied);
        }
    }

    // Encodes, checks and installs one transmission. A guard that says no receives the slot instead, shows why and stops the
    // beacon: it never tries again by itself.
    private void PrepareBeaconTx(DateTime slotStartUtc, WsprMessage message, WsprOptions options, TxGuardInput guard)
    {
        var enc = WsprEncoder.Encode(message);
        if (!enc.Ok)
        {
            lock (_gate) _beaconTransmitRefused = true;
            StopBeacon($"Beacon stopped: \"{message.Text}\" cannot be sent. {enc.Refusal}");
            return;
        }
        var plan = new TxPlan(slotStartUtc, message.Text, null, null);
        var prepared = _tx.PrepareWspr(plan, options.OffsetHz, guard,
            (rate, peak) => WsprWaveform.Render(enc.Symbols, options.OffsetHz, rate, options.AttenuationDb, peak), out var refusal);
        if (prepared is null)
        {
            lock (_gate) _beaconTransmitRefused = true;
            StopBeacon(refusal is null ? "Beacon stopped: transmitting was halted." : $"Beacon stopped. {refusal}");
            return;
        }
        lock (_gate) _lastBeaconMessage = message.Text;
    }

    // The beacon's part of the WSPR screen, under _gate.
    private WsprBeaconView BeaconViewAt(DateTime now)
    {
        var options = WsprSettings();
        var nextSlot = SlotMath.SlotStart(now, Mode.Wspr) + WsprTiming.SlotLength;
        bool? nextTx = _nextDecisionSlot == nextSlot ? _nextDecision?.Transmit : null;
        var watts = RadioWatts();
        double? estimate = watts is { } w ? WsprPower.EstimateDbm(w, options.AttenuationDb) : null;
        var meter = _txMetersLive?.Watts is { } mw && mw > 0 ? WsprPower.EstimateDbm(mw, 0) : (double?)null;
        string? warning = null;
        if (estimate is { } e && Math.Abs(e - options.ReportedDbm) > 3)
            warning = $"The radio is set for about {WsprPower.Display(e)} dBm, but the message says {options.ReportedDbm} dBm.";
        else if (meter is { } m && Math.Abs(m - options.ReportedDbm) > 3)
            warning = $"The radio's meter reads about {WsprPower.Display(m)} dBm, but the message says {options.ReportedDbm} dBm.";
        return new WsprBeaconView(_beaconOn, nextTx, _nextDecisionSlot == nextSlot ? _nextDecision?.Reason ?? string.Empty : string.Empty,
            _beaconOn && _presence.LastInputUtc is { } last ? last + WsprPresence.Limit(PresenceMinutes) : null,
            _beaconPowerNote, _lastBeaconMessage, options.TxPercent, options.ReportedDbm, options.AttenuationDb,
            estimate is null ? null : WsprPower.Display(estimate.Value), meter is null ? null : WsprPower.Display(meter.Value), warning);
    }
}

/// <summary>The beacon's part of the WSPR screen (docs/10-wspr.md section 8).</summary>
/// <param name="On">The beacon is running.</param>
/// <param name="NextSlotTransmits">Whether the next slot transmits, or null when it has not been decided yet.</param>
/// <param name="NextSlotNote">Why the next slot receives, when it does.</param>
/// <param name="PresenceEndsUtc">When the beacon stops if the operator gives no more input, or null when it is off.</param>
/// <param name="PowerNote">What setting the radio's power said ("Set the power on the radio; Costas cannot"), or null.</param>
/// <param name="LastMessage">The message sent last, or null.</param>
/// <param name="TxPercent">The Tx percentage.</param>
/// <param name="ReportedDbm">The power the message reports.</param>
/// <param name="AttenuationDb">The audio attenuation.</param>
/// <param name="EstimateDbm">What the radio's power and the attenuation should give, or null when the radio does not report its power.</param>
/// <param name="MeterDbm">What the radio's output meter reads, or null.</param>
/// <param name="PowerWarning">A warning when the estimate or the meter differs from the reported power by more than 3 dB, or null. Advice only.</param>
public sealed record WsprBeaconView(bool On, bool? NextSlotTransmits, string NextSlotNote, DateTime? PresenceEndsUtc, string? PowerNote,
    string? LastMessage, int TxPercent, int ReportedDbm, int AttenuationDb, double? EstimateDbm, double? MeterDbm, string? PowerWarning)
{
    /// <summary>A beacon that is off, with the settings shown.</summary>
    public static WsprBeaconView Off { get; } = new(false, null, string.Empty, null, null, null, 20, 37, 0, null, null, null);
}
