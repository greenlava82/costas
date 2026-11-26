// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using System.Net;
using Ft8Client.Data.Log;

namespace Ft8Client.Integrations.Wsprnet;

/// <summary>
/// Posts WSPR spots to WSPRnet the way WSJT-X 3.0.1 does (docs/08-open-questions.md, V-WSPR4): one
/// <c>application/x-www-form-urlencoded</c> POST per spot to <c>/post/</c>, answered with text containing
/// <c>spot(s) added</c> on success. WSJT-X uses plain HTTP; Costas uses HTTPS (owner decision 2026-11-26).
/// </summary>
public sealed class WsprnetClient(HttpClient http, Uri? endpoint = null)
{
    /// <summary>The address spots are posted to.</summary>
    public static Uri DefaultEndpoint { get; } = new("https://wsprnet.org/post/");

    /// <summary>The text of a successful reply.</summary>
    public const string SuccessText = "spot(s) added";

    /// <summary>The mode value WSPRnet uses for WSPR-2.</summary>
    public const string ModeWspr2 = "2";

    private readonly Uri _endpoint = endpoint ?? DefaultEndpoint;

    /// <summary>
    /// The form fields of one spot. <paramref name="receiverCall"/> and <paramref name="receiverGrid"/> are mine;
    /// <paramref name="version"/> is the program version WSPRnet records.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Fields(WsprSpotRow spot, string receiverCall, string receiverGrid, string version)
    {
        var inv = CultureInfo.InvariantCulture;
        return
        [
            new("function", "wspr"),
            new("date", spot.SlotUtc.ToString("yyMMdd", inv)),
            new("time", spot.SlotUtc.ToString("HHmm", inv)),
            new("sig", spot.Snr.ToString(inv)),
            new("dt", spot.Dt.ToString("0.0", inv)),
            new("drift", spot.Drift.ToString(inv)),
            new("tqrg", (spot.FreqHz / 1_000_000.0).ToString("0.000000", inv)),
            new("tcall", spot.Callsign),
            new("tgrid", spot.Grid ?? string.Empty),
            new("dbm", spot.Dbm.ToString(inv)),
            new("version", version),
            new("rcall", receiverCall),
            new("rgrid", receiverGrid),
            new("rqrg", ((spot.DialHz + 1500) / 1_000_000.0).ToString("0.000000", inv)),
            new("mode", ModeWspr2),
        ];
    }

    /// <summary>Posts one spot. Never throws for a network problem: it says whether to try again.</summary>
    public async Task<WsprnetResult> PostAsync(WsprSpotRow spot, string receiverCall, string receiverGrid, string version, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(Fields(spot, receiverCall, receiverGrid, version));
        try
        {
            using var response = await http.PostAsync(_endpoint, content, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(response.StatusCode, text);
        }
        catch (HttpRequestException)
        {
            return WsprnetResult.TryLater("WSPRnet is unreachable; waiting to retry.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return WsprnetResult.TryLater("WSPRnet did not answer in time; waiting to retry.");
        }
    }

    /// <summary>
    /// Reads a reply. A success carries <see cref="SuccessText"/>; a server error or a rate limit is worth retrying; any
    /// other answer means WSPRnet refused the spot, which is not sent again.
    /// </summary>
    public static WsprnetResult Parse(HttpStatusCode code, string body)
    {
        if (code == HttpStatusCode.OK && body.Contains(SuccessText, StringComparison.OrdinalIgnoreCase)) return WsprnetResult.Ok;
        if ((int)code >= 500 || code == HttpStatusCode.TooManyRequests || code == HttpStatusCode.RequestTimeout)
            return WsprnetResult.TryLater($"WSPRnet answered {(int)code}; waiting to retry.");
        var detail = body.Trim();
        if (detail.Length > 120) detail = detail[..120];
        return WsprnetResult.Refused(detail.Length == 0 ? $"WSPRnet answered {(int)code}." : $"WSPRnet refused the spot: {detail}");
    }
}
