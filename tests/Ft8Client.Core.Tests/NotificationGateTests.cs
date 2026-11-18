// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Notifications;

namespace Ft8Client.Core.Tests;

public class NotificationGateTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);

    private static AttentionEvent Hunt(string call, string band = "20 m") =>
        new(AttentionKind.Hunt, call, $"{call} on {band} FT8", "New country, heard here at -12 dB.", $"{call} on {band} FT8");

    private static AttentionEvent Caller(string call) => new(AttentionKind.Caller, call, $"{call} is calling you", "20 m FT8 · -12 dB", call);

    [Fact]
    public void Offer_Background_PopsUp()
    {
        var n = new NotificationGate().Offer(Hunt("VP8PJ"), inForeground: false, Now);

        n.Should().Be(new DesktopNotification(AttentionKind.Hunt, "VP8PJ on 20 m FT8", "New country, heard here at -12 dB.", "VP8PJ", Popup: true));
    }

    [Fact]
    public void Offer_ForegroundAndBackgroundOnly_Nothing()
    {
        new NotificationGate().Offer(Hunt("VP8PJ"), inForeground: true, Now).Should().BeNull();
    }

    [Fact]
    public void Offer_ForegroundAndAlways_PopsUp()
    {
        var gate = new NotificationGate { Policy = new NotificationPolicy(OnlyInBackground: false) };

        gate.Offer(Hunt("VP8PJ"), inForeground: true, Now)!.Popup.Should().BeTrue();
    }

    [Fact]
    public void Offer_ForegroundSuppressed_DoesNotCountAsNotified()
    {
        var gate = new NotificationGate();
        gate.Offer(Hunt("VP8PJ"), inForeground: true, Now).Should().BeNull();

        gate.Offer(Hunt("VP8PJ"), inForeground: false, Now.AddSeconds(5))!.Popup.Should().BeTrue();
    }

    [Fact]
    public void Offer_NotificationsOff_Nothing()
    {
        var gate = new NotificationGate { Policy = new NotificationPolicy(Enabled: false) };

        gate.Offer(Hunt("VP8PJ"), false, Now).Should().BeNull();
        gate.Offer(Caller("W1ABC"), false, Now).Should().BeNull();
    }

    [Fact]
    public void Offer_KindOff_OnlyThatKindSilenced()
    {
        var gate = new NotificationGate { Policy = new NotificationPolicy(Hunt: false) };

        gate.Offer(Hunt("VP8PJ"), false, Now).Should().BeNull();
        gate.Offer(Caller("W1ABC"), false, Now).Should().NotBeNull();

        gate.Policy = new NotificationPolicy(Callers: false);
        gate.Offer(Caller("K2DEF"), false, Now).Should().BeNull();
        gate.Offer(Hunt("KL7RA"), false, Now).Should().NotBeNull();
    }

    [Fact]
    public void Offer_SameStationWithinQuietTime_Nothing()
    {
        var gate = new NotificationGate();
        gate.Offer(Hunt("VP8PJ"), false, Now);

        gate.Offer(Hunt("VP8PJ", "17 m"), false, Now.AddMinutes(2)).Should().BeNull();
        gate.Offer(Hunt("VP8PJ"), false, Now.Add(NotificationGate.RepeatQuiet).AddSeconds(-1)).Should().BeNull();
    }

    [Fact]
    public void Offer_SameStationAfterQuietTime_PopsUpAgain()
    {
        var gate = new NotificationGate();
        gate.Offer(Hunt("VP8PJ"), false, Now);

        gate.Offer(Hunt("VP8PJ"), false, Now.Add(NotificationGate.RepeatQuiet))!.Popup.Should().BeTrue();
    }

    [Fact]
    public void Offer_SameStationOtherKind_Notifies()
    {
        var gate = new NotificationGate();
        gate.Offer(Hunt("VP8PJ"), false, Now);

        gate.Offer(Caller("VP8PJ"), false, Now.AddSeconds(15))!.Popup.Should().BeTrue();
    }

    [Fact]
    public void Offer_OthersWithinMergeWindow_MergedQuietly()
    {
        var gate = new NotificationGate();
        gate.Offer(Hunt("VP8PJ"), false, Now);

        var second = gate.Offer(Hunt("KL7RA", "40 m"), false, Now.AddSeconds(15))!;
        var third = gate.Offer(Hunt("VK9XA", "17 m"), false, Now.AddSeconds(30))!;

        second.Popup.Should().BeFalse();
        second.Title.Should().Be("2 Hunt list stations heard");
        third.Should().Be(new DesktopNotification(AttentionKind.Hunt, "3 Hunt list stations heard",
            "VP8PJ on 20 m FT8, KL7RA on 40 m FT8, VK9XA on 17 m FT8.", "VK9XA", Popup: false));
    }

    [Fact]
    public void Offer_ManyCallersWithinMergeWindow_NamesFourAndCountsTheRest()
    {
        var gate = new NotificationGate();
        DesktopNotification? last = null;
        foreach (var (call, i) in new[] { "W1AA", "W2BB", "W3CC", "W4DD", "W5EE", "W6FF" }.Select((c, i) => (c, i)))
            last = gate.Offer(Caller(call), false, Now.AddSeconds(i * 7.5));

        last!.Title.Should().Be("6 stations are calling you");
        last.Body.Should().Be("W1AA, W2BB, W3CC, W4DD and 2 more.");
        last.Call.Should().Be("W6FF");
        last.Popup.Should().BeFalse();
    }

    [Fact]
    public void Offer_AfterMergeWindow_PopsUpFresh()
    {
        var gate = new NotificationGate();
        gate.Offer(Hunt("VP8PJ"), false, Now);
        gate.Offer(Hunt("KL7RA"), false, Now.AddSeconds(30));

        var n = gate.Offer(Hunt("VK9XA"), false, Now.Add(NotificationGate.MergeWindow))!;

        n.Popup.Should().BeTrue();
        n.Title.Should().Be("VK9XA on 20 m FT8");
    }

    [Fact]
    public void Offer_KindsMergeSeparately()
    {
        var gate = new NotificationGate();
        gate.Offer(Hunt("VP8PJ"), false, Now);

        var caller = gate.Offer(Caller("W1ABC"), false, Now.AddSeconds(10))!;

        caller.Popup.Should().BeTrue();
        caller.Kind.Should().Be(AttentionKind.Caller);
    }

    [Fact]
    public void Offer_RepeatInsideMergeWindow_DoesNotGrowMerge()
    {
        var gate = new NotificationGate();
        gate.Offer(Caller("W1ABC"), false, Now);
        gate.Offer(Caller("K2DEF"), false, Now.AddSeconds(15));

        gate.Offer(Caller("W1ABC"), false, Now.AddSeconds(30)).Should().BeNull();
        gate.Offer(Caller("N3GHI"), false, Now.AddSeconds(45))!.Title.Should().Be("3 stations are calling you");
    }
}
