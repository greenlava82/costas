// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ft8Client.Core.Mqtt;

namespace Ft8Client.Core.Tests.Mqtt;

/// <summary>
/// The Home Assistant discovery message against what Costas really publishes: every entity reads a topic Costas
/// publishes, every field its template reads is in that topic's payload, enum sensors list every value Costas can
/// send, binary sensors' on and off match the payloads, and event entities declare the event types sent.
/// A mismatch would show as "unknown" in Home Assistant with no error anywhere.
/// </summary>
public partial class HaDiscoveryContractTests
{
    private static readonly MqttTopics Topics = new("w7lit");
    private static readonly DateTime At = new(2026, 11, 24, 20, 15, 0, DateTimeKind.Utc);

    private static readonly StatsContact Contact = new("KP4JRS", "20m", "FT8", 14_074_500, At, "Puerto Rico", 202, null, "FK68", 18.5, -65.0,
        5000, true, "-10", "-12", false);

    private static JsonObject Components() =>
        JsonNode.Parse(HaDiscovery.Build(Topics, "W7LIT", "0.1.0"))!["cmps"]!.AsObject();

    // Every payload Costas can publish on each topic.
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Payloads()
    {
        var running = new OnAirSnapshot(true, "abc123", At, null, true);
        var sessions = new List<OnAirSnapshot> { OnAirSnapshot.Idle, running, running with { Transmitting = false } };
        sessions.AddRange(Enum.GetValues<SessionEndReason>().Select(r => running with { Active = false, Transmitting = false, EndReason = r }));
        var empty = new DashboardStats(At.Date, 0, 0, 0, 0, [], null, [],
            [.. Enumerable.Range(0, 7).Select(i => new DayCount(At.Date.AddDays(i - 6), 0, []))], 0, []);
        var busy = new DashboardStats(At.Date, 2, 5, 900, 120, [new NewEntity(202, "Puerto Rico", "KP4JRS", At)], Contact, [Contact],
            [.. Enumerable.Range(0, 7).Select(i => new DayCount(At.Date.AddDays(i - 6), 1, [new("20m", 1)]))], 4,
            [new MapPoint("KP4JRS", 18.2, -66.5, "Puerto Rico", "20m", At)]);
        var stats = new[] { empty, busy };
        var states = Enum.GetValues<StationActivity>()
            .SelectMany(a => new[] { new StationState("W7LIT", true, a, "20m", 14_074_000, "FT8"), new StationState("W7LIT", false, a, "20m", 14_074_000, "FT8") })
            .ToList();
        return new Dictionary<string, IReadOnlyList<string>>
        {
            [Topics.Session] = [.. sessions.Select(MqttPayloads.Session)],
            [Topics.Tx] = [MqttPayloads.Tx(true), MqttPayloads.Tx(false)],
            [Topics.State] = [.. states.Select(MqttPayloads.State)],
            [Topics.SessionEvent] = [.. sessions.Skip(1).Select(MqttPayloads.SessionEvent)],
            [Topics.QsoEvent] =
            [
                MqttPayloads.QsoEvent(new LoggedQso("KP4JRS", "20m", "FT8", 14_074_000, At)),
                StatsPayloads.ContactEvent("qso_logged", new LoggedQso("KP4JRS", "20m", "FT8", 14_074_000, At), null),
                StatsPayloads.ContactEvent("new_dxcc", new LoggedQso("KP4JRS", "20m", "FT8", 14_074_000, At), Contact),
            ],
            [Topics.Stats] = [.. stats.Select(StatsPayloads.Counts)],
            [Topics.NewDxccToday] = [.. stats.Select(StatsPayloads.NewDxccToday)],
            [Topics.FarthestToday] = [.. stats.Select(StatsPayloads.FarthestToday)],
            [Topics.SessionDuration] = [.. sessions.Select(x => StatsPayloads.SessionDuration(x, At.AddMinutes(42)))],
            [Topics.RecentQsos] = [.. stats.Select(StatsPayloads.RecentQsos)],
            [Topics.DailyQsos] = [.. stats.Select(StatsPayloads.DailyQsos)],
            [Topics.WorkedWeekMap] = [.. stats.Select(StatsPayloads.WorkedWeekMap)],
            [Topics.QrzSync] = [.. Enum.GetValues<QrzSyncState>().Select(q => StatsPayloads.QrzSync(new QrzSyncStatus(q, null, 0, null)))],
            [Topics.PskSpots] = [StatsPayloads.PskSpots(PskSpotSummary.None), StatsPayloads.PskSpots(new PskSpotSummary(3, 1200, "K1ABC", "FN42"))],
        };
    }

