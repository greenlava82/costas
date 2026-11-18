// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Remote;
using Ft8Client.Core.Remote;
using Ft8Client.Core.Time;

namespace Ft8Client.App.Tests;

public class RemoteNoticesTests
{
    private static readonly DateTime T0 = new(2026, 11, 17, 21, 14, 0, DateTimeKind.Utc);

    [Fact]
    public void For_NoticeAboutThisDevice_NotShownToIt()
    {
        var n = new RemoteNotices();
        n.Add("Ash signed in on another device.", "ash-phone", T0);

        n.For("ash-phone", T0).Should().BeEmpty();
        n.For("dan-phone", T0).Should().ContainSingle().Which.Should().Match<RemoteNotice>(x => x.Text == "Ash signed in on another device." && x.Time == "21:14 UTC");
    }

    [Fact]
    public void For_TenMinutesOld_Gone()
    {
        var n = new RemoteNotices();
        n.Add("x", "a", T0);

        n.For("b", T0 + RemoteNotices.PhoneLifetime - TimeSpan.FromSeconds(1)).Should().HaveCount(1);
        n.For("b", T0 + RemoteNotices.PhoneLifetime).Should().BeEmpty();
    }

    [Fact]
    public void For_Several_NewestFirstWithRisingIds()
    {
        var n = new RemoteNotices();
        n.Add("first", "a", T0);
        n.Add("second", "a", T0);

        var list = n.For("b", T0);
        list.Select(x => x.Text).Should().Equal("second", "first");
        list[0].Id.Should().BeGreaterThan(list[1].Id);
    }

    [Fact]
    public void ForDesk_DesksOwnActionOrOlderThanAMinute_Null()
    {
        var n = new RemoteNotices();
        n.Add("The desk took control.", null, T0);
        n.ForDesk(T0).Should().BeNull("the desk did it");

        n.Add("Ash took control from the desk.", "ash-phone", T0);
        n.ForDesk(T0).Should().Be("Ash took control from the desk.");
        n.ForDesk(T0 + RemoteNotices.DeskLifetime).Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_PhoneTakesControlThenDesk_EveryoneElseTold()
    {
        var clock = new ManualClock(T0);
        var rc = new RemoteControl(new FakeRemoteTarget(), clock, new DeviceTokens(), _ => { });

        await rc.HandleAsync("phone", new RemoteCommand("call", "ve7xyz"));
        await rc.HandleAsync("phone", new RemoteCommand("call", "ve7xyz"));
        rc.DeskActivity();

        rc.Notices.For("tablet", T0).Select(x => x.Text).Should().Equal("The desk took control.", "A phone took control from the desk.");
        rc.Notices.For("phone", T0).Select(x => x.Text).Should().Equal(["The desk took control."], "a second command from the holder is not news");
    }
}
