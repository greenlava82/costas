// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Dapper;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Microsoft.Data.Sqlite;

namespace Ft8Client.Data.Tests;

/// <summary>
/// Several threads write the one log database at once: the decode thread (band statistics), contact logging, spot
/// feeds, QRZ lookups and log syncs. None of them may fail with "database is locked" (Sentry COSTAS-6, 2026-11-21).
/// </summary>
public class DatabaseConcurrencyTests
{
    private static readonly DateTime Slot = new(2026, 11, 21, 17, 24, 0, DateTimeKind.Utc);

    private static SqliteDatabase NewDatabase() =>
        new(Path.Combine(Directory.CreateTempSubdirectory("ft8lock").FullName, "costas.db"));

    private static QsoRecord Contact(int i, string source = QsoSource.Adif) => new()
    {
        ProfileId = "p", Call = "K" + i + "ABC", Band = "20m", Mode = "FT8", StationCallsign = "N0CALL", Source = source,
        QsoDateOn = Slot.AddDays(-30).AddMinutes(i * 5),
    };

    // A second connection that gives up at once instead of waiting: it shows whether the write lock is free right now.
    private static SqliteConnection Impatient(SqliteDatabase db)
    {
        var c = db.Open();
        c.Execute("PRAGMA busy_timeout = 0;");
        c.DefaultTimeout = 1; // Microsoft.Data.Sqlite's own retry, in seconds (0 would mean wait forever)
        return c;
    }

    [Fact]
    public void Open_AnyConnection_WaitsForOtherWriters()
    {
        var db = NewDatabase();
        using var c = db.Open();

        c.ExecuteScalar<int>("PRAGMA busy_timeout;").Should().Be((int)SqliteDatabase.BusyTimeout.TotalMilliseconds);
        c.DefaultTimeout.Should().Be((int)SqliteDatabase.BusyTimeout.TotalSeconds);
        c.ExecuteScalar<string>("PRAGMA journal_mode;").Should().Be("wal");
    }

    [Fact]
    public async Task Add_WhileAnotherConnectionHoldsTheWriteLock_WaitsAndSucceeds()
    {
        var db = NewDatabase();
        var stats = new BandStatRepository(db);
        using var holder = db.Open();
        var tx = holder.BeginTransaction();
        holder.Execute("INSERT INTO band_stat (slot_start_utc, band, mode, decodes) VALUES (@s, '40m', 'FT8', 1)", new { s = Slot.AddMinutes(-1) }, tx);
        var release = Task.Run(async () =>
        {
            await Task.Delay(1500);
            tx.Commit();
            tx.Dispose();
        }, TestContext.Current.CancellationToken);

        var add = () => stats.Add(Slot, "20m", "FT8", 12);

        add.Should().NotThrow("a write waits for the one in progress instead of failing with 'database is locked'");
        await release;
        stats.Since("20m", "FT8", Slot.AddHours(-1)).Should().ContainSingle().Which.Decodes.Should().Be(12);
    }

