// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using Ft8Client.Core.Time;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Integrations.ClubLog;
using Ft8Client.Integrations.Net;

namespace Ft8Client.Integrations.Tests;

/// <summary>Club Log real-time uploads (API from clublog.org's developer pages, read 2026-11-15).</summary>
public class ClubLogRealtimeTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 2, 0, 0, DateTimeKind.Utc);

    private sealed class Rig : IDisposable
    {
        private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAlive;
        private readonly HttpClient _http;
        public readonly QsoRepository Qsos;
        public readonly UploadQueueRepository Queue;
        public readonly ManualClock Clock = new(Now);
        public readonly List<Dictionary<string, string>> Posts = [];
        public readonly IClubLogStopStore Stops;
        public ClubLogUploadService Worker;
        public HttpStatusCode Code = HttpStatusCode.OK;
        public string Reply = "QSO OK";

        public Rig(IClubLogStopStore? stops = null)
        {
            Stops = stops ?? new MemoryClubLogStopStore();
            var db = SqliteDatabase.InMemory(out _keepAlive);
            Qsos = new QsoRepository(db);
            Queue = new UploadQueueRepository(db);
            _http = HttpFactory.Create("W7LIT", new Handler(async (req, ct) =>
            {
                var body = await req.Content!.ReadAsStringAsync(ct);
                Posts.Add(body.Split('&').Select(p => p.Split('=')).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' '))));
                return new HttpResponseMessage(Code) { Content = new StringContent(Reply) };
            }));
            Worker = NewWorker();
        }

        // A fresh service, as after a restart; the stop store is what survives.
        public ClubLogUploadService NewWorker() =>
            new(new ClubLogRealtimeClient(_http), Qsos, Queue, Clock, (wait, _) => { Clock.Advance(wait); return Task.CompletedTask; }, Stops);

        public long Contact(string call)
        {
            var id = Qsos.Insert(new QsoRecord { ProfileId = "home", StationCallsign = "W7LIT", Call = call, Band = "20m", Mode = "FT8", QsoDateOn = Now });
            Queue.Enqueue(id, ClubLogUploadService.Target, Now);
            return id;
        }

        public Task<int> Run(string password = "app-pass", string email = "op@example.com") =>
            Worker.ProcessDueAsync("home", "W7LIT", email, password, "test-key", TestContext.Current.CancellationToken);

        public void Dispose()
        {
            _http.Dispose();
            _keepAlive.Dispose();
        }
    }

    [Fact]
    public async Task ProcessDueAsync_Stored_SendsDocumentedFieldsAndRemovesFromQueue()
    {
        using var r = new Rig();
        r.Contact("K1ABC");

        (await r.Run()).Should().Be(1);

        var post = r.Posts.Should().ContainSingle().Subject;
        post.Keys.Should().BeEquivalentTo(["email", "password", "callsign", "adif", "api"]);
        post["email"].Should().Be("op@example.com");
        post["callsign"].Should().Be("W7LIT");
        post["api"].Should().Be("test-key");
        post["adif"].Should().ContainEquivalentOf("<call:5>K1ABC");
        post["adif"].TrimEnd().Should().EndWithEquivalentOf("<eor>");
        r.Queue.All().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDueAsync_Duplicate_CountsAsStored()
    {
        using var r = new Rig { Reply = "QSO Duplicate" };
        r.Contact("K1ABC");
        (await r.Run()).Should().Be(1);
        r.Queue.All().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDueAsync_LoginRefused_StopsUntilPasswordChanges()
    {
        using var r = new Rig { Code = HttpStatusCode.Forbidden, Reply = "Invalid login" };
        r.Contact("K1ABC");
        r.Contact("K2ABC");

        (await r.Run()).Should().Be(0);
        (await r.Run()).Should().Be(0);
        r.Posts.Should().HaveCount(1, "Club Log asks clients to stop at once after a 403");
        r.Worker.Status.Message.Should().Contain("Invalid login");

        r.Code = HttpStatusCode.OK;
        (await r.Run("new-pass")).Should().Be(2);
    }

    [Fact]
    public async Task ProcessDueAsync_LoginRefusedThenNewInstance_SendsNothing()
    {
        using var r = new Rig { Code = HttpStatusCode.Forbidden, Reply = "Invalid login" };
        r.Contact("K1ABC");
        await r.Run();
        r.Stops.Load("home").Should().NotBeNull();

        r.Code = HttpStatusCode.OK;
        r.Worker = r.NewWorker();
        (await r.Run()).Should().Be(0);

        r.Posts.Should().HaveCount(1, "a restart must not send again after a 403");
        r.Worker.Stopped.Should().BeTrue();
        r.Worker.Status.Health.Should().Be(Ft8Client.Core.Services.ServiceHealth.Down);
        r.Worker.Status.Message.Should().Contain("Invalid login").And.Contain("Settings");
    }

    [Fact]
    public async Task ProcessDueAsync_StoredStopEmailChanged_ClearsStopAndSends()
    {
        using var r = new Rig { Code = HttpStatusCode.Forbidden, Reply = "Invalid login" };
        r.Contact("K1ABC");
        await r.Run();

        r.Code = HttpStatusCode.OK;
        r.Worker = r.NewWorker();
        (await r.Run(email: "new@example.com")).Should().Be(1);

        r.Stops.Load("home").Should().BeNull();
        r.Worker.Stopped.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessDueAsync_StoredStopCleared_Sends()
    {
        using var r = new Rig { Code = HttpStatusCode.Forbidden, Reply = "Invalid login" };
        r.Contact("K1ABC");
        await r.Run();

        r.Code = HttpStatusCode.OK;
        r.Stops.Save("home", null);
        (await r.Run()).Should().Be(1, "the operator resumed uploads");
    }

    [Fact]
    public async Task ProcessDueAsync_Rejected_DroppedAndNextSent()
    {
        using var r = new Rig { Code = HttpStatusCode.BadRequest, Reply = "Bad date" };
        r.Contact("K1ABC");
        await r.Run();

        r.Queue.All().Should().BeEmpty("a record Club Log rejects can never succeed");
        r.Worker.Status.Message.Should().Contain("K1ABC").And.Contain("Bad date");
    }

    [Fact]
    public async Task ProcessDueAsync_ServerError_StaysQueued()
    {
        using var r = new Rig { Code = HttpStatusCode.InternalServerError, Reply = "maintenance" };
        r.Contact("K1ABC");
        await r.Run();

        r.Queue.All().Should().ContainSingle().Which.NextTryUtc.Should().BeAfter(Now);
    }

    [Fact]
    public async Task ProcessDueAsync_TwoContacts_SpacedTwoSecondsApart()
    {
        using var r = new Rig();
        r.Contact("K1ABC");
        r.Contact("K2ABC");
        await r.Run();
        r.Clock.UtcNow.Should().BeOnOrAfter(Now + ClubLogUploadService.Spacing);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
