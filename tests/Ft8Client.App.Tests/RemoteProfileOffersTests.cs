// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.App.Engine;
using Ft8Client.App.Remote;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

public class RemoteProfileOffersTests
{
    private static readonly SessionSnapshot Idle = new() { MyCall = "K1GLV" };

    private static AppSettings Settings(string? linked)
    {
        var s = new AppSettings();
        s.Profile.Name = "Home";
        s.Profile.Callsign = "K1GLV";
        s.Profiles.Add(new StationProfile { Id = "ash", Name = "Ash", Callsign = "K1UTA" });
        s.Remote.Users.Add(new RemoteUser { Id = "u1", Username = "ash", ProfileId = linked });
        return s;
    }

    [Fact]
    public void For_LinkedToAnotherProfileIdle_OffersTheSwitch() =>
        RemoteProfileOffers.For(Settings("ash"), "u1", Idle)
            .Should().Be(new RemoteProfileOffer("K1UTA (Ash profile)", "K1UTA", "K1GLV (Home profile)", false, null));

    [Fact]
    public void For_LinkedToAnotherProfileBusy_SaysWhy() =>
        RemoteProfileOffers.For(Settings("ash"), "u1", Idle with { CallingCq = true })!.Busy
            .Should().Be("while calling CQ as K1GLV");

    [Fact]
    public void For_LinkedToTheProfileInUse_OnMine()
    {
        var s = Settings("ash");
        s.ActiveProfile = "ash";
        RemoteProfileOffers.For(s, "u1", Idle with { CallingCq = true })
            .Should().Be(new RemoteProfileOffer("K1UTA (Ash profile)", "K1UTA", "K1UTA (Ash profile)", true, null), "nothing to switch, so nothing is busy");
    }

    [Theory]
    [InlineData(null, "u1")]
    [InlineData("deleted", "u1")]
    [InlineData("ash", "someone-else")]
    [InlineData("ash", null)]
    public void For_NotLinkedOrUnknown_Null(string? linked, string? user) =>
        RemoteProfileOffers.For(Settings(linked), user, Idle).Should().BeNull();
}
