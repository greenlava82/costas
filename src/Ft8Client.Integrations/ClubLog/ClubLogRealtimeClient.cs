// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Net;
using Ft8Client.Data.Adif;

namespace Ft8Client.Integrations.ClubLog;

/// <summary>
/// Club Log's real-time upload (docs/05-integrations.md): one contact per <c>POST https://clublog.org/realtime.php</c>,
/// form-encoded <c>email</c>, <c>password</c> (an Application Password), <c>callsign</c>, <c>adif</c> (one record) and
/// <c>api</c> (the application's key). 200 is stored (OK, Duplicate or Modified), 400 is a rejected record, 403 means
/// stop: the credentials or key are wrong and retrying gets the address firewalled, 500 is a fault to retry later.
/// </summary>
public sealed class ClubLogRealtimeClient
{
    /// <summary>The upload address.</summary>
    public const string Url = "https://clublog.org/realtime.php";

    private readonly HttpClient _http;
    private readonly Uri _url;

    /// <summary>Creates a client on an <see cref="HttpClient"/> that identifies the app.</summary>
    public ClubLogRealtimeClient(HttpClient http, Uri? url = null)
    {
        _http = http;
        _url = url ?? new Uri(Url);
    }

    /// <summary>Uploads one contact. Never throws for network or service faults; never puts a credential in the result.</summary>
    public async Task<ClubLogUploadResult> UploadAsync(string email, string password, string callsign, AdifRecord record, string apiKey, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = email, ["password"] = password, ["callsign"] = callsign, ["adif"] = AdifWriter.Record(record), ["api"] = apiKey,
        });
        try
        {
            using var resp = await _http.PostAsync(_url, form, ct).ConfigureAwait(false);
            var body = Short(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return resp.StatusCode switch
            {
                HttpStatusCode.OK => new ClubLogUploadResult(ClubLogOutcome.Stored, body),
                HttpStatusCode.BadRequest => new ClubLogUploadResult(ClubLogOutcome.Rejected, $"Club Log rejected the contact: {body}"),
                HttpStatusCode.Forbidden => new ClubLogUploadResult(ClubLogOutcome.Stop,
                    $"Club Log refused the login ({body}). Uploads are stopped until you change the email or app password in Settings."),
                _ => new ClubLogUploadResult(ClubLogOutcome.Retry, $"Club Log answered {(int)resp.StatusCode}; waiting to retry"),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new ClubLogUploadResult(ClubLogOutcome.Retry, "Club Log could not be reached; waiting to retry");
        }
    }

    // The message is for the operator; keep it to one short line.
    private static string Short(string body)
    {
        var line = body.ReplaceLineEndings(" ").Trim();
        return line.Length > 160 ? line[..160] + "…" : line;
    }
}