    [GeneratedRegex(@"^\{\{ value_json\.(\w+) \}\}$")]
    private static partial Regex Simple();

    private static string Scalar(JsonNode v) => v.GetValueKind() == System.Text.Json.JsonValueKind.String ? v.GetValue<string>() : v.ToJsonString();

    [GeneratedRegex(@"value_json\.(\w+)")]
    private static partial Regex Field();

    // What Home Assistant's template gives for a payload, for the simple templates Costas uses.
    private static string Render(string? template, string payload)
    {
        if (template is null) return payload;
        var json = JsonNode.Parse(payload)!.AsObject();
        return template switch
        {
            "{{ value_json.state }}" => Scalar(json["state"]!),
            "{{ value_json.band }}" => json["band"]!.GetValue<string>(),
            "{{ value_json.mode }}" => json["mode"]!.GetValue<string>(),
            "{{ value_json.frequency }}" => json["frequency"]!.ToJsonString(),
            "{{ 'ON' if value_json.rig_connected else 'OFF' }}" => json["rig_connected"]!.GetValue<bool>() ? "ON" : "OFF",
            "{{ value_json.reason if value_json.reason else 'none' }}" => json["reason"]?.GetValue<string>() ?? "none",
            HaDiscovery.NullableStateTemplate => json["state"] is { } v ? Scalar(v) : "None",
            _ when Simple().Match(template) is { Success: true } m => Scalar(json[m.Groups[1].Value]!),
            _ => throw new InvalidOperationException($"No rendering for template {template}; add one so it is checked."),
        };
    }

    [Fact]
    public void Build_EveryEntity_ReadsATopicCostasPublishes()
    {
        var payloads = Payloads();
        foreach (var (key, c) in Components())
            payloads.Should().ContainKey(c!["state_topic"]!.GetValue<string>(), $"{key} reads a topic nothing publishes otherwise");
    }

    [Fact]
    public void Build_EveryTemplateField_InEveryPayloadOfItsTopic()
    {
        var payloads = Payloads();
        foreach (var (key, c) in Components())
        {
            if (c!["value_template"]?.GetValue<string>() is not { } template) continue;
            foreach (var payload in payloads[c["state_topic"]!.GetValue<string>()])
            {
                var json = JsonNode.Parse(payload)!.AsObject();
                foreach (Match m in Field().Matches(template))
                    json.ContainsKey(m.Groups[1].Value).Should().BeTrue($"{key} reads value_json.{m.Groups[1].Value} from {payload}");
            }
        }
    }

    [Fact]
    public void Build_EnumSensors_ListEveryValueCostasSends()
    {
        var payloads = Payloads();
        foreach (var (key, c) in Components())
        {
            if (c!["device_class"]?.GetValue<string>() != "enum") continue;
            var options = c["options"]!.AsArray().Select(o => o!.GetValue<string>()).ToList();
            var template = c["value_template"]?.GetValue<string>();
            foreach (var payload in payloads[c["state_topic"]!.GetValue<string>()])
                options.Should().Contain(Render(template, payload), $"{key} would show unknown for {payload}");
        }
    }

