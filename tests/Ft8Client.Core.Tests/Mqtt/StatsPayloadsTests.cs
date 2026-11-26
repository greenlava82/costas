// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Text.Json.Nodes;
using Ft8Client.Core.Mqtt;

namespace Ft8Client.Core.Tests.Mqtt;

public class StatsPayloadsTests
{
    private static readonly DateTime Today = new(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);

    private static StatsContact Contact(string call, DateTime at, bool newOne = false) =>
        new(call, "20m", "FT8", 14_074_500, at, "Japan", 339, null, "PM95", 35.5, 139.0, 8411, newOne, "-10", "-12", true);

    private static DashboardStats Stats(IReadOnlyList<MapPoint>? points = null, IReadOnlyList<NewEntity>? newOnes = null,
                                        IReadOnlyList<StatsContact>? recent = null, StatsContact? farthest = null) =>
        new(Today, 3, 12, 4321, 150, newOnes ?? [], farthest, recent ?? [],
            [.. Enumerable.Range(0, 7).Select(i => new DayCount(Today.AddDays(i - 6), i, [new("40m", 0), new("20m", i)]))], 9, points ?? []);

    private static JsonNode Parse(string s) => JsonNode.Parse(s)!;

    [Fact]
    public void Counts_FourCounts()
    {
        var j = Parse(StatsPayloads.Counts(Stats()));

        j["qsos_today"]!.GetValue<int>().Should().Be(3);
        j["qsos_week"]!.GetValue<int>().Should().Be(12);
        j["qsos_total"]!.GetValue<int>().Should().Be(4321);
        j["dxcc_worked_total"]!.GetValue<int>().Should().Be(150);
    }

    [Fact]
    public void WorkedWeekMap_200LongPoints_NewestThatFitUnder8KB()
    {
        var points = Enumerable.Range(0, 250)
            .Select(i => new MapPoint($"VP2V/K{i:D4}ABC", -33.87, 151.21, "British Virgin Islands", "160m", Today.AddHours(12).AddMinutes(-i)))
            .ToList();

        var payload = StatsPayloads.WorkedWeekMap(Stats(points));

        StatsPayloads.AttributeBytes(payload).Should().BeLessThanOrEqualTo(StatsPayloads.AttributeLimitBytes);
        var kept = Parse(payload)["attributes"]!["points"]!.AsArray();
        kept.Count.Should().BeGreaterThan(50).And.BeLessThan(200);
        kept[0]!["call"]!.GetValue<string>().Should().Be("VP2V/K0000ABC", "the newest stations are kept");
        kept[^1]!["call"]!.GetValue<string>().Should().Be($"VP2V/K{kept.Count - 1:D4}ABC");
        Parse(payload)["state"]!.GetValue<int>().Should().Be(9, "the state counts every grid, not just the points sent");
    }

    [Fact]
    public void WorkedWeekMap_ShortPoints_CappedAt200()
    {
        var points = Enumerable.Range(0, 300).Select(i => new MapPoint($"K{i}", 1, 2, null, "6m", Today)).ToList();

        var kept = Parse(StatsPayloads.WorkedWeekMap(Stats(points)))["attributes"]!["points"]!.AsArray();

        kept.Count.Should().BeLessThanOrEqualTo(StatsPayloads.MaxMapPoints);
        var first = kept[0]!.AsObject();
        first.Select(p => p.Key).Should().Equal("call", "lat", "lon", "country", "band", "ts");
        first["ts"]!.GetValue<string>().Should().Be("2026-11-26T00:00:00Z");
    }

