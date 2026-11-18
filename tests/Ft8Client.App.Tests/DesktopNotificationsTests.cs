// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Services.Notifications;
using Ft8Client.Core.Notifications;
using Ft8Client.Core.Time;

namespace Ft8Client.App.Tests;

public class DesktopNotificationsTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);

    private static AttentionEvent Hunt(string call) => new(AttentionKind.Hunt, call, $"{call} on 20 m FT8", "New country, heard here at -12 dB.", $"{call} on 20 m FT8");

    private sealed class RecordingNotifier : IDesktopNotifier
    {
        public List<DesktopNotification> Shown { get; } = [];

        public string Name => "Test";

        public event Action<string>? Activated;

        public void Show(DesktopNotification n) => Shown.Add(n);

        public void Click(string call) => Activated?.Invoke(call);

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Raise_NoBackendChosen_ShowsNothingAndDoesNotThrow()
    {
        var svc = new DesktopNotifications(() => new NotificationPolicy(), new ManualClock(T0));

        svc.Raise(Hunt("VP8PJ")).Should().NotBeNull();
        svc.Backend.Should().BeOfType<NullDesktopNotifier>();
    }

    [Fact]
    public void Raise_InBackground_ShowsOnBackend()
    {
        var backend = new RecordingNotifier();
        var svc = new DesktopNotifications(() => new NotificationPolicy(), new ManualClock(T0)) { InForeground = () => false };
        svc.UseBackend(backend);

        svc.Raise(Hunt("VP8PJ"));

        backend.Shown.Should().ContainSingle().Which.Title.Should().Be("VP8PJ on 20 m FT8");
    }

    [Fact]
    public void Raise_InForeground_ShowsNothing()
    {
        var backend = new RecordingNotifier();
        var svc = new DesktopNotifications(() => new NotificationPolicy(), new ManualClock(T0)) { InForeground = () => true };
        svc.UseBackend(backend);

        svc.Raise(Hunt("VP8PJ")).Should().BeNull();
        backend.Shown.Should().BeEmpty();
    }

    [Fact]
    public void Raise_SettingChangedBetweenEvents_FollowsTheNewSetting()
    {
        var policy = new NotificationPolicy();
        var backend = new RecordingNotifier();
        var svc = new DesktopNotifications(() => policy, new ManualClock(T0));
        svc.UseBackend(backend);

        policy = policy with { Enabled = false };
        svc.Raise(Hunt("VP8PJ"));

        backend.Shown.Should().BeEmpty();
    }

    [Fact]
    public void Raise_ClockMoves_MergesThenPopsUpAgain()
    {
        var clock = new ManualClock(T0);
        var backend = new RecordingNotifier();
        var svc = new DesktopNotifications(() => new NotificationPolicy(), clock);
        svc.UseBackend(backend);

        svc.Raise(Hunt("VP8PJ"));
        clock.Advance(TimeSpan.FromSeconds(20));
        svc.Raise(Hunt("KL7RA"));
        clock.Advance(NotificationGate.MergeWindow);
        svc.Raise(Hunt("VK9XA"));

        backend.Shown.Select(n => n.Popup).Should().Equal(true, false, true);
    }

    [Fact]
    public void Activated_BackendClicked_PassesTheCallOn()
    {
        var backend = new RecordingNotifier();
        var svc = new DesktopNotifications(() => new NotificationPolicy(), new ManualClock(T0));
        svc.UseBackend(backend);
        string? clicked = null;
        svc.Activated += c => clicked = c;

        backend.Click("VP8PJ");

        clicked.Should().Be("VP8PJ");
    }

    [Fact]
    public void UseBackend_Replaced_OldBackendNoLongerForwardsClicks()
    {
        var first = new RecordingNotifier();
        var svc = new DesktopNotifications(() => new NotificationPolicy(), new ManualClock(T0));
        svc.UseBackend(first);
        svc.UseBackend(new RecordingNotifier());
        var clicks = 0;
        svc.Activated += _ => clicks++;

        first.Click("VP8PJ");

        clicks.Should().Be(0);
    }
}
