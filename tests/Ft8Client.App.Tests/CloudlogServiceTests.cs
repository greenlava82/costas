// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.Cloudlog;
using Ft8Client.Integrations.Net;

namespace Ft8Client.App.Tests;

/// <summary>Settings' Test button with a Wavelog v2 token: what the token may do, then its station locations.</summary>
public class CloudlogServiceTests
{
    private static readonly Uri Site = new("https://log.example.com");

    [Fact]
    public async Task TestV2_TokenWithoutStationRead_SaysToTypeTheIdAndListsNothing()
    {
        var (message, stations) = await Test("""{"data":{"name":"costas","scopes":["qso:write"],"expires_at":null}}""", stationsJson: null, upload: true);

        message.Should().Contain("Token \"costas\" can upload contacts").And.Contain("no station:read").And.Contain("type the station id");
        stations.Should().BeEmpty();
    }

    [Fact]
    public async Task TestV2_ReadOnlyTokenWithUploadOn_WarnsAboutQsoWrite()
    {
        var (message, stations) = await Test("""{"data":{"name":"ro","scopes":["qso:read","station:read"],"expires_at":"2027-01-31 00:00:00"}}""",
            """{"data":[{"id":4,"name":"Home","callsign":"K1GLV","active":true}]}""", upload: true);

        message.Should().Contain("can read contacts but not upload them, until 2027-01-31").And.Contain("Uploads need the qso:write scope");
        stations.Should().ContainSingle().Which.Id.Should().Be("4");
    }

    [Fact]
    public async Task TestV2_WriteOnlyTokenWithDownloadOn_WarnsAboutQsoRead()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath == "/index.php/api/v2/token"
            ? Answer(HttpStatusCode.OK, """{"data":{"name":"up","scopes":["qso:write"],"expires_at":null}}""")
            : Answer(HttpStatusCode.Forbidden, """{"error":{"code":"insufficient_scope"}}"""));
        using var http = HttpFactory.Create("K1GLV", handler);

        var (message, _) = await CloudlogService.TestV2Async(new CloudlogClient(http), Site, "wl2_t", upload: true, TestContext.Current.CancellationToken, download: true);

        message.Should().Contain("Downloading needs the qso:read scope").And.NotContain("Uploads need");
    }

    [Fact]
    public void ApplyProfiles_DownloadSwitch_KeptPerProfile()
    {
        var s = new AppSettings();
        s.Profile.Callsign = "K1GLV";
        s.Profiles.Add(new StationProfile { Id = "club", Name = "Club", Callsign = "W1AW" });
        var drafts = s.Profiles.Select(p => new ProfileDraft
        {
            Id = p.Id, Name = p.Name, Callsign = p.Callsign, Grid = "FN42", Power = "100", CloudlogDownload = p.Id == "club",
        }).ToList();

        SettingsViewModel.ApplyProfiles(s, drafts, s.Profile.Id);

        s.Profiles.Single(p => p.Id == "club").Cloudlog.DownloadForWorked.Should().BeTrue();
        s.Profile.Cloudlog.DownloadForWorked.Should().BeFalse();
    }

    [Fact]
    public async Task TestV2_RefusedToken_SaysSoAndAsksNoMore()
    {
        var (message, stations) = await Test(null, null, upload: false);

        message.Should().Contain("did not accept that token");
        stations.Should().BeEmpty();
    }

    // tokenJson null answers 401 to the token call; stationsJson null answers 403 to the station call.
    private static async Task<(string Message, IReadOnlyList<CloudlogStation> Stations)> Test(string? tokenJson, string? stationsJson, bool upload)
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/index.php/api/v2/token" => tokenJson is null ? Answer(HttpStatusCode.Unauthorized, """{"error":{"code":"invalid_token"}}""") : Answer(HttpStatusCode.OK, tokenJson),
            "/index.php/api/v2/station" => stationsJson is null ? Answer(HttpStatusCode.Forbidden, """{"error":{"code":"insufficient_scope"}}""") : Answer(HttpStatusCode.OK, stationsJson),
            _ => Answer(HttpStatusCode.NotFound, ""),
        });
        using var http = HttpFactory.Create("K1GLV", handler);
        var result = await CloudlogService.TestV2Async(new CloudlogClient(http), Site, "wl2_t", upload, TestContext.Current.CancellationToken);
        if (tokenJson is null || stationsJson is null && !tokenJson.Contains("station:read", StringComparison.Ordinal))
            handler.Paths.Should().NotContain("/index.php/api/v2/station", "Test does not ask for what the token may not see");
        return result;
    }

    private static HttpResponseMessage Answer(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body) };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(answer(request));
        }
    }
}
