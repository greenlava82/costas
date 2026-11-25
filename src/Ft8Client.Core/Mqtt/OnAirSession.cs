// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Time;

namespace Ft8Client.Core.Mqtt;

/// <summary>
/// The on-air session published to an MQTT broker so that Home Assistant (or anything else) can switch off loads
/// that RF upsets, once per operating session rather than once per over:
/// <list type="bullet">
/// <item>A session starts when Costas commits to a transmission (a message queued for a slot, a keyboard line, Tune)
/// or PTT keys, and no session is running.</item>
/// <item>Each commit and each PTT edge counts as activity. While PTT is keyed the session cannot time out.</item>
/// <item>It ends after <see cref="OnAirSessionOptions.IdleTimeout"/> with no activity, on Halt, when the radio is
/// lost, when Costas closes, or at <see cref="OnAirSessionOptions.MaxLength"/> (once PTT is released).</item>
/// </list>
/// Timers run on the monotonic clock, so a system time change cannot end or stretch a session. Thread-safe;
/// <see cref="Changed"/> is raised in order, on the caller's thread, and its handlers must not block. Nothing here
/// does I/O.
/// </summary>
public sealed class OnAirSession
{
    private readonly object _gate = new();
    private readonly IClock _clock;
    private readonly Func<string> _newId;
    private OnAirSessionOptions _options;
    private OnAirSnapshot _snapshot = OnAirSnapshot.Idle;
    private long _startTicks;
    private long _lastActivityTicks;

    /// <summary>Creates an idle tracker.</summary>
    /// <param name="clock">Wall time for published timestamps; monotonic ticks for the timers.</param>
    /// <param name="options">Idle timeout and maximum length.</param>
    /// <param name="newId">Makes session ids; a short random id when null.</param>
    public OnAirSession(IClock clock, OnAirSessionOptions options, Func<string>? newId = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _newId = newId ?? (() => Guid.NewGuid().ToString("N")[..12]);
    }

    /// <summary>Raised after every change, with the new snapshot. Handlers must return at once (queue the work).</summary>
    public event Action<OnAirSnapshot>? Changed;

    /// <summary>The current state.</summary>
    public OnAirSnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    /// <summary>Idle timeout and maximum length; a change applies from the next <see cref="Tick"/>.</summary>
    public OnAirSessionOptions Options
    {
        get { lock (_gate) return _options; }
        set { lock (_gate) _options = value ?? throw new ArgumentNullException(nameof(value)); }
    }

    /// <summary>Costas has committed to a transmission: starts a session when none is running.</summary>
    public void Commit() => Apply(() =>
    {
        var now = _clock.Ticks;
        _lastActivityTicks = now;
        return _snapshot.Active ? null : Start(now, _snapshot.Transmitting);
    });

    /// <summary>A PTT edge. Keying starts a session when none is running.</summary>
    public void Ptt(bool keyed) => Apply(() =>
    {
        var now = _clock.Ticks;
        if (keyed == _snapshot.Transmitting) return null;
        _lastActivityTicks = now;
        if (keyed && !_snapshot.Active) return Start(now, true);
        return _snapshot with { Transmitting = keyed };
    });

    /// <summary>Ends a running session for <paramref name="reason"/>; does nothing when none is running.</summary>
    public void End(SessionEndReason reason) => Apply(() => _snapshot.Active ? Ended(reason) : null);

    /// <summary>Checks the idle timeout and the maximum length. Call about once a second.</summary>
    public void Tick() => Apply(() =>
    {
        if (!_snapshot.Active || _snapshot.Transmitting) return null;
        var now = _clock.Ticks;
        if (now - _startTicks >= _options.MaxLength.Ticks) return Ended(SessionEndReason.MaxLength);
        if (now - _lastActivityTicks >= _options.IdleTimeout.Ticks) return Ended(SessionEndReason.IdleTimeout);
        return null;
    });

    private OnAirSnapshot Start(long nowTicks, bool transmitting)
    {
        _startTicks = nowTicks;
        return new OnAirSnapshot(true, _newId(), _clock.UtcNow, null, transmitting);
    }

    private OnAirSnapshot Ended(SessionEndReason reason) => _snapshot with { Active = false, SinceUtc = _clock.UtcNow, EndReason = reason };

    private void Apply(Func<OnAirSnapshot?> change)
    {
        lock (_gate)
        {
            var next = change();
            if (next is null || next == _snapshot) return;
            _snapshot = next;
            // Raised inside the lock so subscribers see changes in order; they must only queue, never block.
            Changed?.Invoke(next);
        }
    }
}
