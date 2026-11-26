// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Audio.Timing;
using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Geo;
using Ft8Client.Core.Messages;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Core.Wspr;
using Ft8Client.Data.Log;

namespace Ft8Client.App.Engine;

// WSPR (docs/10-wspr.md): 2-minute slots, decoded by wsprd, no contacts. Only receive is here; the beacon is W4.
public sealed partial class Session
{
    /// <summary>What a command that belongs to the FT8, FT4 or keyboard modes says in WSPR, and does nothing else.</summary>
    public const string WsprRefusal = "Not in WSPR mode.";

    private const int MaxWsprHeard = 200;
    private IWsprDecoder? _wsprDecoder;
    private WsprSpotRepository? _wsprStore;
    private readonly List<WsprHeardSpot> _wsprHeard = [];
    private DateTime? _wsprLastSlot;
    private int _wsprLastCount;
    private string _wsprState = "Waiting for the first slot.";

    /// <summary>
    /// Whether spots heard now are to be uploaded to WSPRnet, asked once per slot. Off unless the uploader sets it
    /// (docs/10-wspr.md section 5): spots heard while it is off are stored but never queued.
    /// </summary>
    public Func<bool> WsprUploadWanted { get; set; } = () => false;

    /// <summary>Raised after each decoded WSPR slot with its start, dial frequency and the spots stored (not raised for a dropped slot).</summary>
    public event Action<DateTime, long, IReadOnlyList<WsprSpot>>? WsprHeard;

    /// <summary>Gives the session the WSPR decoder and the table the spots are kept in. Without a decoder WSPR slots are skipped.</summary>
    public void AttachWspr(IWsprDecoder decoder, WsprSpotRepository? store)
    {
        lock (_gate)
        {
            _wsprDecoder = decoder;
            _wsprStore = store;
            if (store is not null && _config.MyCall.Length > 0) LoadRecentWspr(store);
        }
    }

    // Refuses a command that has no meaning in WSPR: says so in the bottom bar and returns true. Not in WSPR: returns false.
    private bool RefuseWspr()
    {
        lock (_gate)
        {
            if (!ModeInfo.IsWspr(_config.Mode)) return false;
            _fault = WsprRefusal;
        }
        Publish();
        return true;
    }

    /// <summary>Slot clock events in WSPR mode, on the slot thread. Never waits on I/O.</summary>
    private void OnWsprSlotEvent(SlotEvent e)
    {
        // The clock changes mode at a slot boundary, so the first slot after a change can still carry the old mode's marks.
        if (e.Mode != Mode.Wspr) return;
        switch (e.Mark)
        {
            case SlotMark.Start:
                // The operator may have left since this slot was planned: a transmission not yet on the air is dropped.
                CheckPresenceAtStart();
                var transmitting = _tx.Pending is { } p && p.Plan.SlotStartUtc == e.SlotStartUtc;
                lock (_gate) _recordingSlot = transmitting ? null : e.SlotStartUtc;
                if (transmitting) _source.CancelSlot();
                else _source.BeginSlot(e.SlotStartUtc, e.Mode);
                Defer(Publish);
                break;
            case SlotMark.CaptureCutoff:
                // The next slot is planned now, six seconds ahead. Any transmission has finished by here (it ends 111.6 s in), so a
                // beacon that stops for want of the operator never cuts one off.
                PlanBeaconSlot(e.SlotStartUtc + WsprTiming.SlotLength);
                OnWsprCutoff(e);
                break;
        }
        _tx.OnSlotEvent(e);
    }

    private void OnWsprCutoff(SlotEvent e)
    {
        long dial;
        lock (_gate)
        {
            if (_recordingSlot != e.SlotStartUtc) return;
            _recordingSlot = null;
            dial = _dialHz;
        }
        var audio = _source.Cutoff(e.SlotStartUtc, e.Mode);
        if (audio is null) return;
        IWsprDecoder? decoder;
        lock (_gate)
        {
            decoder = _wsprDecoder;
            if (decoder is null)
            {
                _wsprState = "No WSPR decoder (wsprd was not found).";
                _services[ServiceNames.Decoder] = new ServiceStatus(ServiceHealth.Degraded, "wsprd was not found", _clock.UtcNow);
            }
            else
            {
                DecodesStarted++;
                _wsprState = "Decoding.";
                _decodeTask = Task.Run(() => WsprDecodeAsync(decoder, audio, dial));
            }
        }
        Publish();
    }

