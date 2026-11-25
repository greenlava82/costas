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

    private static JsonObject Components() =>
        JsonNode.Parse(HaDiscovery.Build(Topics, "W7LIT", "0.1.0"))!["cmps"]!.AsObject();

    // Every payload Costas can publish on each topic.
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Payloads()
    {
        var running = new OnAirSnapshot(true, "abc123", At, null, true);
        var sessions = new List<OnAirSnapshot> { OnAirSnapshot.Idle, running, running with { Transmitting = false } };
        sessions.AddRange(Enum.GetValues<SessionEndReason>().Select(r => running with { Active = false, Transmitting = false, EndReason = r }));
        var states = Enum.GetValues<StationActivity>()
            .SelectMany(a => new[] { new StationState("W7LIT", true, a, "20m", 14_074_000, "FT8"), new StationState("W7LIT", false, a, "20m", 14_074_000, "FT8") })
            .ToList();
        return new Dictionary<string, IReadOnlyList<string>>
        {
            [Topics.Session] = [.. sessions.Select(MqttPayloads.Session)],
            [Topics.Tx] = [MqttPayloads.Tx(true), MqttPayloads.Tx(false)],
            [Topics.State] = [.. states.Select(MqttPayloads.State)],
            [Topics.SessionEvent] = [.. sessions.Skip(1).Select(MqttPayloads.SessionEvent)],
            [Topics.QsoEvent] = [MqttPayloads.QsoEvent(new LoggedQso("KP4JRS", "20m", "FT8", 14_074_000, At))],
        };
    }

    [GeneratedRegex(@"value_json\.(\w+)")]
    private static partial Regex Field();

    // What Home Assistant's template gives for a payload, for the simple templates Costas uses.
    private static string Render(string? template, string payload)
    {
        if (template is null) return payload;
        var json = JsonNode.Parse(payload)!.AsObject();
        return template switch
        {
            "{{ value_json.state }}" => json["state"]!.GetValue<string>(),
            "{{ value_json.band }}" => json["band"]!.GetValue<string>(),
            "{{ value_json.mode }}" => json["mode"]!.GetValue<string>(),
            "{{ value_json.frequency }}" => json["frequency"]!.ToJsonString(),
            "{{ 'ON' if value_json.rig_connected else 'OFF' }}" => json["rig_connected"]!.GetValue<bool>() ? "ON" : "OFF",
            "{{ value_json.reason if value_json.reason else 'none' }}" => json["reason"]?.GetValue<string>() ?? "none",
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
