// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Geo;
using Ft8Client.Core.Time;
using Ft8Client.Data.Adif;
using Ft8Client.Data.Log;

namespace Ft8Client.Integrations.Cloudlog;

/// <summary>
/// Downloads one Wavelog station location's contacts into the local log for worked-before (owner request 2026-11-18,
/// D27; v2 tokens only). The first run reads the whole log; later runs ask only for contacts after the last Wavelog id
/// seen, kept in <c>sync_state</c> per site and station location. Contacts merge by the usual duplicate rule; one
/// that matches a contact waiting to upload to Wavelog leaves the upload queue, since Wavelog already has it.
/// Downloaded contacts are never queued for upload.
/// </summary>
public sealed class WavelogSyncService(CloudlogClient client, QsoRepository qsos, UploadQueueRepository queue, SyncStateRepository state,
    CountryFile countries, IClock clock, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    /// <summary>The <c>sync_state</c> target: one cursor per site and station location, so changing either starts over.</summary>
    public static string Target(Uri site, string stationId) =>
        $"wavelog:{site.Host}{(site.IsDefaultPort ? "" : ":" + site.Port)}{site.AbsolutePath.TrimEnd('/')}:{stationId.Trim()}";

    /// <summary>Downloads what is new, page by page, at most one request a second.</summary>
    public async Task<WavelogSyncResult> SyncAsync(string profileId, string callsign, Uri site, string key, string stationId, CancellationToken ct)
    {
        var target = Target(site, stationId);
        var (_, last) = state.Get(profileId, target);
        var full = last is null;
        var since = last ?? 0;
        int fetched = 0, inserted = 0, merged = 0;
        for (var page = 0; ; page++)
        {
            ct.ThrowIfCancellationRequested();
            if (page > 0) await _delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            var r = await client.DownloadPageAsync(site, key, stationId, since, ct).ConfigureAwait(false);
            if (r.Page is not { } p) return new(fetched, inserted, merged, full, r.Error ?? "Download failed", r.Permanent, r.RetryAfter);

            if (p.Adif is { Length: > 0 } adif)
            {
                var records = AdifReader.Read(adif)
                    .Select(a => AdifMapper.ToQso(a, profileId, callsign, QsoSource.Wavelog, countries))
                    .OfType<QsoRecord>()
                    .ToList();
                fetched += records.Count;
                foreach (var (id, isNew) in qsos.UpsertEach(records))
                {
                    if (isNew) inserted++;
                    else if (id != 0)
                    {
                        merged++;
                        queue.RemoveFor(id, CloudlogUploadService.Target);
                    }
                }
            }
            var moved = p.LastFetchedId > since;
            if (moved) since = p.LastFetchedId;
            state.Set(profileId, target, clock.UtcNow, since == 0 ? null : since);
            if (!p.HasMore || p.Exported == 0 || !moved) break;
        }
        return new(fetched, inserted, merged, full);
    }
}
