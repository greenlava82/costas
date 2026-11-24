// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using System.Text.Json;
using Ft8Client.Core.Time;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Integrations.Cloudlog;
using Ft8Client.Integrations.Net;

namespace Ft8Client.Integrations.Tests;

public class CloudlogTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc);
    private static readonly Uri Site = new("https://log.example.com");

    [Theory]
    [InlineData("log.example.com", "https://log.example.com/")]
    [InlineData("https://log.example.com/", "https://log.example.com/")]
    [InlineData("https://example.com/wavelog/index.php", "https://example.com/wavelog")]
    [InlineData("https://example.com/wavelog/index.php/api/qso", "https://example.com/wavelog")]
    [InlineData("http://192.168.1.20:8086", "http://192.168.1.20:8086/")]
    [InlineData("", null)]
    [InlineData("ftp://example.com", null)]
    public void SiteUrl_WhatTheOperatorTyped_BaseAddress(string typed, string? expected)
    {
        CloudlogClient.SiteUrl(typed)?.AbsoluteUri.Should().Be(expected);
        if (expected is null) CloudlogClient.SiteUrl(typed).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, """{"status":"created","type":"adif","adif_count":1,"adif_errors":0,"messages":[""]}""", true, false)]
    [InlineData(HttpStatusCode.BadRequest, """{"status":"abort","adif_count":1,"adif_errors":1,"messages":["","Date/Time: 2026-11-07 Callsign: K1ABC Band: 20m Duplicate for W7LIT<br>"]}""", true, false)]
    [InlineData(HttpStatusCode.Unauthorized, """{"status":"failed","reason":"missing or wrong api key"}""", false, true)]
    [InlineData(HttpStatusCode.Unauthorized, """{"status":"failed","reason":"station id does not belong to the API key owner."}""", false, true)]
    [InlineData(HttpStatusCode.NotFound, "<html>Not found</html>", false, true)]
    [InlineData(HttpStatusCode.TooManyRequests, "", false, false)]
    [InlineData(HttpStatusCode.OK, "<html>Sign in</html>", false, false)]
    public void ParseResponse_CreatedOrDuplicateAccepted_KeyErrorsPermanent(HttpStatusCode code, string body, bool accepted, bool permanent)
    {
        var r = CloudlogClient.ParseResponse(code, body);
        (r.Accepted, r.Permanent).Should().Be((accepted, permanent));
        if (!accepted) r.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ParseResponse_ReasonQuotingTheKey_KeyRemoved()
    {
        var r = CloudlogClient.ParseResponse(HttpStatusCode.BadRequest, """{"status":"failed","reason":"bad key cl123secret"}""", "cl123secret");
        r.Error.Should().NotContain("cl123secret");
    }

    [Fact]
    public void ParseStations_StationInfo_ReadsLocations()
    {
        var list = CloudlogClient.ParseStations("""
            [{"station_id":"1","station_profile_name":"Home","station_gridsquare":"DN40","station_callsign":"w7lit","station_active":"1"},
             {"station_id":2,"station_profile_name":"Portable","station_callsign":"W7LIT/P","station_active":null}]
            """)!;
        list.Should().Equal(new CloudlogStation("1", "Home", "W7LIT", true), new CloudlogStation("2", "Portable", "W7LIT/P", false));
        CloudlogClient.ParseStations("{}").Should().BeNull();
    }

    [Fact]
    public async Task Upload_JsonBodyToQsoApi_WithAdifAndUserAgent()
    {
        Dictionary<string, string>? sent = null;
        using var handler = new Handler(async (request, ct) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be(new Uri("https://log.example.com/index.php/api/qso"));
            request.Headers.UserAgent.ToString().Should().StartWith("Costas/");
            sent = JsonSerializer.Deserialize<Dictionary<string, string>>(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"status":"created"}""") };
        });
        using var http = HttpFactory.Create("W7LIT", handler);
        var record = new Ft8Client.Data.Adif.AdifRecord { ["call"] = "K1ABC", ["band"] = "20m", ["mode"] = "FT8" };
        (await new CloudlogClient(http).UploadAsync(Site, "key1", "3", record, TestContext.Current.CancellationToken)).Accepted.Should().BeTrue();
        sent!["key"].Should().Be("key1");
        sent["station_profile_id"].Should().Be("3");
        sent["type"].Should().Be("adif");
        sent["string"].Should().Contain("<call:5>K1ABC").And.Contain("<eor>");
    }

    [Fact]
    public async Task Upload_NonAsciiName_JsonCarriesUtf8AndByteLength()
    {
        string? adif = null;
        using var handler = new Handler(async (request, ct) =>
        {
            adif = JsonSerializer.Deserialize<Dictionary<string, string>>(await request.Content!.ReadAsStringAsync(ct))!["string"];
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"status":"created"}""") };
        });
        using var http = HttpFactory.Create("W7LIT", handler);
        var record = new Ft8Client.Data.Adif.AdifRecord { ["call"] = "DL1ABC", ["name"] = "Jürgen Weiß", ["mode"] = "FT8" };

        (await new CloudlogClient(http).UploadAsync(Site, "key1", "3", record, TestContext.Current.CancellationToken)).Accepted.Should().BeTrue();

        adif.Should().Contain("<name:13>Jürgen Weiß");
    }

    [Fact]
    public async Task Queue_KeyRefusedThenFixed_WaitsAnHourThenUploads()
    {
        var db = SqliteDatabase.InMemory(out var keepAlive);
        using var connection = keepAlive;
        var qsos = new QsoRepository(db);
        var queue = new UploadQueueRepository(db);
        var clock = new ManualClock(Now);
        var id = qsos.Insert(new QsoRecord { ProfileId = "home", StationCallsign = "W7LIT", Call = "K1ABC", Band = "20m", Mode = "FT8", QsoDateOn = Now });
        var other = qsos.Insert(new QsoRecord { ProfileId = "portable", StationCallsign = "W7LIT/P", Call = "K2ABC", Band = "20m", Mode = "FT8", QsoDateOn = Now });
        queue.Enqueue(id, "qrz", Now);
        queue.Enqueue(id, CloudlogUploadService.Target, Now);
        queue.Enqueue(other, CloudlogUploadService.Target, Now);
        var (code, reply) = (HttpStatusCode.Unauthorized, """{"status":"failed","reason":"missing or wrong api key"}""");
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(reply) }));
        using var http = HttpFactory.Create("W7LIT", handler);
        var worker = new CloudlogUploadService(new CloudlogClient(http), qsos, queue, clock, (wait, _) => { clock.Advance(wait); return Task.CompletedTask; });

        (await worker.ProcessDueAsync("home", "W7LIT", Site, "bad", "1", TestContext.Current.CancellationToken)).Should().Be(0);
        var item = queue.All().Single(i => i.QsoId == id && i.Target == CloudlogUploadService.Target);
        item.Attempts.Should().Be(1);
        item.NextTryUtc.Should().Be(clock.UtcNow + CloudlogUploadService.PermanentRetry);
        worker.Status.Message.Should().ContainEquivalentOf("api key");
        (qsos.Get(id)!.WavelogUploadState, qsos.Get(id)!.WavelogUploadError).Should().Be((UploadState.Queued, "The logbook refused the upload: missing or wrong api key."));

        clock.Advance(TimeSpan.FromHours(1));
        (code, reply) = (HttpStatusCode.Created, """{"status":"created"}""");
        (await worker.ProcessDueAsync("home", "W7LIT", Site, "good", "1", TestContext.Current.CancellationToken)).Should().Be(1);
        queue.All().Should().HaveCount(2, "the QRZ item and the other profile's contact stay");
        (qsos.Get(id)!.WavelogUploadState, qsos.Get(id)!.WavelogUploadError).Should().Be((UploadState.Uploaded, null));
        queue.All().Should().Contain(i => i.QsoId == other && i.Target == CloudlogUploadService.Target);
    }

    [Theory]
    [InlineData("wl2_abc123", WavelogApi.V2)]
    [InlineData("  wl2_abc123 ", WavelogApi.V2)]
    [InlineData("cl5f2a9", WavelogApi.V1)]
    [InlineData("WL2_abc", WavelogApi.V1)]
    [InlineData("", WavelogApi.V1)]
    [InlineData(null, WavelogApi.V1)]
    public void ApiFor_Key_PicksTheApi(string? key, WavelogApi api) => CloudlogClient.ApiFor(key).Should().Be(api);

    [Theory]
    [InlineData("https://example.com/wavelog/index.php/api/v2/qso", "https://example.com/wavelog")]
    [InlineData("https://log.example.com/api/v2/station", "https://log.example.com/")]
    public void SiteUrl_PastedV2Address_BaseAddress(string typed, string expected) =>
        CloudlogClient.SiteUrl(typed)!.AbsoluteUri.Should().Be(expected);

    [Fact]
    public async Task Upload_V2Token_PostsAdifImportWithBearer()
    {
        JsonElement? sent = null;
        using var handler = new Handler(async (request, ct) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be(new Uri("https://log.example.com/index.php/api/v2/qso"));
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            request.Headers.Authorization.Parameter.Should().Be("wl2_secret");
            request.Headers.UserAgent.ToString().Should().StartWith("Costas/");
            sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"data":{"parsed":1,"imported":1,"skipped":0,"messages":[]},"meta":{}}"""),
            };
        });
        using var http = HttpFactory.Create("W7LIT", handler);
        var record = new Ft8Client.Data.Adif.AdifRecord { ["call"] = "K1ABC", ["band"] = "20m", ["mode"] = "FT8" };

        (await new CloudlogClient(http).UploadAsync(Site, " wl2_secret", "3", record, TestContext.Current.CancellationToken)).Accepted.Should().BeTrue();

        var body = sent!.Value;
        body.GetProperty("import_type").GetString().Should().Be("adif");
        body.GetProperty("station_profile_id").GetInt32().Should().Be(3);
        body.GetProperty("adif").GetString().Should().Contain("<call:5>K1ABC").And.Contain("<eor>");
        body.GetRawText().Should().NotContain("wl2_secret", "a v2 token travels only in the header");
    }

    [Fact]
    public async Task Upload_V2TokenStationIdNotANumber_RefusedWithoutSending()
    {
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)); });
        using var http = HttpFactory.Create("W7LIT", handler);
        var r = await new CloudlogClient(http).UploadAsync(Site, "wl2_x", "home", new Ft8Client.Data.Adif.AdifRecord { ["call"] = "K1ABC" }, TestContext.Current.CancellationToken);
        (r.Accepted, r.Permanent).Should().Be((false, true));
        r.Error.Should().Contain("must be a number");
        calls.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, """{"data":{"parsed":1,"imported":1,"skipped":0,"messages":[]}}""", true, false, null)]
    [InlineData(HttpStatusCode.Created, """{"data":{"parsed":1,"imported":0,"skipped":1,"messages":["Duplicate for K1ABC"]}}""", true, false, null)]
    [InlineData(HttpStatusCode.Conflict, """{"error":{"code":"conflict","message":"Duplicate"}}""", true, false, null)]
    [InlineData(HttpStatusCode.Created, """{"data":{"parsed":0,"imported":0,"skipped":0,"messages":["No QSO found"]}}""", false, false, "No QSO found")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"code":"invalid_token","message":"Unknown token"}}""", false, true, "unknown or revoked")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"code":"token_expired","message":"Expired"}}""", false, true, "expired")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":"insufficient_scope","message":"Missing scope","details":{"required_scope":"qso:write"}}}""", false, true, "qso:write")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":"forbidden","message":"Station not owned"}}""", false, true, "Station not owned")]
    [InlineData(HttpStatusCode.NotFound, "<html>Not found</html>", false, true, "Wavelog 3.1")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"code":"rate_limited","message":"Slow down","details":{"retry_after":90}}}""", false, false, "slow down")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":"validation_error","message":"band is invalid"}}""", false, false, "band is invalid")]
    [InlineData(HttpStatusCode.OK, "<html>Sign in</html>", false, false, "check the address")]
    public void ParseV2Upload_Answer_AcceptedPermanentOrRetry(HttpStatusCode code, string body, bool accepted, bool permanent, string? says)
    {
        var r = CloudlogClient.ParseV2Upload(code, body, "wl2_secret");
        (r.Accepted, r.Permanent).Should().Be((accepted, permanent));
        if (says is not null) r.Error.Should().ContainEquivalentOf(says);
    }

    [Fact]
    public void ParseV2Upload_RateLimited_CarriesTheWait()
    {
        var r = CloudlogClient.ParseV2Upload(HttpStatusCode.TooManyRequests, """{"error":{"code":"rate_limited","details":{"retry_after":90}}}""");
        r.RetryAfter.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void ParseV2Upload_MessageQuotingTheToken_TokenRemoved() =>
        CloudlogClient.ParseV2Upload(HttpStatusCode.BadRequest, """{"error":{"code":"validation_error","message":"bad wl2_secret"}}""", "wl2_secret")
            .Error.Should().NotContain("wl2_secret");

    [Fact]
    public void ParseV2Token_DocumentedExample_ReadsScopesAndExpiry()
    {
        var t = CloudlogClient.ParseV2Token("""
            {"data":{"id":15,"name":"my logger","owner":"N0CALL","user_id":1,"scopes":["qso:read","qso:write","lookup:read"],"expires_at":"2026-08-16 12:05:50"}}
            """)!;
        t.Name.Should().Be("my logger");
        (t.CanRead, t.CanWrite, t.CanListStations).Should().Be((true, true, false));
        t.ExpiresAt.Should().Be(new DateTime(2026, 8, 16, 12, 5, 50));
        CloudlogClient.ParseV2Token("""{"data":{"name":"forever","scopes":["qso:read"],"expires_at":null}}""")!.ExpiresAt.Should().BeNull();
        CloudlogClient.ParseV2Token("""{"error":{"code":"invalid_token"}}""").Should().BeNull();
    }

    [Fact]
    public void ParseV2Stations_DocumentedFields_ReadsLocations()
    {
        var list = CloudlogClient.ParseV2Stations("""
            {"data":[{"id":1,"name":"Home","callsign":"w7lit","gridsquare":"DN40","active":true},
                     {"id":2,"name":"Portable","callsign":"W7LIT/P","active":false}],"meta":{}}
            """)!;
        list.Should().Equal(new CloudlogStation("1", "Home", "W7LIT", true), new CloudlogStation("2", "Portable", "W7LIT/P", false));
        CloudlogClient.ParseV2Stations("""[{"station_id":"1"}]""").Should().BeNull();
    }

    [Fact]
    public async Task Stations_V2Token_AsksTheV2StationResource()
    {
        Uri? asked = null;
        using var handler = new Handler((request, _) =>
        {
            asked = request.RequestUri;
            request.Headers.Authorization!.Parameter.Should().Be("wl2_t");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":[{"id":7,"name":"Club","callsign":"W7ABC","active":true}]}""") });
        });
        using var http = HttpFactory.Create("W7LIT", handler);
        var list = await new CloudlogClient(http).StationsAsync(Site, "wl2_t", TestContext.Current.CancellationToken);
        asked.Should().Be(new Uri("https://log.example.com/index.php/api/v2/station"));
        list!.Single().Id.Should().Be("7");
    }

    [Fact]
    public async Task Token_V1Key_AsksNothing()
    {
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); });
        using var http = HttpFactory.Create("W7LIT", handler);
        (await new CloudlogClient(http).TokenAsync(Site, "cl123", TestContext.Current.CancellationToken)).Should().BeNull();
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Queue_V2RateLimited_WaitsAsLongAsAsked()
    {
        var db = SqliteDatabase.InMemory(out var keepAlive);
        using var connection = keepAlive;
        var qsos = new QsoRepository(db);
        var queue = new UploadQueueRepository(db);
        var clock = new ManualClock(Now);
        var id = qsos.Insert(new QsoRecord { ProfileId = "home", StationCallsign = "W7LIT", Call = "K1ABC", Band = "20m", Mode = "FT8", QsoDateOn = Now });
        queue.Enqueue(id, CloudlogUploadService.Target, Now);
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"error":{"code":"rate_limited","message":"Too many","details":{"retry_after":600}}}"""),
        }));
        using var http = HttpFactory.Create("W7LIT", handler);
        var worker = new CloudlogUploadService(new CloudlogClient(http), qsos, queue, clock, (wait, _) => { clock.Advance(wait); return Task.CompletedTask; });

        (await worker.ProcessDueAsync("home", "W7LIT", Site, "wl2_t", "1", TestContext.Current.CancellationToken)).Should().Be(0);

        queue.All().Single().NextTryUtc.Should().Be(clock.UtcNow + TimeSpan.FromSeconds(600));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"data":{"exported":2,"lastfetchedid":88,"adif":"<call:5>K1ABC<eor>"},"meta":{"has_more":true}}""", 2, 88L, true, false, null)]
    [InlineData(HttpStatusCode.OK, """{"data":{"exported":0,"lastfetchedid":88,"adif":null},"meta":{"has_more":false}}""", 0, 88L, false, false, null)]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":"insufficient_scope","details":{"required_scope":"qso:read"}}}""", -1, 0L, false, true, "qso:read")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"code":"token_expired"}}""", -1, 0L, false, true, "expired")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"code":"rate_limited","details":{"retry_after":30}}}""", -1, 0L, false, false, "slow down")]
    [InlineData(HttpStatusCode.OK, "<html>Sign in</html>", -1, 0L, false, false, "check the address")]
    public void ParseV2Download_Answer_PageOrRefusal(HttpStatusCode code, string body, int exported, long last, bool more, bool permanent, string? says)
    {
        var r = CloudlogClient.ParseV2Download(code, body, "wl2_t");
        if (exported < 0)
        {
            r.Page.Should().BeNull();
            r.Permanent.Should().Be(permanent);
            r.Error.Should().ContainEquivalentOf(says);
            return;
        }
        r.Page.Should().Be(new WavelogDownloadPage(exported, last, exported == 0 ? null : "<call:5>K1ABC<eor>", more));
    }

    [Fact]
    public async Task DownloadPage_V1Key_RefusedWithoutAsking()
    {
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); });
        using var http = HttpFactory.Create("W7LIT", handler);
        var r = await new CloudlogClient(http).DownloadPageAsync(Site, "cl123", "3", 0, TestContext.Current.CancellationToken);
        (r.Page, r.Permanent).Should().Be((null, true));
        r.Error.Should().Contain("v2 token");
        calls.Should().Be(0);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
