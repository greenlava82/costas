// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Diagnostics;
using System.Text.Json.Nodes;
using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.Core;
using Ft8Client.Core.Mqtt;
using Ft8Client.Core.Services;
using Ft8Client.Data.Log;
using Ft8Client.Data.Secrets;
using Ft8Client.Integrations.Tests.Mqtt;

namespace Ft8Client.App.Tests;

/// <summary>The dashboard statistics end to end: a real log database, the MQTT service and a test broker.</summary>
public sealed class MqttDashboardStatsTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 20, 15, 0, DateTimeKind.Utc);

    private static QsoRecord Row(string call, DateTime on, string band = "20m", string? grid = null, string station = "W7LIT", string? key = null) => new()
    {
        ProfileId = "home",
        Call = call,
        QsoDateOn = on,
        QsoDateOff = on.AddMinutes(1),
        Band = band,
        FreqHz = 14_074_500,
        Mode = "FT8",
        Gridsquare = grid,
        EntityKey = key,
        StationCallsign = station,
        RstSent = "-10",
        RstRcvd = "-08",
        CreatedUtc = on,
        ModifiedUtc = on,
    };

    private static JsonNode J(string payload) => JsonNode.Parse(payload)!;

    private static Func<string, bool> Field(string name, int value) =>
        p => p.Length > 0 && J(p)[name] is { } v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number && v.GetValue<int>() == value;

    private static Func<string, bool> State(int value) => Field("state", value);

    [Fact]
    public async Task Start_LogAlreadyHoldsContacts_CountsMatchTheLog()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, stats: true);
        st.Qsos!.Insert(Row("JA1ABC", Now.AddHours(-2), grid: "PM95"));
        st.Qsos.Insert(Row("DL1ABC", Now.AddHours(-1)));
        st.Qsos.Insert(Row("K1ABC", Now.AddDays(-1), band: "40m", grid: "FN42"));
        st.Qsos.Insert(Row("JA2ABC", Now.AddDays(-10)));
        st.Qsos.Insert(Row("VK2ABC", Now.AddHours(-3), station: "W7LIT/P")); // another station's log: not counted

        st.Service.Start();

        var counts = J(await MqttStation.RetainedAsync(broker, st.Topics.Stats, Field("qsos_total", 4)));
        counts["qsos_today"]!.GetValue<int>().Should().Be(2);
        counts["qsos_week"]!.GetValue<int>().Should().Be(3);
        counts["dxcc_worked_total"]!.GetValue<int>().Should().Be(3, "Japan, Germany and the USA");
        var newToday = J(await MqttStation.RetainedAsync(broker, st.Topics.NewDxccToday, State(1)));
        newToday["attributes"]!["entities"]!.AsArray().Single()!["call"]!.GetValue<string>().Should().Be("DL1ABC", "Japan was worked ten days ago");
        var far = J(await MqttStation.RetainedAsync(broker, st.Topics.FarthestToday, p => p.Contains("JA1ABC", StringComparison.Ordinal)));
        far["state"]!.GetValue<int>().Should().BeGreaterThan(8000);
        var daily = J(await MqttStation.RetainedAsync(broker, st.Topics.DailyQsos, State(2)))["attributes"]!["days"]!.AsArray();
        daily.Should().HaveCount(7);
        daily[^1]!["by_band"]!["20m"]!.GetValue<int>().Should().Be(2);
        daily[^2]!["by_band"]!["40m"]!.GetValue<int>().Should().Be(1);
        var map = J(await MqttStation.RetainedAsync(broker, st.Topics.WorkedWeekMap, State(2)));
        map["attributes"]!["points"]!.AsArray().Select(p => p!["call"]!.GetValue<string>()).Should().Equal("DL1ABC", "JA1ABC", "K1ABC");
        var recent = J(await MqttStation.RetainedAsync(broker, st.Topics.RecentQsos, p => p.Contains("JA2ABC", StringComparison.Ordinal)));
        recent["state"]!.GetValue<string>().Should().Be("2026-11-24T19:16:00Z");
        recent["attributes"]!["qsos"]!.AsArray().Should().HaveCount(4);
    }

    [Fact]
    public async Task Restart_MidSession_SecondCopyRecountsEverything()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, stats: true);
        st.Qsos!.Insert(Row("K1ABC", Now.AddHours(-1)));
        st.Service.Start();
        await MqttStation.RetainedAsync(broker, st.Topics.Stats, Field("qsos_today", 1));
        await st.Service.DisposeAsync();

        st.Qsos.Insert(Row("K2ABC", Now.AddMinutes(-5))); // logged while nothing was publishing
        await using var again = new MqttService(st.Session, st.Tx, st.Settings, st.Secrets, st.Clock, allowed: true, st.Stats);
        again.Start();

        var counts = J(await MqttStation.RetainedAsync(broker, st.Topics.Stats, Field("qsos_today", 2)));
        counts["qsos_total"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public async Task ContactLogged_EventsAndStatsWithinFiveSeconds()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, Mode.Psk31, stats: true);
        st.Qsos!.Insert(Row("K1ABC", Now.AddDays(-30)));
        using var keyboard = new KeyboardSession(st.Session, st.Clock);
        st.Service.Start();
        await st.OnlineAsync(broker);
        await MqttStation.RetainedAsync(broker, st.Topics.Stats, Field("qsos_total", 1));

        var watch = Stopwatch.StartNew();
        st.WorkKp4jrs(keyboard);
        var logged = J((await broker.WaitForAsync(m => m.Topic == st.Topics.QsoEvent && m.Payload.Contains("qso_logged", StringComparison.Ordinal))).Payload);
        var newOne = J((await broker.WaitForAsync(m => m.Topic == st.Topics.QsoEvent && m.Payload.Contains("\"new_dxcc\"", StringComparison.Ordinal))).Payload);
        await MqttStation.RetainedAsync(broker, st.Topics.Stats, Field("qsos_today", 1));
        await MqttStation.RetainedAsync(broker, st.Topics.DailyQsos, State(1));
        var recent = J(await MqttStation.RetainedAsync(broker, st.Topics.RecentQsos, p => p.Contains("KP4JRS", StringComparison.Ordinal)));
        watch.Stop();

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5) + WorkTime(), "logging updates the event and counts within 5 s");
        logged["call"]!.GetValue<string>().Should().Be("KP4JRS");
        logged["mode"]!.GetValue<string>().Should().Be("PSK31");
        logged["country"]!.GetValue<string>().Should().Be("Puerto Rico");
        logged["dxcc"]!.GetValue<int>().Should().Be(202);
        logged["is_new_dxcc"]!.GetValue<bool>().Should().BeTrue();
        logged["distance_km"]!.GetValue<int>().Should().BeGreaterThan(3000);
        logged["lat"].Should().NotBeNull();
        logged["qrz_synced"]!.GetValue<bool>().Should().BeFalse();
        newOne["event_type"]!.GetValue<string>().Should().Be("new_dxcc");
        newOne["call"]!.GetValue<string>().Should().Be("KP4JRS");
        var top = recent["attributes"]!["qsos"]!.AsArray()[0]!;
        top["call"]!.GetValue<string>().Should().Be("KP4JRS");
        top["is_new_dxcc"]!.GetValue<bool>().Should().BeTrue();
        foreach (var key in top.AsObject().Select(p => p.Key)) logged.AsObject().ContainsKey(key).Should().BeTrue($"the event carries {key} like recent_qsos");
        broker.Published.Where(m => m.Topic == st.Topics.QsoEvent).Should().OnlyContain(m => !m.Retain);
    }

    // The contact itself (hearing a CQ, answering, logging) is driven before the clock starts mattering; allow for it.
    private static TimeSpan WorkTime() => TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SecondContactSameEntity_NoNewDxccEvent()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, Mode.Psk31, stats: true);
        st.Qsos!.Insert(Row("KP4ABC", Now.AddDays(-400), key: "KP4"));
        using var keyboard = new KeyboardSession(st.Session, st.Clock);
        st.Service.Start();
        await st.OnlineAsync(broker);

        st.WorkKp4jrs(keyboard);

        var logged = J((await broker.WaitForAsync(m => m.Topic == st.Topics.QsoEvent)).Payload);
        logged["is_new_dxcc"]!.GetValue<bool>().Should().BeFalse("Puerto Rico was worked last year");
        await MqttStation.RetainedAsync(broker, st.Topics.Stats, Field("qsos_today", 1));
        broker.Published.Should().NotContain(m => m.Topic == st.Topics.QsoEvent && m.Payload.Contains("\"new_dxcc\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UtcMidnight_TodayCountsReset()
    {
        var late = new DateTime(2026, 11, 24, 23, 59, 50, DateTimeKind.Utc);
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, stats: true, now: late);
        st.Qsos!.Insert(Row("JA1ABC", late.AddMinutes(-30), grid: "PM95"));
        st.Service.Start();
        await MqttStation.RetainedAsync(broker, st.Topics.Stats, Field("qsos_today", 1));
        await MqttStation.RetainedAsync(broker, st.Topics.NewDxccToday, State(1));

        st.Clock.Advance(TimeSpan.FromSeconds(15)); // 00:00:05 the next day; nothing else changes

        var counts = J(await MqttStation.RetainedAsync(broker, st.Topics.Stats, Field("qsos_today", 0)));
        counts["qsos_week"]!.GetValue<int>().Should().Be(1);
        await MqttStation.RetainedAsync(broker, st.Topics.NewDxccToday, State(0));
        await MqttStation.RetainedAsync(broker, st.Topics.FarthestToday, p => J(p)["state"] is null);
        var days = J(await MqttStation.RetainedAsync(broker, st.Topics.DailyQsos, State(0)))["attributes"]!["days"]!.AsArray();
        days[^1]!["date"]!.GetValue<string>().Should().Be("2026-11-25");
        days[^2]!["total"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task RigDisconnected_StatsKeepTheirValues()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, stats: true);
        st.Qsos!.Insert(Row("K1ABC", Now.AddHours(-1)));
        st.Radio(ServiceHealth.Ok);
        st.Service.Start();
        var before = await MqttStation.RetainedAsync(broker, st.Topics.Stats, Field("qsos_today", 1));

        st.Radio(ServiceHealth.Down);
        await MqttStation.RetainedAsync(broker, st.Topics.State, p => p.Contains("\"rig_connected\":false", StringComparison.Ordinal));
        st.Clock.Advance(TimeSpan.FromSeconds(HaDiscovery.HeartbeatSeconds + 1));
        await broker.WaitForAsync(m => m.Topic == st.Topics.Session && broker.Published.Count(x => x.Topic == st.Topics.Session) >= 2);

        var retained = await broker.RetainedAsync();
        retained[st.Topics.Stats].Should().Be(before);
        retained[st.Topics.Availability].Should().Be(MqttPayloads.Online);
        foreach (var topic in st.Topics.StatsTopics) retained.Should().ContainKey(topic);
    }

    [Fact]
    public async Task WeekWith250Stations_MapAttributesUnder8KB()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, stats: true);
        for (var i = 0; i < 250; i++) st.Qsos!.Insert(Row($"VP2V/K{i:D3}ABC", Now.AddMinutes(-i - 1), grid: "FK78"));
        st.Service.Start();

        var map = await MqttStation.RetainedAsync(broker, st.Topics.WorkedWeekMap, State(1));
        var recent = await MqttStation.RetainedAsync(broker, st.Topics.RecentQsos, p => p.Length > 0);

        StatsPayloads.AttributeBytes(map).Should().BeLessThanOrEqualTo(StatsPayloads.AttributeLimitBytes);
        StatsPayloads.AttributeBytes(recent).Should().BeLessThanOrEqualTo(StatsPayloads.AttributeLimitBytes);
        var points = J(map)["attributes"]!["points"]!.AsArray();
        points.Count.Should().BeInRange(40, StatsPayloads.MaxMapPoints);
        points[0]!["call"]!.GetValue<string>().Should().Be("VP2V/K000ABC", "the newest station is kept first");
    }

    [Fact]
    public async Task ContactsTurnedOff_NoStatsAndNoStatsEntities()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, stats: true, configure: s => s.Mqtt.PublishContacts = false);
        st.Qsos!.Insert(Row("K1ABC", Now.AddHours(-1)));
        st.Service.Start();

        var discovery = await MqttStation.RetainedAsync(broker, st.Topics.Discovery, p => p.Length > 0);

        discovery.Should().NotContain("qsos_today").And.Contain("session_active");
        var retained = await broker.RetainedAsync();
        foreach (var topic in st.Topics.StatsTopics) retained.Should().NotContainKey(topic);
    }

    [Fact]
    public async Task ContactsTurnedOffWhileRunning_StatsRemovedFromBrokerAndDevice()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, stats: true);
        st.Qsos!.Insert(Row("K1ABC", Now.AddHours(-1)));
        st.Service.Start();
        await MqttStation.RetainedAsync(broker, st.Topics.Stats, Field("qsos_today", 1));
        await MqttStation.RetainedAsync(broker, st.Topics.Discovery, p => p.Contains("qsos_today", StringComparison.Ordinal));

        st.Settings.Update(s => s.Mqtt.PublishContacts = false);

        await MqttStation.RetainedAsync(broker, st.Topics.Discovery, p => p.Length > 0 && !p.Contains("qsos_today", StringComparison.Ordinal));
        await MqttStation.Until(() => !broker.RetainedAsync().Result.Keys.Intersect(st.Topics.StatsTopics).Any(), "every statistics topic is cleared");

        st.Settings.Update(s => s.Mqtt.PublishContacts = true);

        await MqttStation.RetainedAsync(broker, st.Topics.Stats, Field("qsos_today", 1));
        await MqttStation.RetainedAsync(broker, st.Topics.Discovery, p => p.Contains("qsos_today", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QrzSync_DisabledPendingErrorOk()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, stats: true);
        st.Service.Start();
        await MqttStation.RetainedAsync(broker, st.Topics.QrzSync, p => p.Contains("\"state\":\"disabled\"", StringComparison.Ordinal));

        st.Secrets.Set(SecretNames.QrzLogbookKey("home"), "KEY");
        var id = st.Qsos!.Insert(Row("K1ABC", Now.AddHours(-1)));
        st.Uploads!.Enqueue(id, "qrz", Now);
        st.Qsos.SetUploadState(id, UploadState.Queued, null); // a log change, as the queueing write is
        var pending = J(await MqttStation.RetainedAsync(broker, st.Topics.QrzSync, p => p.Contains("\"state\":\"pending\"", StringComparison.Ordinal)));
        pending["attributes"]!["pending_count"]!.GetValue<int>().Should().Be(1);

        st.Session.SetService(ServiceNames.Qrz, new ServiceStatus(ServiceHealth.Down, "QRZ refused the logbook key", Now));
        var error = J(await MqttStation.RetainedAsync(broker, st.Topics.QrzSync, p => p.Contains("\"state\":\"error\"", StringComparison.Ordinal)));
        error["attributes"]!["last_error"]!.GetValue<string>().Should().Be("QRZ refused the logbook key");

        st.Sync!.Set("home", "qrz", Now, 5);
        st.Uploads.RemoveFor(id, "qrz");
        st.Qsos.SetUploadState(id, UploadState.Uploaded, null, 123);
        st.Session.SetService(ServiceNames.Qrz, new ServiceStatus(ServiceHealth.Ok, "Synced 1 records", Now));
        var ok = J(await MqttStation.RetainedAsync(broker, st.Topics.QrzSync, p => p.Contains("\"state\":\"ok\"", StringComparison.Ordinal)));
        ok["attributes"]!["last_sync_ts"]!.GetValue<string>().Should().Be("2026-11-24T20:15:00Z");
        ok["attributes"]!["pending_count"]!.GetValue<int>().Should().Be(0);
        var recent = J(await MqttStation.RetainedAsync(broker, st.Topics.RecentQsos, p => p.Contains("\"qrz_synced\":true", StringComparison.Ordinal)));
        recent["attributes"]!["qsos"]!.AsArray()[0]!["call"]!.GetValue<string>().Should().Be("K1ABC");
    }

    [Fact]
    public async Task PskSpots_LastFifteenMinutesOfMyPskReporterSpots()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, stats: true);
        SpotRecord Spot(string rx, string? grid, int minutesAgo, string source = "mqtt", string me = "W7LIT") =>
            new() { MyCall = me, RxCall = rx, RxGrid = grid, Band = "20m", Mode = "FT8", Snr = -10, TimeUtc = Now.AddMinutes(-minutesAgo), Source = source };
        st.Spots!.Add(Spot("K1ABC", "FN42", 2));
        st.Spots.Add(Spot("VK2XYZ", "QF56", 14));
        st.Spots.Add(Spot("JA1XYZ", null, 3));
        st.Spots.Add(Spot("ZL1OLD", "RF70", 20));                         // too old
        st.Spots.Add(Spot("DL1SKM", "JO62", 1, source: ReverseBeaconService.Source)); // RBN, not PSK Reporter
        st.Spots.Add(Spot("G4ABC", "IO91", 1, me: "K9XYZ"));               // someone else's signal
        st.Service.Start();

        var j = J(await MqttStation.RetainedAsync(broker, st.Topics.PskSpots, State(3)));

        j["attributes"]!["farthest_reporter"]!.GetValue<string>().Should().Be("VK2XYZ");
        j["attributes"]!["farthest_grid"]!.GetValue<string>().Should().Be("QF56");
        j["attributes"]!["farthest_km"]!.GetValue<int>().Should().BeGreaterThan(12000);

        st.Clock.Advance(TimeSpan.FromMinutes(14)); // the K1ABC and JA1XYZ spots are now over 15 minutes old

        await MqttStation.RetainedAsync(broker, st.Topics.PskSpots, State(0));
    }

    [Fact]
    public async Task SessionDuration_CountsMinutesThenZero()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var st = new MqttStation(broker.Port, stats: true);
        st.Service.Start();
        await MqttStation.RetainedAsync(broker, st.Topics.SessionDuration, State(0));

        await st.TuneAsync();
        st.Clock.Advance(TimeSpan.FromMinutes(3));

        await MqttStation.RetainedAsync(broker, st.Topics.SessionDuration, State(3));
        await st.Service.DisposeAsync();
        await MqttStation.RetainedAsync(broker, st.Topics.SessionDuration, State(0));
    }
}