    [Fact]
    public void Build_BinarySensors_OnAndOffMatchWhatIsSent()
    {
        var payloads = Payloads();
        foreach (var (key, c) in Components())
        {
            if (c!["p"]!.GetValue<string>() != "binary_sensor") continue;
            var allowed = new[] { c["payload_on"]!.GetValue<string>(), c["payload_off"]!.GetValue<string>() };
            var rendered = payloads[c["state_topic"]!.GetValue<string>()].Select(p => Render(c["value_template"]?.GetValue<string>(), p)).ToList();
            rendered.Should().OnlyContain(v => allowed.Contains(v), $"{key} must be on or off, never unknown");
            rendered.Distinct().Should().HaveCount(2, $"{key} is seen both on and off");
        }
    }

    [Fact]
    public void Build_EventEntities_DeclareEveryEventTypeSent()
    {
        var payloads = Payloads();
        foreach (var (key, c) in Components())
        {
            if (c!["p"]!.GetValue<string>() != "event") continue;
            var types = c["event_types"]!.AsArray().Select(t => t!.GetValue<string>()).ToList();
            foreach (var payload in payloads[c["state_topic"]!.GetValue<string>()])
                types.Should().Contain(JsonNode.Parse(payload)!["event_type"]!.GetValue<string>(), $"{key} drops events it does not declare");
        }
    }

    [Fact]
    public void Build_AttributeTemplates_ReadAnAttributesObjectUnder8KB()
    {
        var payloads = Payloads();
        foreach (var (key, c) in Components())
        {
            if (c!["json_attributes_topic"]?.GetValue<string>() is not { } topic) continue;
            c["json_attributes_template"]!.GetValue<string>().Should().Be(HaDiscovery.AttributesTemplate);
            topic.Should().Be(c["state_topic"]!.GetValue<string>());
            foreach (var payload in payloads[topic])
            {
                JsonNode.Parse(payload)!["attributes"].Should().BeOfType<JsonObject>($"{key} reads attributes from {payload}");
                StatsPayloads.AttributeBytes(payload).Should().BeLessThanOrEqualTo(StatsPayloads.AttributeLimitBytes);
            }
        }
    }

    [Fact]
    public void Build_NumericSensors_RenderANumberOrNone()
    {
        var payloads = Payloads();
        foreach (var (key, c) in Components())
        {
            if (c!["state_class"] is null) continue;
            foreach (var payload in payloads[c["state_topic"]!.GetValue<string>()])
            {
                var v = Render(c["value_template"]!.GetValue<string>(), payload);
                (v == "None" || double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out _)).Should().BeTrue($"{key} renders {v} from {payload}");
            }
        }
    }

    [Fact]
    public void Build_TimestampSensor_RendersIsoUtcOrNone()
    {
        var c = Components()["recent_qsos"]!;
        var rendered = Payloads()[c["state_topic"]!.GetValue<string>()].Select(p => Render(c["value_template"]!.GetValue<string>(), p)).ToList();

        rendered.Should().Contain("None").And.Contain("2026-11-24T20:15:00Z");
    }

    [Fact]
    public void Build_FrequencySensor_NumericHertz()
    {
        var f = Components()["frequency"]!;
        Render(f["value_template"]!.GetValue<string>(), Payloads()[Topics.State][0]).Should().Be("14074000");
        f["unit_of_measurement"]!.GetValue<string>().Should().Be("Hz");
    }

    [Fact]
    public void Build_Availability_MatchesTheLastWillAndOnline()
    {
        var root = JsonNode.Parse(HaDiscovery.Build(Topics, "W7LIT", "0.1.0"))!;

        root["availability_topic"]!.GetValue<string>().Should().Be(Topics.Availability);
        root["payload_available"]!.GetValue<string>().Should().Be(MqttPayloads.Online);
        root["payload_not_available"]!.GetValue<string>().Should().Be(MqttPayloads.Offline);
    }
}
