// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Remote;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

public class RemoteProfileNoteTests
{
    private static AppSettings Settings(string? linked)
    {
        var s = new AppSettings();
        s.Profile.Callsign = "K1GLV";
        s.Profiles.Add(new StationProfile { Id = "ash", Name = "Ash", Callsign = "K1UTA" });
        s.Remote.Users.Add(new RemoteUser { Id = "u1", Username = "ash", ProfileId = linked });
        return s;
    }

    [Fact]
    public void For_LinkedToAnotherProfile_SaysWhoTheyWillOperateAs() =>
        RemoteProfileNote.For(Settings("ash"), "u1")
            .Should().Be("You will operate as K1UTA (Ash profile) when you take control, if the station is idle.");

    [Fact]
    public void For_LinkedToTheProfileInUse_Null()
    {
        var s = Settings("ash");
        s.ActiveProfile = "ash";
        RemoteProfileNote.For(s, "u1").Should().BeNull();
    }

    [Theory]
    [InlineData(null, "u1")]
    [InlineData("deleted", "u1")]
    [InlineData("ash", "someone-else")]
    [InlineData("ash", null)]
    public void For_NotLinkedOrUnknown_Null(string? linked, string? user) =>
        RemoteProfileNote.For(Settings(linked), user).Should().BeNull();
}
