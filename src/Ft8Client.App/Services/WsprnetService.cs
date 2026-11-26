// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.Core;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Data.Log;
using Ft8Client.Integrations.Net;
using Ft8Client.Integrations.Wsprnet;

namespace Ft8Client.App.Services;

/// <summary>
/// Uploads the WSPR signals heard to WSPRnet for the active profile, a random 5 to 25 s after each pass (WSJT-X waits
/// 0 to 20 s so the site is not hit at the same second), and keeps the WSPRnet indicator current. Off unless the
/// profile turns it on; never run in simulation; never holds up reception (docs/10-wspr.md section 5).
/// <paramref name="handler"/> replaces the network in tests.
/// </summary>
public sealed class WsprnetService(Session session, AppSettingsAccessor settings, WsprSpotRepository spots, IClock clock,
    bool uploadsAllowed, HttpMessageHandler? handler = null, Func<TimeSpan>? wait = null) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private HttpClient? _http;
    private string? _httpCall;
    private WsprnetUploader? _uploader;

    /// <summary>Starts the background worker and tells the session when to queue what it hears.</summary>
    public void Start()
    {
        session.WsprUploadWanted = () => uploadsAllowed && settings.Current.Profile.Wspr.UploadEnabled;
        _loop ??= Task.Run(() => LoopSupervisor.RunAsync("WSPRnet upload", () => RunAsync(_cts.Token), _cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var profile = settings.Current.Profile;
                if (!uploadsAllowed || !profile.Wspr.UploadEnabled)
                {
                    session.SetService(ServiceNames.Wsprnet, ServiceStatus.Off(uploadsAllowed ? "WSPRnet off" : "WSPRnet off in simulation"));
                }
                else if (string.IsNullOrWhiteSpace(profile.Callsign) || string.IsNullOrWhiteSpace(profile.Grid))
                {
                    session.SetService(ServiceNames.Wsprnet, new(ServiceHealth.Down, "Set your callsign and grid in Settings to report to WSPRnet", clock.UtcNow));
                }
                else
                {
                    try
                    {
                        // One client and uploader for the run, so the back-off is kept; a new callsign starts afresh.
                        if (_http is null || _httpCall != profile.Callsign)
                        {
                            _http?.Dispose();
                            _http = HttpFactory.Create(profile.Callsign, handler, TimeSpan.FromSeconds(10));
                            _httpCall = profile.Callsign;
                            _uploader = new WsprnetUploader(new WsprnetClient(_http), spots, clock);
                        }
                        await _uploader!.ProcessDueAsync(profile.Callsign.Trim().ToUpperInvariant(), profile.Grid.Trim(), AppInfo.Id + " " + AppInfo.Version, ct).ConfigureAwait(false);
                        session.SetService(ServiceNames.Wsprnet, _uploader.Status);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
                    {
                        session.SetService(ServiceNames.Wsprnet, new(ServiceHealth.Degraded, "WSPRnet upload failed; waiting to retry", clock.UtcNow));
                    }
                }
                await Task.Delay(wait?.Invoke() ?? TimeSpan.FromSeconds(5 + Random.Shared.NextDouble() * 20), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _http?.Dispose();
        _cts.Dispose();
    }
}
