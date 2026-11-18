// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Data.Adif;
using Ft8Client.Data.Log;
using Ft8Client.Data.Settings;

namespace Ft8Client.Integrations.ClubLog;

/// <summary>
/// Drains the Club Log part of the persistent upload queue, one contact every 2 seconds at most (real-time upload is
/// for contacts as they are made, not batches). A refused login stops all uploads until the credentials change or the
/// operator clears the stop, as Club Log requires; the stop is kept in <paramref name="stops"/> so a restart does not
/// send again. A rejected record is dropped from the queue with the reason; faults retry with back-off.
/// </summary>
public sealed class ClubLogUploadService(ClubLogRealtimeClient client, QsoRepository qsos, UploadQueueRepository queue, IClock clock,
    Func<TimeSpan, CancellationToken, Task>? delay = null, IClubLogStopStore? stops = null)
{
    /// <summary>Persistent queue target.</summary>
    public const string Target = "clublog";

    /// <summary>Shortest time between two uploads.</summary>
    public static readonly TimeSpan Spacing = TimeSpan.FromSeconds(2);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly Backoff _backoff = Backoff.Network();
    private readonly IClubLogStopStore _stops = stops ?? new MemoryClubLogStopStore();
    private DateTime _lastRequest = DateTime.MinValue;
    private string? _refusedLogin;

    /// <summary>Visible service status after the last run.</summary>
    public ServiceStatus Status { get; private set; } = ServiceStatus.Off("Club Log not set up");

    /// <summary>True while uploads are stopped because Club Log refused the login.</summary>
    public bool Stopped { get; private set; }

    /// <summary>
    /// Uploads the due contacts of this profile and callsign, stopping at the first fault. Sends nothing while a stop
    /// recorded for this email and callsign stands. A different email or callsign clears it, as does a different
    /// password seen by this instance (a password changed in Settings clears the stored stop there).
    /// </summary>
    public async Task<int> ProcessDueAsync(string profileId, string callsign, string email, string password, string apiKey, CancellationToken ct)
    {
        // Only in memory: the stored stop holds nothing derived from the password.
        var login = Fingerprint(email, password, callsign);
        if (_stops.Load(profileId) is { } stop)
        {
            var passwordChanged = _refusedLogin is not null && _refusedLogin != login;
            if (stop.Covers(email, callsign) && !passwordChanged)
            {
                Stopped = true;
                Status = new(ServiceHealth.Down, Describe(stop), clock.UtcNow);
                return 0;
            }
            _stops.Save(profileId, null);
        }
        Stopped = false;
        _refusedLogin = null;
        var done = 0;
        foreach (var item in queue.All().Where(i => i.Target == Target && i.NextTryUtc <= clock.UtcNow))
        {
            ct.ThrowIfCancellationRequested();
            var q = qsos.Get(item.QsoId);
            if (q is null) { queue.Remove(item.Id); continue; }
            if (q.ProfileId != profileId || !string.Equals(q.StationCallsign, callsign, StringComparison.OrdinalIgnoreCase)) continue;
            var wait = _lastRequest + Spacing - clock.UtcNow;
            if (wait > TimeSpan.Zero) await _delay(wait, ct).ConfigureAwait(false);
            _lastRequest = clock.UtcNow;
            var result = await client.UploadAsync(email, password, callsign, AdifMapper.ToAdif(q), apiKey, ct).ConfigureAwait(false);
            switch (result.Outcome)
            {
                case ClubLogOutcome.Stored:
                    queue.Remove(item.Id);
                    Status = new(ServiceHealth.Ok, $"Uploaded {q.Call} to Club Log", clock.UtcNow);
                    done++;
                    continue;
                case ClubLogOutcome.Rejected:
                    queue.Remove(item.Id);
                    Status = new(ServiceHealth.Degraded, $"{q.Call}: {result.Message}", clock.UtcNow);
                    continue;
                case ClubLogOutcome.Stop:
                    _refusedLogin = login;
                    Stopped = true;
                    var refused = new ClubLogStop { Email = email, Callsign = callsign, StoppedUtc = clock.UtcNow, Reason = result.Message };
                    _stops.Save(profileId, refused);
                    Status = new(ServiceHealth.Down, Describe(refused), clock.UtcNow);
                    return done;
                default:
                    queue.Retry(item.Id, result.Message, clock.UtcNow + _backoff.DelayFor(item.Attempts + 1));
                    Status = new(ServiceHealth.Degraded, result.Message, clock.UtcNow);
                    return done;
            }
        }
        return done;
    }

    /// <summary>The status line for a stop: what Club Log said and how to start uploads again.</summary>
    public static string Describe(ClubLogStop stop) =>
        FormattableString.Invariant($"Club Log refused the login at {stop.StoppedUtc:yyyy-MM-dd HH:mm} UTC ({stop.Reason}). Uploads are stopped until you change the email or Application Password, or resume them, in Settings.");

    // Changes whenever the email, password or callsign does, without keeping the password in memory longer than needed.
    private static string Fingerprint(string email, string password, string callsign) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{email}\n{password}\n{callsign}")));
}