    private async Task WsprDecodeAsync(IWsprDecoder decoder, SlotAudio audio, long dial)
    {
        try
        {
            WsprDecodeResult result;
            try
            {
                result = await decoder.DecodeAsync(audio, dial, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new WsprDecodeResult([], 0, TimeSpan.Zero, DecodeStatus.Failed, ex.Message);
            }
            ApplyWspr(audio, dial, result);
        }
        finally
        {
            Interlocked.Increment(ref _decodesApplied);
        }
    }

    // Stores and shows what a slot decoded to. A slot recorded across a band change or a dial move of more than RetuneHz
    // is dropped: not listed, stored or uploaded (as D24 does for FT8).
    private void ApplyWspr(SlotAudio audio, long dial, WsprDecodeResult result)
    {
        List<WsprSpotRow> rows;
        string call;
        lock (_gate)
        {
            _services[ServiceNames.Decoder] = result.Status == DecodeStatus.Ok
                ? new ServiceStatus(ServiceHealth.Ok, $"{result.Spots.Count} WSPR spots in {result.Elapsed.TotalSeconds:0.0} s", _clock.UtcNow)
                : new ServiceStatus(ServiceHealth.Degraded, result.Message, _clock.UtcNow);
            if (audio.Mode != _config.Mode || HeardBeforeMove(audio.SlotStartUtc))
            {
                _wsprState = "Slot dropped: the radio moved during it.";
                rows = [];
                call = string.Empty;
            }
            else if (result.Status != DecodeStatus.Ok)
            {
                _wsprState = result.Message.Length > 0 ? result.Message : "The WSPR decoder failed.";
                rows = [];
                call = string.Empty;
            }
            else
            {
                call = _config.MyCall;
                var mine = Grid.ToLatLon(_config.MyGrid);
                var wanted = WsprUploadWanted();
                // A hash that did not resolve names nobody, so WSPRnet gets nothing for it.
                rows = [.. result.Spots.Select(s => WsprSpotRow.From(s, call, DistanceTo(mine, s), wanted && s.HasCall ? WsprUploadState.Pending : WsprUploadState.None))];
                _wsprState = rows.Count == 0 ? "No WSPR signals heard in the last slot." : $"{rows.Count} heard in the last slot.";
            }
        }
        if (rows.Count == 0 && call.Length == 0)
        {
            Publish();
            return;
        }

        // The write can be slow or blocked: outside the lock, and the list shown does not wait for it.
        var stored = new List<WsprSpot>();
        foreach (var r in rows)
        {
            if (_wsprStore is { } store)
            {
                try
                {
                    if (!store.Add(r)) continue;
                }
                catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
                {
                    lock (_gate) _fault = $"Could not store a WSPR spot: {ex.Message}";
                    continue;
                }
            }
            stored.Add(r.ToSpot());
        }
        lock (_gate)
        {
            if (audio.Mode == _config.Mode && !HeardBeforeMove(audio.SlotStartUtc))
            {
                var byDistance = rows.ToDictionary(r => (r.Callsign, r.FreqHz), r => r.DistanceKm);
                foreach (var s in stored)
                    _wsprHeard.Insert(0, new WsprHeardSpot(s, byDistance.GetValueOrDefault((s.Callsign, s.FrequencyHz))));
                if (_wsprHeard.Count > MaxWsprHeard) _wsprHeard.RemoveRange(MaxWsprHeard, _wsprHeard.Count - MaxWsprHeard);
                _wsprLastSlot = audio.SlotStartUtc;
                _wsprLastCount = stored.Count;
                _lastDecodeTime = result.Elapsed;
            }
        }
        if (stored.Count > 0) Listeners.Raise(WsprHeard, audio.SlotStartUtc, dial, (IReadOnlyList<WsprSpot>)stored, nameof(WsprHeard));
        Publish();
    }

    private static double? DistanceTo(LatLon? mine, WsprSpot s) =>
        mine is { } m && Grid.ToLatLon(s.Grid) is { } there ? GreatCircle.DistanceKm(m, there) : null;

    // Under _gate: the newest stored spots of this profile, so the list is not empty after a restart.
    private void LoadRecentWspr(WsprSpotRepository store)
    {
        try
        {
            var mine = Grid.ToLatLon(_config.MyGrid);
            _wsprHeard.Clear();
            foreach (var r in store.Recent(_config.MyCall, MaxWsprHeard))
                _wsprHeard.Add(new WsprHeardSpot(r.ToSpot(), r.DistanceKm ?? DistanceTo(mine, r.ToSpot())));
            if (_wsprHeard.Count > 0) _wsprLastSlot = _wsprHeard[0].Spot.SlotStartUtc;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            _wsprHeard.Clear();
        }
    }

    // Under _gate: null outside WSPR.
    private WsprView? WsprViewAt(DateTime now) => ModeInfo.IsWspr(_config.Mode)
        ? new WsprView(_wsprHeard.ToList(), SlotMath.SlotStart(now, Mode.Wspr) + WsprTiming.SlotLength, _wsprLastSlot, _wsprLastCount, _wsprState)
        {
            Beacon = BeaconViewAt(now),
        }
        : null;
}

/// <summary>The WSPR part of a snapshot (docs/10-wspr.md section 8).</summary>
/// <param name="Recent">Signals heard, newest first.</param>
/// <param name="NextSlotUtc">Start of the next 2-minute slot.</param>
/// <param name="LastSlotUtc">Start of the last slot that was decoded, or null.</param>
/// <param name="LastSlotCount">How many signals the last decoded slot added to the list.</param>
/// <param name="DecoderState">One line on what the decoder is doing or why a slot was dropped.</param>
public sealed record WsprView(IReadOnlyList<WsprHeardSpot> Recent, DateTime NextSlotUtc, DateTime? LastSlotUtc, int LastSlotCount, string DecoderState)
{
    /// <summary>The beacon: whether it runs, what the next slot does, how long the operator has, and the power advice.</summary>
    public WsprBeaconView Beacon { get; init; } = WsprBeaconView.Off;
}
