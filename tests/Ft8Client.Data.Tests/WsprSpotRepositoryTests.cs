// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Wspr;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;

namespace Ft8Client.Data.Tests;

public sealed class WsprSpotRepositoryTests : IDisposable
{
    private static readonly DateTime Slot = new(2026, 11, 26, 14, 2, 0, DateTimeKind.Utc);

    private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAlive;
    private readonly WsprSpotRepository _repo;

    public WsprSpotRepositoryTests() => _repo = new WsprSpotRepository(SqliteDatabase.InMemory(out _keepAlive));

    public void Dispose() => _keepAlive.Dispose();

    private static WsprSpotRow Row(string call = "ND6P", int snr = -9, DateTime? slot = null, long freq = 14_097_046, string profile = "W7LIT",
        WsprUploadState state = WsprUploadState.None) =>
        WsprSpotRow.From(new WsprSpot(slot ?? Slot, snr, 1.1, freq, 0, call, "DM04", 30, false, 14_095_600), profile, 1234.5, state);

    [Fact]
    public void Add_NewSpot_StoresEveryFieldAndSetsId()
    {
        var row = Row();

        _repo.Add(row).Should().BeTrue();

        row.Id.Should().BePositive();
        var back = _repo.Recent("W7LIT", 10).Should().ContainSingle().Subject;
        back.SlotUtc.Should().Be(Slot);
        back.SlotUtc.Kind.Should().Be(DateTimeKind.Utc);
        back.DialHz.Should().Be(14_095_600);
        back.FreqHz.Should().Be(14_097_046);
        back.Snr.Should().Be(-9);
        back.Dt.Should().Be(1.1);
        back.Callsign.Should().Be("ND6P");
        back.Grid.Should().Be("DM04");
        back.Dbm.Should().Be(30);
        back.IsHashed.Should().BeFalse();
        back.DistanceKm.Should().Be(1234.5);
        back.ToSpot().Should().Be(new WsprSpot(Slot, -9, 1.1, 14_097_046, 0, "ND6P", "DM04", 30, false, 14_095_600));
    }

    [Fact]
    public void Add_SameSignalSameSlot_IsStoredOnce()
    {
        _repo.Add(Row()).Should().BeTrue();
        _repo.Add(Row(snr: -3)).Should().BeFalse();

        _repo.Recent("W7LIT", 10).Should().ContainSingle().Which.Snr.Should().Be(-9);
    }

    [Fact]
    public void Add_SameSignalOtherProfile_IsStoredForBoth()
    {
        _repo.Add(Row(profile: "W7LIT")).Should().BeTrue();
        _repo.Add(Row(profile: "K1ABC")).Should().BeTrue();

        _repo.Recent("W7LIT", 10).Should().HaveCount(1);
        _repo.Recent("K1ABC", 10).Should().HaveCount(1);
    }

    [Fact]
    public void Add_NullGridAndDistance_Roundtrip()
    {
        var spot = new WsprSpot(Slot, -15, 0.2, 14_097_100, 1, "PJ4/K1ABC", null, 37, false, 14_095_600);
        _repo.Add(WsprSpotRow.From(spot, "W7LIT", null, WsprUploadState.None));

        var back = _repo.Recent("W7LIT", 1).Single();
        back.Grid.Should().BeNull();
        back.DistanceKm.Should().BeNull();
        back.ToSpot().Should().Be(spot);
    }

    [Fact]
    public void Recent_ManySlots_NewestFirstThenStrongest()
    {
        _repo.Add(Row("AA1AA", -20, Slot));
        _repo.Add(Row("BB2BB", -5, Slot));
        _repo.Add(Row("CC3CC", -30, Slot.AddMinutes(2)));

        _repo.Recent("W7LIT", 10).Select(r => r.Callsign).Should().Equal("CC3CC", "BB2BB", "AA1AA");
        _repo.Recent("W7LIT", 2).Should().HaveCount(2);
    }

    [Fact]
    public void Pending_MixedStates_ReturnsOnlyPendingOldestFirst()
    {
        _repo.Add(Row("AA1AA", slot: Slot.AddMinutes(2), state: WsprUploadState.Pending));
        _repo.Add(Row("BB2BB", slot: Slot, state: WsprUploadState.Pending));
        _repo.Add(Row("CC3CC", slot: Slot, state: WsprUploadState.None));
        _repo.Add(Row("DD4DD", slot: Slot, state: WsprUploadState.Uploaded));

        _repo.Pending("W7LIT", 10).Select(r => r.Callsign).Should().Equal("BB2BB", "AA1AA");
        _repo.PendingCount("W7LIT").Should().Be(2);
        _repo.Pending("W7LIT", 1).Should().HaveCount(1);
    }

    [Fact]
    public void SetUploadState_Uploaded_LeavesQueue()
    {
        var a = Row("AA1AA", state: WsprUploadState.Pending);
        var b = Row("BB2BB", state: WsprUploadState.Pending);
        _repo.Add(a);
        _repo.Add(b);

        _repo.SetUploadState([a.Id], WsprUploadState.Uploaded);

        _repo.Pending("W7LIT", 10).Select(r => r.Callsign).Should().Equal("BB2BB");
    }

    [Fact]
    public void Prune_OldSpots_KeepsPendingAndRecent()
    {
        _repo.Add(Row("OLD1", slot: Slot.AddDays(-40)));
        _repo.Add(Row("OLDQ", slot: Slot.AddDays(-40), state: WsprUploadState.Pending));
        _repo.Add(Row("NEW1", slot: Slot));

        _repo.Prune(Slot.AddMinutes(2), TimeSpan.FromDays(30)).Should().Be(1);

        _repo.Recent("W7LIT", 10).Select(r => r.Callsign).Should().BeEquivalentTo("OLDQ", "NEW1");
    }

    [Fact]
    public void Migration_Latest_IncludesWsprSpotTable() => SqliteDatabase.LatestVersion.Should().BeGreaterThanOrEqualTo(8);
}
