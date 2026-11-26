// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using System.Text;

namespace Ft8Client.Integrations.Fakes;

/// <summary>
/// An in-process stand-in for wsprnet.org's <c>/post/</c>, for tests: accepts a spot with <c>1 spot(s) added</c>, or
/// rejects it, fails with a server error, or never answers, on command. It is an <see cref="HttpMessageHandler"/>, so there
/// is no listener to stop.
/// </summary>
public sealed class FakeWsprnetServer : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<Dictionary<string, string>> _requests = [];

    /// <summary>Fail this many next requests with HTTP 503.</summary>
    public int FailNext { get; set; }

    /// <summary>When set, every request is answered with this text and no success (a refused spot).</summary>
    public string? RejectReason { get; set; }

    /// <summary>When true, a request never answers until its token is cancelled (the client's timeout).</summary>
    public bool Hang { get; set; }

    /// <summary>The forms posted, in order.</summary>
    public IReadOnlyList<Dictionary<string, string>> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    /// <summary>The address of each request.</summary>
    public List<Uri> Uris { get; } = [];

    /// <summary>The <c>User-Agent</c> of each request.</summary>
    public List<string> UserAgents { get; } = [];

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => WebUtility.UrlDecode(p[0]), p => p.Length > 1 ? WebUtility.UrlDecode(p[1]) : string.Empty, StringComparer.Ordinal);
        lock (_gate)
        {
            Uris.Add(request.RequestUri!);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            if (FailNext > 0)
            {
                FailNext--;
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            if (RejectReason is { } why) return Reply(why);
            _requests.Add(form);
        }
        return Reply("1 spot(s) added");
    }

    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/html") };
}
