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
using Ft8Client.Integrations.ClubLog;
using Ft8Client.Integrations.Net;

namespace Ft8Client.App.Services;

/// <summary>Runs Club Log real-time uploads for the active profile and keeps the Club Log indicator current.</summary>
public sealed class ClubLogService(Session session, AppSettingsAccessor settings, ISecretStore secrets,
    QsoRepository qsos, UploadQueueRepository queue, IClock clock, string? apiKey, bool uploadsAllowed) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private ClubLogUploadService? _worker;
    private HttpClient? _http;
    private string? _httpCall;

    /// <summary>Starts the background worker.</summary>
    public void Start() => _loop ??= Task.Run(() => LoopSupervisor.RunAsync("Club Log upload", () => RunAsync(_cts.Token), _cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var profile = settings.Current.Profile;
                var options = profile.ClubLog;
                var password = secrets.Get(SecretNames.ClubLogPassword(profile.Id));
                if (!uploadsAllowed || !options.UploadOnComplete)
                    session.SetService(ServiceNames.ClubLog, ServiceStatus.Off(uploadsAllowed ? "Club Log off" : "Club Log off in simulation"));
                else if (apiKey is null)
                    session.SetService(ServiceNames.ClubLog, new(ServiceHealth.Down, "This build has no Club Log application key", clock.UtcNow));
                else if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(password))
                    session.SetService(ServiceNames.ClubLog, new(ServiceHealth.Down, "Set the Club Log email and Application Password in Settings", clock.UtcNow));
                else
                {
                    try
                    {
                        // One client for the run, so the worker keeps its pacing and a refused login; a new callsign
                        // (User-Agent) starts afresh.
                        if (_http is null || _httpCall != profile.Callsign)
                        {
                            _http?.Dispose();
                            _http = HttpFactory.Create(profile.Callsign);
                            _httpCall = profile.Callsign;
                            _worker = new ClubLogUploadService(new ClubLogRealtimeClient(_http), qsos, queue, clock);
                        }
                        await _worker!.ProcessDueAsync(profile.Id, profile.Callsign, options.Email.Trim(), password.Trim(), apiKey, ct).ConfigureAwait(false);
                        var waiting = queue.All().Any(i => i.Target == ClubLogUploadService.Target && qsos.Get(i.QsoId)?.ProfileId == profile.Id);
                        session.SetService(ServiceNames.ClubLog, _worker.RefusedLogin is not null || _worker.Status.Health is ServiceHealth.Down or ServiceHealth.Degraded
                            ? _worker.Status
                            : new(ServiceHealth.Ok, waiting ? "Club Log waiting to retry" : "Club Log ready", clock.UtcNow));
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception)
                    {
                        // Never log the exception: a request carries the password.
                        session.SetService(ServiceNames.ClubLog, new(ServiceHealth.Degraded, "Club Log upload failed; waiting to retry", clock.UtcNow));
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
        _http?.Dispose();
        _cts.Dispose();
    }
}
