// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Data.Log;
using Ft8Client.Data.Secrets;
using Ft8Client.Integrations.Cloudlog;
using Ft8Client.Integrations.Net;

namespace Ft8Client.App.Services;

/// <summary>
/// Optional uploads to the operator's own Wavelog or Cloudlog logbook (owner request 2026-11-07, from the Aurora
/// comparison). Contacts are queued when logged and sent in the background; simulation never reaches the site. A
/// Wavelog v2 token (<c>wl2_…</c>) is used with the v2 API, any other key with v1 (owner request 2026-11-18).
/// <paramref name="handler"/> replaces the network in tests.
/// </summary>
public sealed class CloudlogService(Session session, AppSettingsAccessor settings, ISecretStore secrets,
    QsoRepository qsos, UploadQueueRepository queue, IClock clock, bool uploadsAllowed, HttpMessageHandler? handler = null) : IAsyncDisposable
{
    // Test with a v2 token: what the token may do first (any token may ask), then its station locations if it may list
    // them. Sends no contact.
    internal static async Task<(string Message, IReadOnlyList<CloudlogStation> Stations)> TestV2Async(CloudlogClient client, Uri site, string key,
        bool upload, CancellationToken ct)
    {
        if (await client.TokenAsync(site, key, ct).ConfigureAwait(false) is not { } token)
            return ($"{site.Host} did not accept that token (expired or revoked), or is not Wavelog 3.1 or later at that address.", []);
        var rights = (token.CanRead, token.CanWrite) switch
        {
            (true, true) => "can read and upload contacts",
            (false, true) => "can upload contacts",
            (true, false) => "can read contacts but not upload them",
            _ => "cannot read or upload contacts",
        };
        var name = token.Name.Length > 0 ? $"Token \"{token.Name}\"" : "The token";
        var message = $"{site.Host} accepted the v2 token. {name} {rights}"
            + (token.ExpiresAt is { } at ? $", until {at:yyyy-MM-dd}." : ".");
        if (upload && !token.CanWrite) message += " Uploads need the qso:write scope: add it to the token in Wavelog.";
        if (!token.CanListStations) return (message + " It cannot list station locations (no station:read scope): type the station id.", []);
        var stations = await client.StationsAsync(site, key, ct).ConfigureAwait(false);
        if (stations is null) return (message + " Listing the station locations failed: type the station id.", []);
        if (stations.Count == 0) return (message + " The account has no station locations yet.", []);
        return (message + $" {stations.Count} station location{(stations.Count == 1 ? "" : "s")}: pick the one for this callsign.", stations);
    }

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <summary>Starts the background worker.</summary>
    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    /// <summary>
    /// Checks an address and key from Settings before they are saved: lists the account's station locations, or says
    /// what is wrong. Sends no contact.
    /// </summary>
    public async Task<(string Message, IReadOnlyList<CloudlogStation> Stations)> TestAsync(string url, string key, CancellationToken ct, bool upload = false)
    {
        if (!uploadsAllowed) return ("Wavelog/Cloudlog is off in simulation.", []);
        if (CloudlogClient.SiteUrl(url) is not { } site) return ("Enter your logbook's web address, such as https://log.example.com.", []);
        if (string.IsNullOrWhiteSpace(key)) return ("Enter an API key from your logbook (Account › API keys, read and write).", []);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        using var http = HttpFactory.Create(settings.Current.Profile.Callsign, handler);
        var client = new CloudlogClient(http);
        if (CloudlogClient.ApiFor(key) == WavelogApi.V2) return await TestV2Async(client, site, key.Trim(), upload, linked.Token).ConfigureAwait(false);
        var stations = await client.StationsAsync(site, key.Trim(), linked.Token).ConfigureAwait(false);
        if (stations is null) return ($"{site.Host} did not accept that key, or has no Wavelog/Cloudlog API at that address.", []);
        if (stations.Count == 0) return ($"{site.Host} accepted the key, but the account has no station locations yet.", []);
        return ($"{site.Host} accepted the key: {stations.Count} station location{(stations.Count == 1 ? "" : "s")}. Pick the one for this callsign.", stations);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var profile = settings.Current.Profile;
                var options = profile.Cloudlog;
                var key = secrets.Get(SecretNames.CloudlogKey(profile.Id));
                var site = CloudlogClient.SiteUrl(options.Url);
                if (!uploadsAllowed || !options.UploadOnComplete)
                    session.SetService(ServiceNames.Cloudlog, ServiceStatus.Off(uploadsAllowed ? "Wavelog/Cloudlog off" : "Wavelog/Cloudlog off in simulation"));
                else if (site is null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(options.StationId))
                    session.SetService(ServiceNames.Cloudlog, new(ServiceHealth.Down, "Set the logbook address, API key and station in Settings", clock.UtcNow));
                else
                {
                    try
                    {
                        using var http = HttpFactory.Create(profile.Callsign, handler);
                        var worker = new CloudlogUploadService(new CloudlogClient(http), qsos, queue, clock);
                        await worker.ProcessDueAsync(profile.Id, profile.Callsign, site, key.Trim(), options.StationId.Trim(), ct).ConfigureAwait(false);
                        if (worker.Status.Health != ServiceHealth.Off) session.SetService(ServiceNames.Cloudlog, worker.Status);
                        else if (queue.All().Any(i => i.Target == CloudlogUploadService.Target && qsos.Get(i.QsoId)?.ProfileId == profile.Id))
                            session.SetService(ServiceNames.Cloudlog, new(ServiceHealth.Degraded, "Wavelog/Cloudlog waiting to retry", clock.UtcNow));
                        else session.SetService(ServiceNames.Cloudlog, new(ServiceHealth.Ok, $"{site.Host} ready", clock.UtcNow));
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception)
                    {
                        // Never log the exception: a request carries the API key.
                        session.SetService(ServiceNames.Cloudlog, new(ServiceHealth.Degraded, "Wavelog/Cloudlog upload failed; waiting to retry", clock.UtcNow));
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _cts.Dispose();
    }
}