    [Fact]
    public async Task Writes_FromManyThreadsAtOnce_AllSucceed()
    {
        var db = NewDatabase();
        var stats = new BandStatRepository(db);
        var spots = new SpotRepository(db);
        var qsos = new QsoRepository(db);
        var cache = new CallsignCacheRepository(db);
        var sync = new SyncStateRepository(db);
        var queue = new UploadQueueRepository(db);
        const int Threads = 8, PerThread = 40;

        // What runs at once in the app: band stats per decode, spots from the feeds, contacts logged and queued for
        // upload, lookups cached, sync marks, and an import in batches.
        var work = Enumerable.Range(0, Threads).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < PerThread; i++)
            {
                var n = t * PerThread + i;
                switch (t % 4)
                {
                    case 0:
                        stats.Add(Slot.AddSeconds(15 * n), "20m", "FT8", n);
                        break;
                    case 1:
                        spots.Add(new SpotRecord { MyCall = "N0CALL", RxCall = "R" + n, Band = "20m", Mode = "FT8", TimeUtc = Slot.AddMinutes(10 * n), Source = "pskreporter" });
                        cache.Put(new CallsignCacheEntry { Call = "C" + n, FetchedUtc = Slot, Found = true });
                        break;
                    case 2:
                        var (id, _) = qsos.Upsert(Contact(n));
                        queue.Enqueue(id, "qrz", Slot);
                        sync.Set("p", "t" + n, Slot, n);
                        break;
                    default:
                        qsos.UpsertMany(Enumerable.Range(0, 5).Select(k => Contact(100_000 + n * 5 + k, QsoSource.Lotw)).ToList());
                        break;
                }
            }
        }, TestContext.Current.CancellationToken)).ToArray();

        var all = Task.WhenAll(work);
        await all.Invoking(t => t).Should().NotThrowAsync();
        stats.Since("20m", "FT8", Slot.AddDays(-1)).Should().HaveCount(2 * PerThread);
        qsos.Count().Should().Be(2 * PerThread + 2 * PerThread * 5);
    }

    public static TheoryData<string> BatchMethods => ["UpsertMany", "UpsertEach", "ApplyLotwConfirmations"];

    [Theory]
    [MemberData(nameof(BatchMethods))]
    public void BatchWrite_LargeImport_GivesTheWriteLockBackBetweenBatches(string method)
    {
        var db = NewDatabase();
        var qsos = new QsoRepository(db);
        var stats = new BandStatRepository(db);
        Exception? blocked = null;

        // Partway through the import (the first record of the second batch), another write must get in at once.
        IEnumerable<QsoRecord> Records()
        {
            for (var i = 0; i < QsoRepository.BatchSize * 3; i++)
            {
                if (i == QsoRepository.BatchSize)
                {
                    try
                    {
                        using var other = Impatient(db);
                        other.Execute("INSERT INTO band_stat (slot_start_utc, band, mode, decodes) VALUES (@Slot, '20m', 'FT8', 7)", new { Slot });
                    }
                    catch (SqliteException ex)
                    {
                        blocked = ex;
                    }
                }
                yield return Contact(i);
            }
        }

        switch (method)
        {
            case "UpsertMany": qsos.UpsertMany(Records()); break;
            case "UpsertEach": qsos.UpsertEach(Records()); break;
            default: qsos.ApplyLotwConfirmations(Records()); break;
        }

        blocked.Should().BeNull("an import must not hold the write lock from its first record to its last");
        stats.Since("20m", "FT8", Slot.AddMinutes(-1)).Should().ContainSingle();
        qsos.Count().Should().Be(QsoRepository.BatchSize * 3);
    }

    [Fact]
    public void UpsertMany_FailsInALaterBatch_KeepsEarlierBatchesAndRaisesChanged()
    {
        var db = NewDatabase();
        var qsos = new QsoRepository(db);
        var changed = 0;
        qsos.Changed += () => changed++;

        IEnumerable<QsoRecord> Records()
        {
            for (var i = 0; i < QsoRepository.BatchSize; i++) yield return Contact(i);
            throw new IOException("ADIF file went away");
        }

        var import = () => qsos.UpsertMany(Records());

        import.Should().Throw<IOException>();
        qsos.Count().Should().Be(QsoRepository.BatchSize, "the batch written before the error stays in the log");
        changed.Should().Be(1, "the log view shows the contacts that did go in");
        qsos.UpsertMany(Enumerable.Range(0, QsoRepository.BatchSize + 10).Select(i => Contact(i)).ToList())
            .Should().Be((10, QsoRepository.BatchSize), "running the import again merges what is already there");
    }

    [Fact]
    public void Upsert_DuplicateLookup_UsesTheCaseInsensitiveCallBandIndex()
    {
        var db = NewDatabase();
        using var c = db.Open();

        var plan = string.Join("\n", c.Query<(long, long, long, string Detail)>(
            "EXPLAIN QUERY PLAN SELECT * FROM qso WHERE call = @call COLLATE NOCASE AND band = @band COLLATE NOCASE AND qso_date_on BETWEEN @from AND @to",
            new { call = "K1ABC", band = "20m", from = Slot.AddHours(-1), to = Slot }).Select(r => r.Detail));

        plan.Should().Contain("ix_qso_call_band_nocase");
    }
}
