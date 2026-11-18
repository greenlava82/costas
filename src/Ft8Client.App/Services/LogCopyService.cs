// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Time;
using Ft8Client.Data.Adif;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Ft8Client.App.Services;

/// <summary>
/// Keeps the ADIF log copy (<see cref="AdifLogMirror"/>) current. Every change to the log calls <see cref="Request"/>,
/// which only notes the time and wakes the loop, so logging and PTT are never held up. The loop runs on the thread
/// pool: it waits until the log has been quiet for <see cref="Quiet"/> (at most <see cref="MaxWait"/> after the first
/// change), then rewrites the files. It also writes once at start. A failed write (disk full, folder gone) is logged,
/// shown in <see cref="Status"/> and the bottom bar, and tried again after 1 minute, doubling up to 30.
/// </summary>
public sealed class LogCopyService
{
    /// <summary>How long the log must be quiet before the copy is written.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(5);

    /// <summary>The longest a change waits while changes keep coming (a QRZ sync or a long import).</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(60);

    /// <summary>The first wait after a failed write.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);

    /// <summary>The longest wait after repeated failures.</summary>
    public static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(30);

    private readonly AdifLogMirror _mirror;
    private readonly IClock _clock;
    private readonly Debounce _debounce;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _wake = new(0, 1);

    /// <summary>Creates the service. Pass shorter waits only in tests.</summary>
    public LogCopyService(AdifLogMirror mirror, IClock clock, TimeSpan? quiet = null, TimeSpan? maxWait = null, TimeSpan? retry = null)
    {
        _mirror = mirror;
        _clock = clock;
        _debounce = new Debounce(quiet ?? Quiet, maxWait ?? MaxWait, retry ?? Retry, MaxRetry);
        Status = new LogCopyStatus(mirror.Folder, SafeExisting(), null, null, null);
    }

    /// <summary>The latest status.</summary>
    public LogCopyStatus Status { get; private set; }

    /// <summary>Raised after each write attempt, on the loop's thread.</summary>
    public event Action<LogCopyStatus>? StatusChanged;

    /// <summary>How many writes were attempted and their status published (for tests).</summary>
    public int Attempts => Volatile.Read(ref _attempts);

    private int _attempts;

    /// <summary>Notes that the log changed. Returns at once; safe from any thread.</summary>
    public void Request()
    {
        lock (_gate) _debounce.Request(_clock.UtcNow);
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already woken.
        }
    }

    /// <summary>Writes at start, then after each quiet period following changes, until cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        Request();
        while (!ct.IsCancellationRequested)
        {
            DateTime? due;
            lock (_gate) due = _debounce.DueUtc;
            var now = _clock.UtcNow;
            if (due is null)
            {
                await _wake.WaitAsync(ct).ConfigureAwait(false);
                continue;
            }
            if (now < due)
            {
                await _wake.WaitAsync(due.Value - now, ct).ConfigureAwait(false);
                continue;
            }
            lock (_gate) _debounce.Begin();
            // Never write on the thread that asked (a Release can resume this loop inline).
            await Task.Yield();
            WriteOnce();
        }
    }

    // Writes the files and records the outcome.
    private void WriteOnce()
    {
        var now = _clock.UtcNow;
        try
        {
            var files = _mirror.Write(now);
            lock (_gate) _debounce.Succeeded();
            Status = new LogCopyStatus(_mirror.Folder, files, now, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            DateTime? next;
            lock (_gate)
            {
                _debounce.Failed(now);
                next = _debounce.DueUtc;
            }
            Log.Warning(ex, "Could not write the ADIF log copy to {Folder}", _mirror.Folder);
            Status = Status with { Files = SafeExisting(), Error = ex.Message, NextTryUtc = next };
        }
        Interlocked.Increment(ref _attempts);
        StatusChanged?.Invoke(Status);
    }

    private IReadOnlyList<string> SafeExisting()
    {
        try
        {
            return _mirror.Existing();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
