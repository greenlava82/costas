// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Contacts;
using Ft8Client.Core.Hunting;
using Ft8Client.Core.Messages;
using Ft8Client.Core.Notifications;
using Ft8Client.Core.Ranking;
using Ft8Client.Core.Time;

namespace Ft8Client.Core.Tests;

public class NotificationTextTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);

    private static HuntCandidate Heard(string call, string band = "17m", string mode = "FT8", string? rx = null, double? km = null) =>
        new(call, TestData.Countries.Lookup(call)?.Entity.Key, null, null, band, mode, -14, Now, rx, km);

    [Fact]
    public void Hunt_CountryHeardNearby_TitleNamesStationAndText()
    {
        var s = new HuntSighting(new HuntTarget(HuntTargetKind.Country, "VK9X", null), Heard("VK9XA", rx: "W7GJ", km: 482.8));

        var e = NotificationText.Hunt(s, TestData.Countries, DistanceUnit.Miles);

        e.Should().Be(new AttentionEvent(AttentionKind.Hunt, "VK9XA", "VK9XA on 17 m FT8",
            "Christmas Island (VK9X), heard 300 mi from you at -14 dB.", "VK9XA on 17 m FT8"));
    }

    [Fact]
    public void Hunt_CallHeardHere_SaysOnYourHuntList()
    {
        var s = new HuntSighting(new HuntTarget(HuntTargetKind.Call, "VK9XA", null), Heard("vk9xa", "20m", "FT4"));

        var e = NotificationText.Hunt(s, TestData.Countries, DistanceUnit.Miles);

        e.Call.Should().Be("VK9XA");
        e.Title.Should().Be("VK9XA on 20 m FT4");
        e.Body.Should().Be("On your Hunt list, heard here at -14 dB.");
    }

    [Fact]
    public void Hunt_NewCountry_SaysNewCountry()
    {
        var s = new HuntSighting(new HuntTarget(HuntTargetKind.NewCountry, "", null), Heard("VK9XA"));

        NotificationText.Hunt(s, TestData.Countries, DistanceUnit.Miles).Body.Should().Be("New country Christmas Island, heard here at -14 dB.");
    }

    [Fact]
    public void Caller_New_SaysCallingYouWithMessage()
    {
        var w = new WaitingCaller("w1abc", "FN31", -9, MessageKind.GridReply, null, SlotParity.Even, 1200, Now, Now, LastMessage: "W7LIT W1ABC FN31");

        var e = NotificationText.Caller(w, "20m", "FT8");

        e.Should().Be(new AttentionEvent(AttentionKind.Caller, "W1ABC", "W1ABC is calling you", "20 m FT8 · -9 dB · W7LIT W1ABC FN31", "W1ABC"));
    }

    [Fact]
    public void Caller_AfterStop_SaysRepliedAfterYouStopped()
    {
        var w = new WaitingCaller("W1ABC", null, 3, MessageKind.Report, -10, SlotParity.Odd, 900, Now, Now, AfterStop: true);

        var e = NotificationText.Caller(w, "40m", "FT4");

        e.Title.Should().Be("W1ABC replied after you stopped");
        e.Body.Should().Be("40 m FT4 · +3 dB");
    }
}
