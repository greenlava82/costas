// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Data.Database;
using Ft8Client.Data.Log;

namespace Ft8Client.Data.Tests;

public sealed class UploadQueueRepositoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 18, 2, 0, 0, DateTimeKind.Utc);

    private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAlive;
    private readonly QsoRepository _qsos;
    private readonly UploadQueueRepository _queue;

    public UploadQueueRepositoryTests()
    {
        var db = SqliteDatabase.InMemory(out _keepAlive);
        _qsos = new QsoRepository(db);
        _queue = new UploadQueueRepository(db);
    }

    public void Dispose() => _keepAlive.Dispose();

    private long Contact() =>
        _qsos.Insert(new QsoRecord { ProfileId = "home", StationCallsign = "W7LIT", Call = "K1ABC", Band = "20m", Mode = "FT8", QsoDateOn = Now });

    [Theory]
    [InlineData("clublog")]
    [InlineData("cloudlog")]
    public void Enqueue_NonQrzTarget_LeavesQrzStateAlone(string target)
    {
        var id = Contact();

        _queue.Enqueue(id, target, Now);

        var q = _qsos.Get(id)!;
        q.UploadState.Should().Be(UploadState.None);
        q.EqslUploadState.Should().Be(UploadState.None);
        _queue.All().Should().ContainSingle().Which.Target.Should().Be(target);
    }

    [Theory]
    [InlineData("clublog")]
    [InlineData("cloudlog")]
    [InlineData("eqsl")]
    public void Enqueue_OtherTargetAfterQrzUploaded_QrzStaysUploaded(string target)
    {
        var id = Contact();
        _qsos.SetUploadState(id, UploadState.Uploaded, null, 12345);

        _queue.Enqueue(id, target, Now);

        var q = _qsos.Get(id)!;
        q.UploadState.Should().Be(UploadState.Uploaded);
        q.QrzLogid.Should().Be(12345);
    }

    [Fact]
    public void Enqueue_QrzFailedThenClubLog_QrzFailureKept()
    {
        var id = Contact();
        _qsos.SetUploadState(id, UploadState.Failed, "Bad record");

        _queue.Enqueue(id, "clublog", Now);

        var q = _qsos.Get(id)!;
        q.UploadState.Should().Be(UploadState.Failed);
        q.UploadError.Should().Be("Bad record");
    }

    [Fact]
    public void Enqueue_Qrz_MarksQrzQueuedOnly()
    {
        var id = Contact();
        _qsos.SetUploadState(id, UploadState.Failed, "Timed out");

        _queue.Enqueue(id, "qrz", Now);

        var q = _qsos.Get(id)!;
        q.UploadState.Should().Be(UploadState.Queued);
        q.UploadError.Should().BeNull();
        q.EqslUploadState.Should().Be(UploadState.None);
    }

    [Fact]
    public void Enqueue_Eqsl_MarksEqslQueuedOnly()
    {
        var id = Contact();

        _queue.Enqueue(id, "eqsl", Now);

        var q = _qsos.Get(id)!;
        q.EqslUploadState.Should().Be(UploadState.Queued);
        q.UploadState.Should().Be(UploadState.None);
    }

    [Fact]
    public void RemoveFor_ContactAndTarget_RemovesOnlyThatTarget()
    {
        var id = Contact();
        var other = Contact();
        _queue.Enqueue(id, "cloudlog", Now);
        _queue.Enqueue(id, "clublog", Now);
        _queue.Enqueue(other, "cloudlog", Now);

        _queue.RemoveFor(id, "cloudlog");

        _queue.All().Select(i => (i.QsoId, i.Target)).Should().BeEquivalentTo([(id, "clublog"), (other, "cloudlog")]);
    }
}
