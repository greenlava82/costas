// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Time;

/// <summary>
/// When to act on a burst of requests: <see cref="Quiet"/> after the last one, but never later than <see cref="MaxWait"/>
/// after the first, so a steady stream of changes still gets acted on. After a failure the work is due again
/// <see cref="Retry"/> later, doubling up to <see cref="MaxRetry"/>, unless a new request makes it due sooner.
/// Not thread-safe: the caller serialises access. Times are UTC.
/// </summary>
public sealed class Debounce(TimeSpan quiet, TimeSpan maxWait, TimeSpan retry, TimeSpan maxRetry)
{
    private DateTime? _first, _last, _retryAt;
    private TimeSpan _nextRetry = retry;

    /// <summary>How long it waits after the last request.</summary>
    public TimeSpan Quiet { get; } = quiet;

    /// <summary>The longest it waits after the first request of a burst.</summary>
    public TimeSpan MaxWait { get; } = maxWait;

    /// <summary>The first wait after a failure.</summary>
    public TimeSpan Retry { get; } = retry;

    /// <summary>The longest wait after repeated failures.</summary>
    public TimeSpan MaxRetry { get; } = maxRetry;

    /// <summary>True while work is waiting to be done.</summary>
    public bool Pending => _first is not null || _retryAt is not null;

    /// <summary>When the waiting work is due, or null when nothing waits.</summary>
    public DateTime? DueUtc
    {
        get
        {
            DateTime? burst = _first is { } f && _last is { } l ? Min(l + Quiet, f + MaxWait) : null;
            return (burst, _retryAt) switch
            {
                ({ } b, { } r) => Min(b, r),
                ({ } b, null) => b,
                (null, { } r) => r,
                _ => null,
            };
        }
    }

    /// <summary>Records a request.</summary>
    public void Request(DateTime nowUtc)
    {
        _first ??= nowUtc;
        _last = nowUtc;
    }

    /// <summary>True when work is waiting and due at <paramref name="nowUtc"/>.</summary>
    public bool IsDue(DateTime nowUtc) => DueUtc is { } d && nowUtc >= d;

    /// <summary>Call when the work starts: requests made from now on start a new burst.</summary>
    public void Begin()
    {
        _first = _last = null;
        _retryAt = null;
    }

    /// <summary>Call when the work succeeded: the retry wait goes back to <see cref="Retry"/>.</summary>
    public void Succeeded() => _nextRetry = Retry;

    /// <summary>Call when the work failed: it is due again after the retry wait, which then doubles.</summary>
    public void Failed(DateTime nowUtc)
    {
        _retryAt = nowUtc + _nextRetry;
        _nextRetry = Min(_nextRetry + _nextRetry, MaxRetry);
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
