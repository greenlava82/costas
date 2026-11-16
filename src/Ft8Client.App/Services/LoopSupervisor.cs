// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Serilog;

namespace Ft8Client.App.Services;

/// <summary>
/// Keeps a background loop running: an unexpected exception is logged and the loop starts again after a back-off
/// (1 s, doubling to 1 minute), instead of the loop ending silently for the rest of the session (evaluation B9).
/// The back-off starts over once the loop has run for <see cref="StableAfter"/> without a fault.
/// </summary>
public static class LoopSupervisor
{
    /// <summary>The first wait before a restart.</summary>
    public static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait before a restart.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    /// <summary>A loop that ran this long before failing is restarted after <see cref="FirstBackoff"/> again.</summary>
    public static readonly TimeSpan StableAfter = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Runs <paramref name="loop"/> until it returns or <paramref name="ct"/> is cancelled, restarting it after any
    /// other exception. <paramref name="faulted"/> hears each fault (for a status line or a crash report).
    /// <paramref name="delay"/> is for tests.
    /// </summary>
    public static async Task RunAsync(string name, Func<Task> loop, CancellationToken ct, Action<string, Exception>? faulted = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        var backoff = FirstBackoff;
        while (!ct.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            try
            {
                await loop().ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (DateTime.UtcNow - started >= StableAfter) backoff = FirstBackoff;
                Log.Error(ex, "Background task {Loop} failed; restarting in {Seconds:0} s", name, backoff.TotalSeconds);
                faulted?.Invoke(name, ex);
            }
            try
            {
                await delay(backoff, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }
}
