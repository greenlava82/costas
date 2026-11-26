// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Text.RegularExpressions;
using Ft8Client.Data.Adif;

namespace Ft8Client.Integrations.Lotw;

/// <summary>
/// Downloads LoTW confirmations from <c>lotwreport.adi</c> (see docs/05-integrations.md). The password travels in the
/// query string, as the API requires, over HTTPS; the address is never logged.
/// </summary>
public sealed partial class LotwReportClient
{
    /// <summary>The report address.</summary>
    public const string Url = "https://lotw.arrl.org/lotwuser/lotwreport.adi";

    /// <summary>
    /// How long LoTW may take to start sending. LoTW can be silent for minutes before the first byte: a first download
    /// of years of confirmations, or a busy evening at ARRL, takes many minutes (issue #219: 2 minutes was not enough).
    /// </summary>
    public static readonly TimeSpan FirstByteTimeout = TimeSpan.FromMinutes(15);

    /// <summary>How long the download may go quiet once LoTW has started sending.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(3);

    /// <summary>The longest one download may take from start to end.</summary>
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(30);

    private readonly HttpClient _http;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates a client on an <see cref="HttpClient"/> that identifies the app. The client's own timeout must be at least
    /// <see cref="TotalTimeout"/> (or infinite): this class keeps its own deadlines.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="time">The clock for the deadlines; the system clock when null.</param>
    public LotwReportClient(HttpClient http, TimeProvider? time = null)
    {
        _http = http;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The request address (holds the password: never log it).</summary>
    /// <param name="login">LoTW login name.</param>
    /// <param name="password">LoTW website password.</param>
    /// <param name="since">The last <c>APP_LoTW_LASTQSL</c> (or a <c>YYYY-MM-DD</c> date); null for every confirmation.</param>
    /// <param name="ownCall">Only contacts made as this call; null for every call on the account.</param>
    public static string BuildUrl(string login, string password, string? since, string? ownCall)
    {
        var q = new List<string>
        {
            "login=" + Uri.EscapeDataString(login),
            "password=" + Uri.EscapeDataString(password),
            "qso_query=1",
            "qso_qsl=yes",
            "qso_qsldetail=yes",
            "qso_withown=yes",
            "qso_qslsince=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(since) ? "1900-01-01" : since),
        };
        if (!string.IsNullOrWhiteSpace(ownCall)) q.Add("qso_owncall=" + Uri.EscapeDataString(ownCall));
        return Url + "?" + string.Join('&', q);
    }

    /// <summary>
    /// Downloads confirmations received since <paramref name="since"/>. LoTW gets <see cref="FirstByteTimeout"/> to start
    /// sending, then <see cref="IdleTimeout"/> between pieces, and <see cref="TotalTimeout"/> in all.
    /// </summary>
    public async Task<LotwReport> FetchAsync(string login, string password, string? since, string? ownCall, CancellationToken ct)
    {
        var started = false;
        using var total = new CancellationTokenSource(TotalTimeout, _time);
        using var quiet = new CancellationTokenSource(FirstByteTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, total.Token, quiet.Token);
        string text;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(login, password, since, ownCall));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new LotwReport([], null, $"LoTW answered {(int)response.StatusCode}");
            var body = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                using var bytes = new MemoryStream();
                var buffer = new byte[16384];
                int n;
                while ((n = await body.ReadAsync(buffer, linked.Token).ConfigureAwait(false)) > 0)
                {
                    started = true;
                    quiet.CancelAfter(IdleTimeout);
                    bytes.Write(buffer, 0, n);
                }
                text = Decode(bytes.ToArray(), response.Content.Headers.ContentType?.CharSet);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new LotwReport([], null, total.IsCancellationRequested
                ? $"LoTW did not finish sending in {TotalTimeout.TotalMinutes:0} minutes"
                : started
                    ? $"LoTW stopped sending for {IdleTimeout.TotalMinutes:0} minutes"
                    : $"LoTW did not answer in {FirstByteTimeout.TotalMinutes:0} minutes");
        }
        catch (HttpRequestException)
        {
            return new LotwReport([], null, started ? "The LoTW download was cut off" : "LoTW unreachable");
        }
        catch (IOException)
        {
            return new LotwReport([], null, started ? "The LoTW download was cut off" : "LoTW unreachable");
        }
        return Parse(text);
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        var encoding = System.Text.Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = System.Text.Encoding.GetEncoding(charset.Trim('"', ' '));
            }
            catch (ArgumentException)
            {
            }
        }
        return encoding.GetString(bytes);
    }

    /// <summary>
    /// Parses a report. A failed query comes back as an HTML page with no ADIF end-of-header, usually a wrong login or
    /// password.
    /// </summary>
    public static LotwReport Parse(string text)
    {
        var eoh = text.IndexOf("<eoh>", StringComparison.OrdinalIgnoreCase);
        if (eoh < 0)
        {
            return new LotwReport([], null, text.Contains("password", StringComparison.OrdinalIgnoreCase) || text.Contains("login", StringComparison.OrdinalIgnoreCase)
                ? "LoTW did not accept the login name or password"
                : "LoTW sent something other than a report");
        }
        var m = LastQslField().Match(text[..eoh]);
        var last = m.Success ? m.Groups[2].Value[..Math.Min(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups[2].Value.Length)] : null;
        var all = AdifReader.Read(text);
        var count = NumRecField().Match(text[..eoh]);
        if (count.Success && int.TryParse(count.Groups[2].Value[..Math.Min(int.Parse(count.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), count.Groups[2].Value.Length)].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var expected)
            && all.Count < expected && text.IndexOf("<APP_LoTW_EOF>", eoh, StringComparison.OrdinalIgnoreCase) < 0)
        {
            // Cut off part way (fewer records than the header promised and no end marker): keep the old qso_qslsince so
            // the missing confirmations come next time rather than being skipped for good.
            return new LotwReport([], null, $"LoTW sent {all.Count} of {expected} confirmations; the rest come with the next download");
        }
        var records = all.Where(r => string.Equals(r["qsl_rcvd"], "Y", StringComparison.OrdinalIgnoreCase)).ToList();
        return new LotwReport(records, last?.Trim(), null);
    }

    [GeneratedRegex(@"<APP_LoTW_LASTQSL:(\d+)>([^<]*)", RegexOptions.IgnoreCase)]
    private static partial Regex LastQslField();

    [GeneratedRegex(@"<APP_LoTW_NUMREC:(\d+)>([^<]*)", RegexOptions.IgnoreCase)]
    private static partial Regex NumRecField();
}