    [Fact]
    public void NewDxccToday_ManyEntities_UnderLimitKeepsNewestInTimeOrder()
    {
        var many = Enumerable.Range(0, 400).Select(i => new NewEntity(i, $"Entity number {i:D3} with a long name", $"CALL{i:D3}", Today.AddSeconds(i))).ToList();

        var payload = StatsPayloads.NewDxccToday(Stats(newOnes: many));

        StatsPayloads.AttributeBytes(payload).Should().BeLessThanOrEqualTo(StatsPayloads.AttributeLimitBytes);
        Parse(payload)["state"]!.GetValue<int>().Should().Be(400);
        var kept = Parse(payload)["attributes"]!["entities"]!.AsArray();
        kept[^1]!["call"]!.GetValue<string>().Should().Be("CALL399");
        kept[0]!["call"]!.GetValue<string>().Should().Be($"CALL{400 - kept.Count:D3}", "oldest first, ending with the newest");
        kept[0]!.AsObject().Select(p => p.Key).Should().Equal("dxcc", "country", "call", "ts");
    }

    [Fact]
    public void RecentQsos_EveryFieldNewestTime()
    {
        var recent = new[] { Contact("JA1ABC", Today.AddHours(3), newOne: true), Contact("JA2ABC", Today.AddHours(1)) };

        var j = Parse(StatsPayloads.RecentQsos(Stats(recent: recent)));

        j["state"]!.GetValue<string>().Should().Be("2026-11-26T03:00:00Z");
        var q = j["attributes"]!["qsos"]!.AsArray()[0]!.AsObject();
        q.Select(p => p.Key).Should().Equal("call", "band", "mode", "frequency_hz", "ts", "country", "dxcc", "state_or_region", "grid", "lat", "lon",
            "distance_km", "is_new_dxcc", "rst_sent", "rst_rcvd", "qrz_synced");
        q["is_new_dxcc"]!.GetValue<bool>().Should().BeTrue();
        q["qrz_synced"]!.GetValue<bool>().Should().BeTrue();
        q["frequency_hz"]!.GetValue<long>().Should().Be(14_074_500);
    }

