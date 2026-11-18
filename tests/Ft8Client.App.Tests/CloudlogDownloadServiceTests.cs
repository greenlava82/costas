// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.Audio.Backends;
using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Data.Reference;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

/// <summary>The background worker's Wavelog download: when it runs, what it tells the indicator, and the log index.</summary>
public sealed class CloudlogDownloadServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);

    private readonly ManualClock _clock = new(Now);
    private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAlive;
    private readonly SqliteDatabase _db;
    private readonly string _dir = Directory.CreateTempSubdirectory("costas-wavelog").FullName;
    private readonly List<string> _asked = [];

    public CloudlogDownloadServiceTests() => _db = SqliteDatabase.InMemory(out _keepAlive);

    public void Dispose()
    {
        _keepAlive.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public async Task RunOnce_DownloadOnWithV2Token_DownloadsThenWaitsFifteenMinutes()
    {
        await using var service = Service("wl2_t", out var qsos);
        var changed = 0;
        service.LogChanged += () => changed++;

        await service.RunOnceAsync(TestContext.Current.CancellationToken);

        qsos.List().Should().ContainSingle(q => q.Call == "K1ABC" && q.Source == QsoSource.Wavelog);
        changed.Should().Be(1, "worked-before is rebuilt");
        service.DownloadStatus!.Health.Should().Be(ServiceHealth.Ok);
        service.DownloadStatus.Message.Should().Be("Downloaded 1 contact from log.example.com");

        await service.RunOnceAsync(TestContext.Current.CancellationToken);
        _asked.Should().HaveCount(1, "the next download waits");

        _clock.Advance(CloudlogService.DownloadEvery);
        await service.RunOnceAsync(TestContext.Current.CancellationToken);
        _asked.Should().HaveCount(2);
        _asked[1].Should().Contain("since_id=5");
    }

    [Fact]
    public async Task RunOnce_DownloadOnWithV1Key_SaysItNeedsAV2TokenAndAsksNothing()
    {
        await using var service = Service("cl5f2a9", out _);

        await service.RunOnceAsync(TestContext.Current.CancellationToken);

        _asked.Should().BeEmpty();
        service.DownloadStatus!.Health.Should().Be(ServiceHealth.Down);
        service.DownloadStatus.Message.Should().Contain("v2 token");
    }

    private CloudlogService Service(string key, out QsoRepository qsos)
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var s = new AppSettings();
        s.Profile.Callsign = "W7LIT";
        s.Profile.Cloudlog.Url = "https://log.example.com";
        s.Profile.Cloudlog.StationId = "3";
        s.Profile.Cloudlog.DownloadForWorked = true;
        var settings = new AppSettingsAccessor(store, s);
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretNames.CloudlogKey(s.Profile.Id), key);
        qsos = new QsoRepository(_db);
        var handler = new Handler(request =>
        {
            _asked.Add(request.RequestUri!.PathAndQuery);
            var body = _asked.Count == 1
                ? """{"data":{"exported":1,"lastfetchedid":5,"adif":"<call:5>K1ABC<qso_date:8>20261101<time_on:4>1200<band:3>20m<mode:3>FT8<station_callsign:5>W7LIT<eor>"},"meta":{"has_more":false}}"""
                : """{"data":{"exported":0,"lastfetchedid":5,"adif":null},"meta":{"has_more":false}}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var freqs = ReferenceData.LoadFrequencies();
        var tx = new Transmitter(new SimulatedRig(_clock), new NullAudioOutput { Now = () => _clock.UtcNow }, _clock, freqs);
        var session = new Session(new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40" }, new NoDecoder(), new SilentSource(), tx, _clock,
            ReferenceData.LoadCountries(), freqs);
        return new CloudlogService(session, settings, secrets, qsos, new UploadQueueRepository(_db), new SyncStateRepository(_db),
            ReferenceData.LoadCountries(), _clock, uploadsAllowed: true, handler);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    private sealed class NoDecoder : IDecoder
    {
        public Task<DecodeResult> DecodeAsync(SlotAudio slot, DecodeContext ctx, CancellationToken ct) =>
            Task.FromResult(new DecodeResult([], TimeSpan.Zero, DecodeStatus.Ok));
    }

    private sealed class SilentSource : ISlotSource
    {
        public void BeginSlot(DateTime slotStartUtc, Mode mode)
        {
        }

        public void CancelSlot()
        {
        }

        public SlotAudio? Cutoff(DateTime slotStartUtc, Mode mode) => null;
    }
}
