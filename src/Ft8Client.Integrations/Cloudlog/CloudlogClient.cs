// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using System.Text;
using System.Text.Json;
using Ft8Client.Data.Adif;

namespace Ft8Client.Integrations.Cloudlog;

/// <summary>
/// Wavelog and Cloudlog's QSO API (Wavelog is a Cloudlog fork with the same API; both read from their source on
/// 2026-11-07): <c>POST {site}/index.php/api/qso</c> with JSON <c>{"key", "station_profile_id", "type": "adif",
/// "string"}</c> answers 201 <c>{"status": "created"}</c>, or 400/401/403 with a reason. <c>GET
/// {site}/index.php/api/station_info/{key}</c> lists the account's station locations. A Wavelog 3.1+ token
/// (<c>wl2_…</c>) goes to the v2 API instead (<see cref="ApiFor"/>, read from docs.wavelog.org on 2026-11-18): <c>POST
/// api/v2/qso</c> as an ADIF import, <c>GET api/v2/station</c> and <c>GET api/v2/token</c>, with the token as a Bearer
/// header.
/// </summary>
public sealed class CloudlogClient(HttpClient http)
{
    /// <summary>The API a key works with: v2 for a Wavelog token starting <c>wl2_</c>, v1 for anything else.</summary>
    public static WavelogApi ApiFor(string? key) =>
        (key ?? string.Empty).Trim().StartsWith("wl2_", StringComparison.Ordinal) ? WavelogApi.V2 : WavelogApi.V1;

    /// <summary>
    /// The site's base address from what the operator typed: "log.example.com", "https://log.example.com/",
    /// ".../index.php" or a pasted ".../index.php/api/qso" all give "https://log.example.com". Null when it is not a
    /// web address.
    /// </summary>
    public static Uri? SiteUrl(string? typed)
    {
        var t = typed?.Trim() ?? string.Empty;
        if (t.Length == 0) return null;
        if (!t.Contains("://", StringComparison.Ordinal)) t = "https://" + t;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return null;
        var path = u.AbsolutePath.TrimEnd('/');
        foreach (var tail in new[] { "/api/v2", "/api/qso", "/api/station_info", "/index.php" })
        {
            var i = path.IndexOf(tail, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) path = path[..i];
        }
        return new UriBuilder(u.Scheme, u.Host, u.Port, path).Uri;
    }

    private static Uri Api(Uri site, string rest) => new(site.AbsoluteUri.TrimEnd('/') + "/index.php/api/" + rest);

