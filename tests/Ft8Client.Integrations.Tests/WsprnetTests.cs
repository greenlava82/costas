// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using Ft8Client.Core;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Core.Wspr;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Integrations.Fakes;
using Ft8Client.Integrations.Net;
using Ft8Client.Integrations.Wsprnet;

namespace Ft8Client.Integrations.Tests;

public sealed class WsprnetTests : IDisposable
{
    private static readonly DateTime Slot = new(2026, 11, 26, 14, 2, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAlive;
    private readonly WsprSpotRepository _repo;
    private readonly ManualClock _clock = new(Slot.AddMinutes(1));
    private readonly FakeWsprnetServer _server = new();
    private readonly WsprnetUploader _uploader;

    public WsprnetTests()
    {
        _repo = new WsprSpotRepository(SqliteDatabase.InMemory(out _keepAlive));
        _uploader = Uploader(_repo, _server, TimeSpan.FromSeconds(20));
    }

    public void Dispose() => _keepAlive.Dispose();

    private WsprnetUploader Uploader(WsprSpotRepository repo, HttpMessageHandler handler, TimeSpan timeout) =>
        new(new WsprnetClient(HttpFactory.Create("W7LIT", handler, timeout)), repo, _clock) { Pace = TimeSpan.Zero };

    private WsprSpotRow Add(string call, WsprUploadState state = WsprUploadState.Pending, DateTime? slot = null, string? grid = "FN42", long freq = 14_097_050)
    {
        var row = WsprSpotRow.From(new WsprSpot(slot ?? Slot, -12, 0.3, freq, 1, call, grid, 30, false, 14_095_600), "W7LIT", 5000, state);
        _repo.Add(row);
        return row;
    }

    [Fact]
    public async Task ProcessDueAsync_PendingSpot_PostsEveryFieldAsWsjtxDoes()
    {
        Add("K1ABC");

        var sent = await _uploader.ProcessDueAsync("W7LIT", "DN40ab", "Costas 1.2.3", Ct);

        sent.Should().Be(1);
        var f = _server.Requests.Should().ContainSingle().Subject;
        f.Should().Equal(new Dictionary<string, string>
        {
            ["function"] = "wspr", ["date"] = "261126", ["time"] = "1402", ["sig"] = "-12", ["dt"] = "0.3", ["drift"] = "1",
            ["tqrg"] = "14.097050", ["tcall"] = "K1ABC", ["tgrid"] = "FN42", ["dbm"] = "30", ["version"] = "Costas 1.2.3",
            ["rcall"] = "W7LIT", ["rgrid"] = "DN40ab", ["rqrg"] = "14.097100", ["mode"] = "2",
        });
        _server.Uris.Should().OnlyContain(u => u.ToString() == "https://wsprnet.org/post/");
        _repo.Pending("W7LIT", 10).Should().BeEmpty();
        _uploader.Status.Health.Should().Be(ServiceHealth.Ok);
    }

    [Fact]
    public async Task ProcessDueAsync_Request_CarriesCostasUserAgent()
    {
        Add("K1ABC");
        await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct);

        _server.UserAgents.Should().ContainSingle().Which.Should().StartWith("Costas/").And.EndWith("(W7LIT)");
    }

    [Fact]
    public async Task ProcessDueAsync_Type2Spot_SendsEmptyGrid()
    {
        Add("PJ4/K1ABC", grid: null);
        await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct);

        _server.Requests.Single()["tgrid"].Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDueAsync_SettingOff_SendsNothingAndLeavesRowsAlone()
    {
        Add("K1ABC", WsprUploadState.None);

        (await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct)).Should().Be(0);

