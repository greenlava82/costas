// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using System.Text.Json;
using Ft8Client.Core.Geo;
using Ft8Client.Core.Time;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Data.Reference;
using Ft8Client.Integrations.Cloudlog;
using Ft8Client.Integrations.Net;

namespace Ft8Client.Integrations.Tests;

public sealed class WavelogSyncServiceTests : IDisposable
{
    private static readonly CountryFile Countries = ReferenceData.LoadCountries();
    private static readonly DateTime Now = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);
    private static readonly Uri Site = new("https://log.example.com");

    private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAlive;
    private readonly QsoRepository _qsos;
    private readonly UploadQueueRepository _queue;
    private readonly SyncStateRepository _state;
    private readonly ManualClock _clock = new(Now);
    private readonly List<string> _asked = [];
    private readonly Queue<string> _pages = new();

    public WavelogSyncServiceTests()
    {
        var db = SqliteDatabase.InMemory(out _keepAlive);
        _qsos = new QsoRepository(db);
        _queue = new UploadQueueRepository(db);
        _state = new SyncStateRepository(db);
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task Sync_FirstTwoPagesThenNothingNew_InsertsFollowsSinceIdAndKeepsTheCursor()
    {
        _pages.Enqueue(Page(1, 41, true, Record("K1ABC", "20261101", "1200")));
        _pages.Enqueue(Page(1, 57, false, Record("JA1QRS", "20261102", "0300")));

        var r = await Sync();

        (r.Fetched, r.Inserted, r.Merged, r.Full, r.Error).Should().Be((2, 2, 0, true, null));
        _asked.Should().HaveCount(2);
        _asked[0].Should().Contain("since_id=0").And.Contain("station_id=3").And.Contain("format=adif").And.Contain("per_page=1000");
        _asked[1].Should().Contain("since_id=41");
        _qsos.List().Should().OnlyContain(q => q.Source == QsoSource.Wavelog && q.StationCallsign == "W7LIT" && q.ProfileId == "home");
        _queue.All().Should().BeEmpty("a downloaded contact is never sent back to Wavelog");
        _qsos.List().Should().OnlyContain(q => q.WavelogUploadState == UploadState.Uploaded, "Wavelog has them");

        _pages.Enqueue("""{"data":{"exported":0,"lastfetchedid":57,"adif":null},"meta":{"has_more":false}}""");
        var again = await Sync();
        (again.Fetched, again.Full).Should().Be((0, false));
        _asked[2].Should().Contain("since_id=57");
    }

    [Fact]
    public async Task Sync_ContactWaitingToUpload_MergesAndLeavesTheUploadQueue()
    {
        var id = _qsos.Insert(new QsoRecord
        {
            ProfileId = "home", StationCallsign = "W7LIT", Call = "K1ABC", Band = "20m", Mode = "FT8", Source = QsoSource.Local,
            QsoDateOn = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
        });
        _queue.Enqueue(id, CloudlogUploadService.Target, Now);
        _queue.Enqueue(id, "clublog", Now);
        _pages.Enqueue(Page(1, 9, false, Record("K1ABC", "20261101", "1200", gridsquare: "FN42")));

        var r = await Sync();

        (r.Inserted, r.Merged).Should().Be((0, 1));
        _qsos.Get(id)!.Gridsquare.Should().Be("FN42", "missing fields are taken from Wavelog");
        _queue.All().Select(i => i.Target).Should().Equal("clublog");
        _qsos.Get(id)!.WavelogUploadState.Should().Be(UploadState.Uploaded, "Wavelog already has the contact");
    }

    [Fact]
    public async Task Sync_DeletedAndEditedContacts_StayAsTheOperatorLeftThem()
    {
        var deleted = _qsos.Insert(new QsoRecord { ProfileId = "home", StationCallsign = "W7LIT", Call = "VK3ABC", Band = "20m", Mode = "FT8", QsoDateOn = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc) });
        _qsos.Delete(deleted);
        var edited = _qsos.Insert(new QsoRecord { ProfileId = "home", StationCallsign = "W7LIT", Call = "K1ABC", Band = "20m", Mode = "FT8", QsoDateOn = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc) });
        var row = _qsos.Get(edited)!;
        row.Name = "Johnny";
        _qsos.SaveEdit(row);
        _pages.Enqueue(Page(2, 12, false,
            Record("VK3ABC", "20261103", "0900"),
            Record("K1ABC", "20261101", "1200", name: "John", lotw: true)));

        await Sync();

        _qsos.List().Should().ContainSingle("the deleted contact is not brought back");
        var kept = _qsos.Get(edited)!;
        kept.Name.Should().Be("Johnny");
        kept.LotwConfirmed.Should().BeTrue("a confirmation is taken even into an edited contact");
    }

    [Fact]
    public async Task Sync_OtherStationLocation_StartsFromTheBeginning()
    {
        _pages.Enqueue(Page(1, 41, false, Record("K1ABC", "20261101", "1200")));
        await Sync();
        _pages.Enqueue(Page(0, 0, false));

        var r = await Sync(stationId: "4");

        r.Full.Should().BeTrue();
        _asked[1].Should().Contain("station_id=4").And.Contain("since_id=0");
    }

    [Fact]
    public async Task Sync_TokenWithoutRead_StopsPermanentlyNamingTheScope()
    {
        _pages.Enqueue("""{"error":{"code":"insufficient_scope","message":"Missing scope","details":{"required_scope":"qso:read"}}}""");

        var r = await Sync(code: HttpStatusCode.Forbidden);

        r.Permanent.Should().BeTrue();
        r.Error.Should().Contain("qso:read");
        _qsos.List().Should().BeEmpty();
    }

    private async Task<WavelogSyncResult> Sync(string stationId = "3", HttpStatusCode code = HttpStatusCode.OK)
    {
        using var handler = new Handler((request, _) =>
        {
            request.Headers.Authorization!.Parameter.Should().Be("wl2_t");
            _asked.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(_pages.Dequeue()) });
        });
        using var http = HttpFactory.Create("W7LIT", handler);
        var sync = new WavelogSyncService(new CloudlogClient(http), _qsos, _queue, _state, Countries, _clock, (_, _) => Task.CompletedTask);
        return await sync.SyncAsync("home", "W7LIT", Site, "wl2_t", stationId, TestContext.Current.CancellationToken);
    }

    private static string Page(int exported, long last, bool more, params string[] records) =>
        JsonSerializer.Serialize(new
        {
            data = new { exported, lastfetchedid = last, adif = records.Length == 0 ? null : "<adif_ver:5>3.1.4<eoh>" + string.Concat(records) },
            meta = new { has_more = more },
        });

    private static string Record(string call, string date, string time, string? gridsquare = null, string? name = null, bool lotw = false)
    {
        static string F(string tag, string? v) => v is null ? "" : $"<{tag}:{v.Length}>{v}";
        return F("call", call) + F("qso_date", date) + F("time_on", time) + F("band", "20m") + F("mode", "FT8") + F("station_callsign", "W7LIT")
            + F("gridsquare", gridsquare) + F("name", name) + (lotw ? F("lotw_qsl_rcvd", "Y") : "") + "<eor>";
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