    private static HttpRequestMessage RequestV2(HttpMethod method, Uri site, string resource, string key)
    {
        var request = new HttpRequestMessage(method, Api(site, "v2/" + resource));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key.Trim());
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    /// <summary>Sends one contact to the station location <paramref name="stationId"/>, through the key's API.</summary>
    public async Task<CloudlogUploadResult> UploadAsync(Uri site, string key, string stationId, AdifRecord record, CancellationToken ct)
    {
        if (ApiFor(key) == WavelogApi.V2) return await UploadV2Async(site, key, stationId, record, ct).ConfigureAwait(false);
        var body = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["key"] = key, ["station_profile_id"] = stationId, ["type"] = "adif", ["string"] = AdifWriter.Record(record),
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.PostAsync(Api(site, "qso"), content, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseResponse(response.StatusCode, text, key);
        }
        catch (HttpRequestException)
        {
            return new(false, "The logbook site is unreachable; waiting to retry.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, "The logbook site did not answer in time; waiting to retry.");
        }
    }

    // v2 takes the same ADIF record as v1 through its ADIF import, where a duplicate is counted as skipped, not an error.
    private async Task<CloudlogUploadResult> UploadV2Async(Uri site, string key, string stationId, AdifRecord record, CancellationToken ct)
    {
        if (!int.TryParse(stationId.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var station))
            return new(false, "The station id must be a number for the Wavelog v2 API; press Test in Settings to pick the station.", Permanent: true);
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["import_type"] = "adif", ["station_profile_id"] = station, ["adif"] = AdifWriter.Record(record),
        });
        using var request = RequestV2(HttpMethod.Post, site, "qso", key);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseV2Upload(response.StatusCode, text, key);
        }
        catch (HttpRequestException)
        {
            return new(false, "The logbook site is unreachable; waiting to retry.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, "The logbook site did not answer in time; waiting to retry.");
        }
    }

    /// <summary>
    /// Reads a Wavelog v2 answer to an ADIF import of one contact. Imported, or skipped as already in the log (or 409
    /// conflict), counts as accepted. A bad, expired or under-scoped token, a station the token does not own, or no v2
    /// API at the address is permanent; a rate limit carries the wait the site asked for.
    /// </summary>
    public static CloudlogUploadResult ParseV2Upload(HttpStatusCode code, string body, string? key = null)
    {
        var (data, error) = V2Envelope(body);
        if ((int)code is >= 200 and < 300 && data is { ValueKind: JsonValueKind.Object } d)
        {
            if (Int(d, "imported") > 0 || Int(d, "skipped") > 0) return new(true);
            var said = d.TryGetProperty("messages", out var m) && m.ValueKind == JsonValueKind.Array
                ? string.Join(" ", m.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                : null;
            return new(false, "The logbook did not take the contact: " + (Clean(said, key) ?? "it read no contact from the record") + ".");
        }
        if (code == HttpStatusCode.Conflict) return new(true);
        var errorCode = error is { } e1 ? Text(e1, "code") : null;
        var message = Clean(error is { } e2 ? Text(e2, "message") : null, key);
        JsonElement? details = error is { } e3 && e3.TryGetProperty("details", out var dt) && dt.ValueKind == JsonValueKind.Object ? dt : null;
        return (code, errorCode) switch
        {
            (_, "token_expired") => new(false, "The Wavelog API token has expired; make a new one in Wavelog and enter it in Settings.", Permanent: true),
            (_, "invalid_token") or (HttpStatusCode.Unauthorized, _) =>
                new(false, "The logbook refused the API token (unknown or revoked); check it in Settings.", Permanent: true),
            (_, "insufficient_scope") => new(false,
                $"The Wavelog API token lacks the {(details is { } s ? Text(s, "required_scope") : null) ?? "qso:write"} permission; add it to the token in Wavelog, or make a new one.",
                Permanent: true),
            (HttpStatusCode.Forbidden, _) => new(false, "The logbook refused the upload: " + (message ?? "the station id does not belong to this token's account") + ".", Permanent: true),
            (HttpStatusCode.NotFound, _) => new(false,
                "The logbook site has no Wavelog v2 API at that address (it needs Wavelog 3.1 or later); check the address in Settings.", Permanent: true),
            (HttpStatusCode.TooManyRequests, _) => new(false, "The logbook asked Costas to slow down; waiting to retry.",
                RetryAfter: details is { } r && Int(r, "retry_after") is > 0 and var secs ? TimeSpan.FromSeconds(secs) : null),
            _ when error is null => new(false, $"The logbook answered HTTP {(int)code} without a result; check the address in Settings."),
            _ => new(false, "The logbook did not take the contact: " + (message ?? errorCode ?? $"HTTP {(int)code}") + "."),
        };
    }

    // The v2 envelope: {"data": …} on success, {"error": {"code", "message", "details"}} on failure; both null when the
    // body is not JSON (a login page, a proxy error, or the wrong address).
    private static (JsonElement? Data, JsonElement? Error) V2Envelope(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            JsonElement? data = root.TryGetProperty("data", out var d) ? d.Clone() : null;
            JsonElement? error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object ? e.Clone() : null;
            return (data, error);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static int Int(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? 0
        : v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n
        : v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s
        : 0;

    /// <summary>What a v2 token says about itself; null for a v1 key, a refused token, or an unreachable site.</summary>
    public async Task<WavelogToken?> TokenAsync(Uri site, string key, CancellationToken ct)
    {
        if (ApiFor(key) != WavelogApi.V2) return null;
        var json = await GetV2Async(site, "token", key, ct).ConfigureAwait(false);
        return json is null ? null : ParseV2Token(json);
    }

    /// <summary>Reads the answer to <c>GET api/v2/token</c>; null when it is not one.</summary>
    public static WavelogToken? ParseV2Token(string json)
    {
        var (data, _) = V2Envelope(json);
        if (data is not { ValueKind: JsonValueKind.Object } d) return null;
        var scopes = d.TryGetProperty("scopes", out var s) && s.ValueKind == JsonValueKind.Array
            ? s.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];
        DateTime? expires = DateTime.TryParseExact(Text(d, "expires_at"), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var at) ? at : null;
        return new WavelogToken(Text(d, "name") ?? string.Empty, scopes, expires);
    }

    private async Task<string?> GetV2Async(Uri site, string resource, string key, CancellationToken ct)
    {
        try
        {
            using var request = RequestV2(HttpMethod.Get, site, resource, key);
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Reads the answer to <c>GET api/v2/station</c>; null when it is not a list of locations.</summary>
    public static IReadOnlyList<CloudlogStation>? ParseV2Stations(string json)
    {
        var (data, _) = V2Envelope(json);
        if (data is not { ValueKind: JsonValueKind.Array } d) return null;
        var list = new List<CloudlogStation>();
        foreach (var e in d.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            var id = Text(e, "id");
            if (string.IsNullOrEmpty(id)) continue;
            list.Add(new CloudlogStation(id, Text(e, "name") ?? id, (Text(e, "callsign") ?? string.Empty).ToUpperInvariant(),
                Text(e, "active") is "1" or "true"));
        }
        return list;
    }

    /// <summary>
    /// Reads an answer to a QSO upload. Created, or refused only because the contact is already there (Wavelog says
    /// "Duplicate for"), counts as accepted. A wrong key or station id is permanent.
    /// </summary>
    public static CloudlogUploadResult ParseResponse(HttpStatusCode code, string body, string? key = null)
    {
        string? status = null, reason = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String) status = s.GetString();
                if (doc.RootElement.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String) reason = r.GetString();
                if (doc.RootElement.TryGetProperty("messages", out var m) && m.ValueKind == JsonValueKind.Array)
                {
                    var joined = string.Join(" ", m.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (joined.Length > 0) reason = reason is null ? joined : reason + " " + joined;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON: a login page, a proxy error, or the wrong address.
        }
        if (string.Equals(status, "created", StringComparison.OrdinalIgnoreCase)) return new(true);
        if (reason is not null && reason.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)) return new(true);
        var clean = Clean(reason, key);
        return code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new(false, "The logbook refused the upload: " + (clean ?? "check the API key and station id") + ".", Permanent: true),
            HttpStatusCode.NotFound => new(false, "The logbook site has no QSO API at that address; check the address in Settings.", Permanent: true),
            HttpStatusCode.TooManyRequests => new(false, "The logbook asked Costas to slow down; waiting to retry."),
            _ when status is null => new(false, $"The logbook answered HTTP {(int)code} without a result; check the address in Settings."),
            _ => new(false, "The logbook did not take the contact: " + (clean ?? status) + "."),
        };
    }

    /// <summary>The account's station locations; null when the key or address is wrong or the site is unreachable.</summary>
    public async Task<IReadOnlyList<CloudlogStation>?> StationsAsync(Uri site, string key, CancellationToken ct)
    {
        if (ApiFor(key) == WavelogApi.V2)
            return await GetV2Async(site, "station", key, ct).ConfigureAwait(false) is { } json ? ParseV2Stations(json) : null;
        try
        {
            using var response = await http.GetAsync(Api(site, "station_info/" + Uri.EscapeDataString(key)), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return ParseStations(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Reads the station_info answer; null when it is not a JSON array.</summary>
    public static IReadOnlyList<CloudlogStation>? ParseStations(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<CloudlogStation>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var id = Text(e, "station_id");
                if (string.IsNullOrEmpty(id)) continue;
                list.Add(new CloudlogStation(id, Text(e, "station_profile_name") ?? id, (Text(e, "station_callsign") ?? string.Empty).ToUpperInvariant(),
                    Text(e, "station_active") is "1" or "true"));
            }
            return list;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? null
        : v.ValueKind == JsonValueKind.String ? v.GetString()
        : v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? v.GetRawText()
        : null;

    // Keeps the site's reason short and never echoes the key or HTML back into the status line.
    private static string? Clean(string? reason, string? key)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var r = System.Text.RegularExpressions.Regex.Replace(reason, "<[^>]*>", " ").Trim();
        if (!string.IsNullOrEmpty(key)) r = r.Replace(key, "…", StringComparison.Ordinal);
        r = System.Text.RegularExpressions.Regex.Replace(r, @"\s+", " ").TrimEnd('.');
        return r.Length > 160 ? r[..160] + "…" : r;
    }
}