        _server.Requests.Should().BeEmpty();
        _server.Uris.Should().BeEmpty();
        _repo.Recent("W7LIT", 10).Should().ContainSingle().Which.UploadState.Should().Be(WsprUploadState.None);
    }

    [Fact]
    public async Task ProcessDueAsync_ServerDown_KeepsQueueAndBacksOffOneTwoFourMinutes()
    {
        Add("K1ABC");
        Add("VK3XYZ", slot: Slot.AddMinutes(2));
        _server.FailNext = 100;

        (await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct)).Should().Be(0);
        _repo.PendingCount("W7LIT").Should().Be(2);
        _uploader.Status.Health.Should().Be(ServiceHealth.Degraded);
        _uploader.NextTryUtc.Should().Be(_clock.UtcNow.AddMinutes(1));

        var calls = _server.Uris.Count;
        await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct);
        _server.Uris.Count.Should().Be(calls, "nothing is tried before the back-off ends");

        _clock.Advance(TimeSpan.FromMinutes(1));
        await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct);
        _uploader.NextTryUtc.Should().Be(_clock.UtcNow.AddMinutes(2));

        _clock.Advance(TimeSpan.FromMinutes(2));
        await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct);
        _uploader.NextTryUtc.Should().Be(_clock.UtcNow.AddMinutes(4));
        _repo.PendingCount("W7LIT").Should().Be(2);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(40, 30)]
    public void Backoff_Failures_DoublesUpToThirtyMinutes(int failures, int minutes) =>
        WsprnetUploader.Backoff(failures).Should().Be(TimeSpan.FromMinutes(minutes));

    [Fact]
    public async Task ProcessDueAsync_ServerRecovers_SendsEverythingAndResetsBackoff()
    {
        Add("K1ABC");
        Add("VK3XYZ", slot: Slot.AddMinutes(2));
        _server.FailNext = 1;
        await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct);

        _clock.Advance(TimeSpan.FromMinutes(1));
        (await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct)).Should().Be(2);

        _repo.PendingCount("W7LIT").Should().Be(0);
        _server.Requests.Select(r => r["tcall"]).Should().Equal("K1ABC", "VK3XYZ");
        _uploader.NextTryUtc.Should().Be(DateTime.MinValue);
        _uploader.Status.Health.Should().Be(ServiceHealth.Ok);
    }

    [Fact]
    public async Task ProcessDueAsync_SpotRefused_MarkedFailedAndNotSentAgain()
    {
        Add("K1ABC");
        _server.RejectReason = "Invalid callsign";

        await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct);

        _repo.Recent("W7LIT", 10).Single().UploadState.Should().Be(WsprUploadState.Failed);
        _uploader.Failed.Should().Be(1);
        _uploader.Status.Message.Should().Contain("refused").And.Contain("Invalid callsign");
        var calls = _server.Uris.Count;
        await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct);
        _server.Uris.Count.Should().Be(calls);
    }

    [Fact]
    public async Task ProcessDueAsync_SpotTooOld_MarkedFailedWithoutSending()
    {
        Add("K1ABC", slot: Slot.AddDays(-2));

        await _uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct);

        _server.Uris.Should().BeEmpty();
        _repo.Recent("W7LIT", 10).Single().UploadState.Should().Be(WsprUploadState.Failed);
    }

    [Fact]
    public async Task ProcessDueAsync_ServerHangs_TimesOutAndKeepsSpot()
    {
        Add("K1ABC");
        _server.Hang = true;
        var uploader = Uploader(_repo, _server, TimeSpan.FromMilliseconds(300));

        (await uploader.ProcessDueAsync("W7LIT", "DN40", "v", Ct)).Should().Be(0);

        _repo.PendingCount("W7LIT").Should().Be(1);
        uploader.Status.Message.Should().Contain("did not answer");
    }

    [Fact]
    public async Task ProcessDueAsync_QueueSurvivesRestart()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ft8client-wspr").FullName, "costas.db");
        var first = new WsprSpotRepository(new SqliteDatabase(path));
        first.Add(WsprSpotRow.From(new WsprSpot(Slot, -9, 0.1, 14_097_000, 0, "ND6P", "DM04", 30, false, 14_095_600), "W7LIT", null, WsprUploadState.Pending));
        _server.FailNext = 1;
        await Uploader(first, _server, TimeSpan.FromSeconds(20)).ProcessDueAsync("W7LIT", "DN40", "v", Ct);

        // A new process: a new repository over the same file, and a new uploader with no memory of the failure.
        var second = new WsprSpotRepository(new SqliteDatabase(path));
        second.PendingCount("W7LIT").Should().Be(1);
        (await Uploader(second, _server, TimeSpan.FromSeconds(20)).ProcessDueAsync("W7LIT", "DN40", "v", Ct)).Should().Be(1);

        second.PendingCount("W7LIT").Should().Be(0);
        _server.Requests.Single()["tcall"].Should().Be("ND6P");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "1 spot(s) added", true, false)]
    [InlineData(HttpStatusCode.OK, "<html>error</html>", false, false)]
    [InlineData(HttpStatusCode.BadRequest, "", false, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", false, true)]
    [InlineData(HttpStatusCode.TooManyRequests, "", false, true)]
    public void Parse_Reply_Classified(HttpStatusCode code, string body, bool accepted, bool retry)
    {
        var r = WsprnetClient.Parse(code, body);
        r.Accepted.Should().Be(accepted);
        r.Retry.Should().Be(retry);
    }
}
