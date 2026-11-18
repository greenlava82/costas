// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using System.Net;
using System.Xml.Linq;
using Ft8Client.Core;

namespace Ft8Client.Integrations.Qrz;

/// <summary>
/// QRZ XML data client (https://www.qrz.com/XML/current_spec.html). Logs in once, caches the session key, and logs in
/// again once when a lookup answers with an error other than "not found".
/// </summary>
public sealed class QrzXmlClient(HttpClient http, Func<(string User, string Password)?> credentials, Uri? endpoint = null)
{
    /// <summary>The production endpoint.</summary>
    public static readonly Uri DefaultEndpoint = new("https://xmldata.qrz.com/xml/current/");

    private readonly Uri _endpoint = endpoint ?? DefaultEndpoint;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _sessionKey;

    /// <summary>Logs in and returns true when the credentials work.</summary>
    public async Task<bool> TestAsync(CancellationToken ct)
    {
        _sessionKey = null;
        try
        {
            await LoginAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (QrzException ex) when (ex.IsAuth)
        {
            return false;
        }
    }

    /// <summary>
    /// Looks up a call. Returns null only when QRZ says the call is not found. Any other QRZ error (a lookup limit, a
    /// lapsed subscription, a session that will not renew) throws <see cref="QrzException"/>, so it is never taken for a
    /// miss; a refused login throws with <see cref="QrzException.IsAuth"/> set.
    /// </summary>
    public async Task<QrzLookupResult?> LookupAsync(string call, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string? lastError = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var key = _sessionKey ?? await LoginAsync(ct).ConfigureAwait(false);
                var doc = await GetAsync($"s={Uri.EscapeDataString(key)};callsign={Uri.EscapeDataString(call)}", ct).ConfigureAwait(false);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                var session = doc.Root?.Element(ns + "Session");
                var error = session?.Element(ns + "Error")?.Value;
                var cs = doc.Root?.Element(ns + "Callsign");
                if (cs is not null) return Map(cs, ns);
                if (error is not null && error.Contains("not found", StringComparison.OrdinalIgnoreCase)) return null;
                // A session error (expired key) or anything else: log in again once, then give up without calling it a miss.
                lastError = error;
                _sessionKey = null;
            }
            throw new QrzException(lastError is null ? "QRZ lookup returned no record and no reason." : $"QRZ lookup failed: {lastError.Trim()}");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> LoginAsync(CancellationToken ct)
    {
        var c = credentials() ?? throw new QrzException("QRZ username and password are not set.", isAuth: true);
        var agent = AppInfo.Id + AppInfo.Version;
        var doc = await GetAsync($"username={Uri.EscapeDataString(c.User)};password={Uri.EscapeDataString(c.Password)};agent={agent}", ct).ConfigureAwait(false);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var session = doc.Root?.Element(ns + "Session");
        _sessionKey = session?.Element(ns + "Key")?.Value is { Length: > 0 } k ? k : null;
        if (_sessionKey is not null) return _sessionKey;
        var error = session?.Element(ns + "Error")?.Value?.Trim();
        throw new QrzException(string.IsNullOrEmpty(error) ? "QRZ refused the lookup login." : $"QRZ refused the lookup login: {error}", isAuth: true);
    }

    private async Task<XDocument> GetAsync(string query, CancellationToken ct)
    {
        try
        {
            var uri = new UriBuilder(_endpoint) { Query = query }.Uri;
            using var resp = await http.GetAsync(uri, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new QrzException($"QRZ lookup answered HTTP {(int)resp.StatusCode}.");
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return XDocument.Parse(body);
        }
        catch (HttpRequestException ex)
        {
            throw new QrzException($"QRZ lookup is unreachable: {ex.Message}", inner: ex);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new QrzException("QRZ lookup returned unreadable data.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new QrzException("QRZ lookup did not answer in time.", inner: ex);
        }
    }

    private static QrzLookupResult Map(XElement cs, XNamespace ns)
    {
        string? V(string n) => cs.Element(ns + n)?.Value is { Length: > 0 } v ? WebUtility.HtmlDecode(v).Trim() is { Length: > 0 } t ? t : null : null;
        double? D(string n) => double.TryParse(V(n), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        int? I(string n) => int.TryParse(V(n), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
        // The spec documents 0/1 for these flags but its own sample uses Y/N; accept both.
        bool? B(string n) => V(n)?.ToUpperInvariant() switch
        {
            "1" or "Y" => true,
            "0" or "N" => false,
            _ => null,
        };
        var name = string.Join(' ', new[] { V("fname"), V("name") }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new QrzLookupResult
        {
            Call = V("call") ?? string.Empty,
            Name = name.Length == 0 ? null : name,
            City = V("addr2"),
            State = V("state"),
            Country = V("country"),
            Grid = V("grid"),
            Lat = D("lat"),
            Lon = D("lon"),
            Dxcc = I("dxcc"),
            Nickname = V("nickname"),
            County = V("county"),
            LicenseClass = V("class"),
            TimeZone = V("TimeZone"),
            GmtOffset = D("GMTOffset"),
            ObservesDst = B("DST"),
            ImageUrl = V("image") is { } img && img.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? img : null,
            QslManager = V("qslmgr"),
            Lotw = B("lotw"),
            Eqsl = B("eqsl"),
            PaperQsl = B("mqsl"),
            CqZone = I("cqzone"),
            ItuZone = I("ituzone"),
        };
    }
}
