// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Globalization;
using Ft8Client.Core.Stations;

namespace Ft8Client.Integrations.SuperFox;

/// <summary>
/// Checks a SuperFox signature with the service WSJT-X uses (its <c>Network/FoxVerifier</c>):
/// <c>GET https://www.9dx.cc/check/&lt;fox&gt;/&lt;slot time, ISO 8601 UTC&gt;/&lt;code&gt;.text</c>, which answers one line ending
/// in <c>VERIFIED</c> or <c>INVALID</c>. Only the Fox's call, the slot time and the code are sent (and the app's
/// User-Agent). Any failure gives <see cref="FoxCheck.None"/>.
/// </summary>
public sealed class FoxVerifyClient
{
    /// <summary>The service's address.</summary>
    public const string BaseUrl = "https://www.9dx.cc";

    /// <summary>How long a check may take; WSJT-X allows 5 s.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly string _base;

    /// <summary>Creates a client on an <see cref="HttpClient"/> that identifies the app.</summary>
    public FoxVerifyClient(HttpClient http, string? baseUrl = null)
    {
        _http = http;
        _base = (baseUrl ?? BaseUrl).TrimEnd('/');
    }

    /// <summary>The address checked for a signature heard in the slot starting at <paramref name="slotUtc"/>.</summary>
    public Uri UrlFor(SuperFoxSignature sig, DateTime slotUtc) =>
        new($"{_base}/check/{Uri.EscapeDataString(sig.FoxCall)}/{slotUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}/{sig.Code}.text");

    /// <summary>Checks one signature.</summary>
    public async Task<FoxCheck> CheckAsync(SuperFoxSignature sig, DateTime slotUtc, CancellationToken ct)
    {
        if (!sig.Checkable) return FoxCheck.None;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            using var resp = await _http.GetAsync(UrlFor(sig, slotUtc), timeout.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return FoxCheck.None;
            var body = await resp.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Read(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return FoxCheck.None;
        }
    }

    /// <summary>Reads the service's answer (<c>2026-11-15T00:00:00Z K8R 920749 VERIFIED</c>).</summary>
    public static FoxCheck Read(string body)
    {
        var line = body.Trim();
        if (line.EndsWith(" VERIFIED", StringComparison.Ordinal)) return FoxCheck.Verified;
        if (line.EndsWith(" INVALID", StringComparison.Ordinal)) return FoxCheck.Invalid;
        return FoxCheck.None;
    }
}