    [Fact]
    public void RecentQsos_EmptyLog_NullState()
    {
        var j = Parse(StatsPayloads.RecentQsos(Stats()));

        j["state"].Should().BeNull();
        j["attributes"]!["qsos"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public void DailyQsos_OldestFirstWithBands()
    {
        var j = Parse(StatsPayloads.DailyQsos(Stats()));

        j["state"]!.GetValue<int>().Should().Be(3, "today's count");
        var days = j["attributes"]!["days"]!.AsArray();
        days.Should().HaveCount(7);
        days[0]!["date"]!.GetValue<string>().Should().Be("2026-11-20");
        days[6]!["date"]!.GetValue<string>().Should().Be("2026-11-26");
        days[6]!["by_band"]!["20m"]!.GetValue<int>().Should().Be(6);
        days[6]!["by_band"]!["40m"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void FarthestToday_NoneToday_NullStateAndAttributes()
    {
        var j = Parse(StatsPayloads.FarthestToday(Stats()));

        j["state"].Should().BeNull();
        j["attributes"]!.AsObject().Select(p => p.Key).Should().Equal("call", "country", "grid", "ts");
    }

    [Fact]
    public void FarthestToday_Contact_KmAndDetails()
    {
        var j = Parse(StatsPayloads.FarthestToday(Stats(farthest: Contact("JA1ABC", Today.AddHours(2)))));

        j["state"]!.GetValue<int>().Should().Be(8411);
        j["attributes"]!["call"]!.GetValue<string>().Should().Be("JA1ABC");
        j["attributes"]!["grid"]!.GetValue<string>().Should().Be("PM95");
        j["attributes"]!["ts"]!.GetValue<string>().Should().Be("2026-11-26T02:00:00Z");
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(true, 59, 0)]
    [InlineData(true, 60, 1)]
    [InlineData(true, 3 * 3600 + 30, 180)]
    public void SessionMinutes_WholeMinutesOfARunningSession(bool active, int seconds, int minutes)
    {
        var since = Today.AddHours(10);
        var s = new OnAirSnapshot(active, active ? "abc" : null, active ? since : null, null, false);

        StatsPayloads.SessionMinutes(s, since.AddSeconds(seconds)).Should().Be(minutes);
        Parse(StatsPayloads.SessionDuration(s, since.AddSeconds(seconds)))["state"]!.GetValue<int>().Should().Be(minutes);
    }

    [Fact]
    public void SessionMinutes_ClockBehindStart_Zero() =>
        StatsPayloads.SessionMinutes(new OnAirSnapshot(true, "abc", Today.AddHours(1), null, false), Today).Should().Be(0);

    [Fact]
    public void QrzSync_LongError_Shortened()
    {
        var j = Parse(StatsPayloads.QrzSync(new QrzSyncStatus(QrzSyncState.Error, Today, 4, new string('x', 2000))));

        j["state"]!.GetValue<string>().Should().Be("error");
        j["attributes"]!["pending_count"]!.GetValue<int>().Should().Be(4);
        j["attributes"]!["last_sync_ts"]!.GetValue<string>().Should().Be("2026-11-26T00:00:00Z");
        j["attributes"]!["last_error"]!.GetValue<string>().Length.Should().Be(StatsPayloads.MaxErrorLength);
    }

    [Theory]
    [InlineData(QrzSyncState.Ok, "ok")]
    [InlineData(QrzSyncState.Pending, "pending")]
    [InlineData(QrzSyncState.Error, "error")]
    [InlineData(QrzSyncState.Disabled, "disabled")]
    public void QrzStateName_EveryState(QrzSyncState state, string name) => StatsPayloads.QrzStateName(state).Should().Be(name);

    [Fact]
    public void PskSpots_CountAndFarthest()
    {
        var j = Parse(StatsPayloads.PskSpots(new PskSpotSummary(17, 9876, "VK2XYZ", "QF56")));

        j["state"]!.GetValue<int>().Should().Be(17);
        j["attributes"]!["farthest_km"]!.GetValue<int>().Should().Be(9876);
        j["attributes"]!["farthest_reporter"]!.GetValue<string>().Should().Be("VK2XYZ");
        j["attributes"]!["farthest_grid"]!.GetValue<string>().Should().Be("QF56");
    }

    [Fact]
    public void ContactEvent_Described_OriginalFieldsPlusEntry()
    {
        var j = Parse(StatsPayloads.ContactEvent("new_dxcc", new LoggedQso("JA1ABC", "20m", "FT8", 14_074_000, Today.AddHours(3)),
            Contact("JA1ABC", Today.AddHours(3), newOne: true))).AsObject();

        j["event_type"]!.GetValue<string>().Should().Be("new_dxcc");
        j["frequency"]!.GetValue<long>().Should().Be(14_074_000, "the dial frequency, as before");
        j["frequency_hz"]!.GetValue<long>().Should().Be(14_074_500);
        j["country"]!.GetValue<string>().Should().Be("Japan");
        j["dxcc"]!.GetValue<int>().Should().Be(339);
        j["distance_km"]!.GetValue<int>().Should().Be(8411);
        j["is_new_dxcc"]!.GetValue<bool>().Should().BeTrue();
        j["ts"]!.GetValue<string>().Should().Be("2026-11-26T03:00:00Z");
    }

    [Fact]
    public void ContactEvent_NotInLog_SameKeysWithNulls()
    {
        var full = Parse(StatsPayloads.ContactEvent("qso_logged", new LoggedQso("K1ABC", "20m", "FT8", 1, Today), Contact("K1ABC", Today))).AsObject();
        var bare = Parse(StatsPayloads.ContactEvent("qso_logged", new LoggedQso("K1ABC", "20m", "FT8", 1, Today), null)).AsObject();

        bare.Select(p => p.Key).Should().Equal(full.Select(p => p.Key));
        bare["country"].Should().BeNull();
        bare["is_new_dxcc"]!.GetValue<bool>().Should().BeFalse();
        bare["call"]!.GetValue<string>().Should().Be("K1ABC");
    }

    [Fact]
    public void AttributeBytes_CountsUtf8()
    {
        var payload = StatsPayloads.FarthestToday(Stats(farthest: Contact("JA1ABC", Today) with { Country = "Åland" }));

        StatsPayloads.AttributeBytes(payload).Should().Be(System.Text.Encoding.UTF8.GetByteCount(Parse(payload)["attributes"]!.ToJsonString()));
    }
}
