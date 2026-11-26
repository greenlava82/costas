// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Data.Log;

namespace Ft8Client.Integrations.Wsprnet;

/// <summary>
/// Sends the WSPR spots waiting in the <c>wspr_spot</c> table to WSPRnet, oldest first (docs/10-wspr.md section 5). The
/// queue is the table, so it survives a restart. On a network or server problem it stops, keeps everything, and waits
/// 1, 2, 4 ... up to 30 minutes before trying again; a spot WSPRnet refuses is marked failed and never sent again, and
/// a spot too old to be worth sending is dropped the same way. It never throws and never touches reception.
/// </summary>
public sealed class WsprnetUploader(WsprnetClient client, WsprSpotRepository spots, IClock clock)
{
    /// <summary>A spot older than this when its turn comes is marked failed instead of sent (WSPRnet reports are about now).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>Longest wait between tries after repeated trouble.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    private const int BatchSize = 100;
    private int _failures;
    private DateTime _nextTryUtc = DateTime.MinValue;

    /// <summary>The pause WSJT-X leaves between spots (200 ms); tests set it to zero.</summary>
    public TimeSpan Pace { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>The indicator state after the last pass.</summary>
    public ServiceStatus Status { get; private set; } = ServiceStatus.Off("WSPRnet off");

    /// <summary>Spots sent and accepted since the app started.</summary>
    public int Sent { get; private set; }

    /// <summary>Spots WSPRnet refused, or that were too old, since the app started.</summary>
    public int Failed { get; private set; }

    /// <summary>When the next pass may send, or <see cref="DateTime.MinValue"/> when it may now.</summary>
    public DateTime NextTryUtc => _nextTryUtc;

    /// <summary>The wait after <paramref name="failures"/> failed passes in a row: 1 minute, doubling, at most 30.</summary>
    public static TimeSpan Backoff(int failures) =>
        failures <= 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(Math.Min(Math.Pow(2, Math.Min(failures - 1, 10)), MaxBackoff.TotalMinutes));

    /// <summary>
    /// Sends what is waiting for <paramref name="profileCall"/>, unless a back-off is running. Returns the number of
    /// spots WSPRnet accepted in this pass.
    /// </summary>
    public async Task<int> ProcessDueAsync(string profileCall, string receiverGrid, string version, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (now < _nextTryUtc) return 0;
        var accepted = 0;
        while (!ct.IsCancellationRequested)
        {
            var batch = spots.Pending(profileCall, BatchSize);
            if (batch.Count == 0) break;
            foreach (var spot in batch)
            {
                if (ct.IsCancellationRequested) return accepted;
                if (clock.UtcNow - spot.SlotUtc > MaxAge)
                {
                    spots.SetUploadState([spot.Id], WsprUploadState.Failed);
                    Failed++;
                    continue;
                }
                var result = await client.PostAsync(spot, profileCall, receiverGrid, version, ct).ConfigureAwait(false);
                if (result.Accepted)
                {
                    spots.SetUploadState([spot.Id], WsprUploadState.Uploaded);
                    accepted++;
                    Sent++;
                    _failures = 0;
                    Status = new ServiceStatus(ServiceHealth.Ok, $"WSPRnet: {Sent} spots sent", clock.UtcNow);
                }
                else if (result.Retry)
                {
                    _failures++;
                    _nextTryUtc = clock.UtcNow + Backoff(_failures);
                    Status = new ServiceStatus(ServiceHealth.Degraded, $"{result.Message} Next try in {Backoff(_failures).TotalMinutes:0} min.", clock.UtcNow);
                    return accepted;
                }
                else
                {
                    spots.SetUploadState([spot.Id], WsprUploadState.Failed);
                    Failed++;
                    Status = new ServiceStatus(ServiceHealth.Degraded, result.Message, clock.UtcNow);
                }
                if (Pace > TimeSpan.Zero) await Task.Delay(Pace, ct).ConfigureAwait(false);
            }
        }
        _nextTryUtc = DateTime.MinValue;
        if (_failures == 0 && Status.Health != ServiceHealth.Degraded)
            Status = new ServiceStatus(ServiceHealth.Ok, Sent == 0 ? "WSPRnet ready" : $"WSPRnet: {Sent} spots sent", clock.UtcNow);
        return accepted;
    }
}
