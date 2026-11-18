// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Remote;

/// <summary>
/// Slows down password guessing on the remote page. Failures are counted per username and per client address; after
/// <see cref="FreeFailures"/> of either, sign-in is refused for <see cref="FirstLockout"/>, doubling with each further
/// failure up to <see cref="MaxLockout"/>. A successful sign-in clears that username and address.
/// </summary>
/// <remarks>
/// Each password check is reserved with <see cref="TryBegin"/> before the slow hash and counts against the limit while it
/// runs, so guesses sent all at once get no more tries than guesses sent one after another. No more than
/// <see cref="MaxConcurrent"/> checks run at a time, so a flood of sign-ins cannot take every processor from decoding;
/// the rest are refused at once with <see cref="BusyWait"/> rather than queued.
/// </remarks>
public sealed class LoginThrottle
{
    /// <summary>Failures allowed before the first wait.</summary>
    public const int FreeFailures = 5;

    /// <summary>The first wait.</summary>
    public static readonly TimeSpan FirstLockout = TimeSpan.FromSeconds(30);

    /// <summary>The longest wait.</summary>
    public static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(15);

    /// <summary>Password checks allowed at once by default.</summary>
    public const int DefaultMaxConcurrent = 2;

    /// <summary>The wait given when a check is refused because others are still running.</summary>
    public static readonly TimeSpan BusyWait = TimeSpan.FromSeconds(1);

    // More keys than this and the oldest are forgotten, so a flood of made-up usernames cannot grow memory without end.
    private const int MaxKeys = 4096;

    private readonly object _gate = new();
    private readonly Dictionary<string, (int Failures, DateTime Until, DateTime Last)> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _inFlight = new(StringComparer.Ordinal);
    private int _inFlightTotal;

    /// <summary>Creates a throttle.</summary>
    /// <param name="maxConcurrent">Password checks allowed at once; at least one.</param>
    public LoginThrottle(int maxConcurrent = DefaultMaxConcurrent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrent, 1);
        MaxConcurrent = maxConcurrent;
    }

    /// <summary>Password checks allowed at once.</summary>
    public int MaxConcurrent { get; }

    /// <summary>Password checks reserved and not yet finished.</summary>
    public int InFlight
    {
        get { lock (_gate) return _inFlightTotal; }
    }

    /// <summary>
    /// Reserves one password check for <paramref name="username"/> from <paramref name="address"/>. Returns the
    /// reservation, to be finished with <see cref="LoginAttempt.Failed"/> or <see cref="LoginAttempt.Succeeded"/> (or
    /// disposed), or null with <paramref name="wait"/> set when the check must not run: locked out, the free tries
    /// already taken by checks still running, or <see cref="MaxConcurrent"/> checks running.
    /// </summary>
    public LoginAttempt? TryBegin(string username, string address, DateTime nowUtc, out TimeSpan wait)
    {
        lock (_gate)
        {
            var keys = Keys(username, address);
            wait = TimeSpan.Zero;
            foreach (var k in keys)
            {
                if (_keys.TryGetValue(k, out var e) && e.Until > nowUtc && e.Until - nowUtc > wait) wait = e.Until - nowUtc;
            }
            if (wait > TimeSpan.Zero) return null;
            foreach (var k in keys)
            {
                // Every failure up to the free ones may still be running at once; past them, one check at a time.
                var failures = _keys.TryGetValue(k, out var e) ? e.Failures : 0;
                var allowed = Math.Max(1, FreeFailures + 1 - failures);
                if (_inFlight.GetValueOrDefault(k) >= allowed)
                {
                    wait = BusyWait;
                    return null;
                }
            }
            if (_inFlightTotal >= MaxConcurrent)
            {
                wait = BusyWait;
                return null;
            }
            foreach (var k in keys) _inFlight[k] = _inFlight.GetValueOrDefault(k) + 1;
            _inFlightTotal++;
            return new LoginAttempt(this, username, address);
        }
    }

    /// <summary>Ends a reservation: counts a failure, clears on success, or just releases it.</summary>
    internal void Finish(LoginAttempt attempt, bool? succeeded, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (attempt.Finished) return;
            attempt.Finished = true;
            foreach (var k in Keys(attempt.Username, attempt.Address))
            {
                var n = _inFlight.GetValueOrDefault(k) - 1;
                if (n > 0) _inFlight[k] = n;
                else _inFlight.Remove(k);
            }
            _inFlightTotal--;
            if (succeeded == true) Succeeded(attempt.Username, attempt.Address);
            else if (succeeded == false) Failed(attempt.Username, attempt.Address, nowUtc);
        }
    }

    /// <summary>How long until <paramref name="username"/> from <paramref name="address"/> may try again; zero when now.</summary>
    public TimeSpan Wait(string username, string address, DateTime nowUtc)
    {
        lock (_gate)
        {
            var wait = TimeSpan.Zero;
            foreach (var k in Keys(username, address))
            {
                if (_keys.TryGetValue(k, out var e) && e.Until > nowUtc && e.Until - nowUtc > wait) wait = e.Until - nowUtc;
            }
            return wait;
        }
    }

    /// <summary>Counts a wrong username or password.</summary>
    public void Failed(string username, string address, DateTime nowUtc)
    {
        lock (_gate)
        {
            foreach (var k in Keys(username, address))
            {
                var failures = (_keys.TryGetValue(k, out var e) ? e.Failures : 0) + 1;
                var until = failures <= FreeFailures ? DateTime.MinValue
                    : nowUtc + TimeSpan.FromTicks(Math.Min(MaxLockout.Ticks, FirstLockout.Ticks << Math.Min(20, failures - FreeFailures - 1)));
                _keys[k] = (failures, until, nowUtc);
            }
            if (_keys.Count > MaxKeys)
            {
                foreach (var k in _keys.OrderBy(p => p.Value.Last).Take(_keys.Count - MaxKeys).Select(p => p.Key).ToList()) _keys.Remove(k);
            }
        }
    }

    /// <summary>Clears the count after a successful sign-in.</summary>
    public void Succeeded(string username, string address)
    {
        lock (_gate)
        {
            foreach (var k in Keys(username, address)) _keys.Remove(k);
        }
    }

    private static string[] Keys(string username, string address) =>
        ["u:" + username.Trim().ToUpperInvariant(), "a:" + address];
}
