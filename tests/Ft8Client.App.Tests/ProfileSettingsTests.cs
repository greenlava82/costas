// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.App.ViewModels;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

public class ProfileSettingsTests
{
    private static AppSettings Home()
    {
        var s = new AppSettings();
        s.Profile.Callsign = "N0CALL";
        s.Profile.Grid = "DN40";
        s.Profile.Rig.Port = "COM3";
        return s;
    }

    [Fact]
    public void ApplyProfiles_NewDraft_CopiesRadioFromSourceAndBecomesActive()
    {
        var s = Home();
        var drafts = new List<ProfileDraft>
        {
            new() { Id = "home", Name = "Home", Callsign = "n0call", Grid = "dn40", Power = "100" },
            new() { Id = "p1", CopyOf = "home", Name = " Park ", Callsign = "n0call/p", Grid = "dn41", Power = "10", EqslUpload = true, EqslQth = " Park " },
        };

        SettingsViewModel.ApplyProfiles(s, drafts, "p1");

        s.Profiles.Should().HaveCount(2);
        s.ActiveProfile.Should().Be("p1");
        s.Profile.Name.Should().Be("Park");
        s.Profile.Callsign.Should().Be("N0CALL/P");
        s.Profile.Grid.Should().Be("DN41");
        s.Profile.PowerWatts.Should().Be(10);
        s.Profile.Rig.Port.Should().Be("COM3");
        s.Profiles[0].PowerWatts.Should().Be(100);
        s.Profile.Eqsl.UploadOnComplete.Should().BeTrue();
        s.Profile.Eqsl.QthNickname.Should().Be("Park");
        s.Profiles[0].Eqsl.UploadOnComplete.Should().BeFalse();
    }

    [Fact]
    public void ApplyProfiles_LotwPerDraft_EachProfileKeepsItsOwnAccount()
    {
        var s = Home();
        s.Profile.Lotw.Username = "N0CALL";
        s.Profile.Lotw.Enabled = true;
        var drafts = new List<ProfileDraft>
        {
            new() { Id = "home", Name = "Home", Callsign = "N0CALL", Grid = "DN40", Power = "100",
                    LotwEnabled = true, LotwUser = "N0CALL", LotwStationLocation = "Home", LotwUpload = true },
            new() { Id = "p2", CopyOf = "home", Name = "Guest", Callsign = "K0GST", Grid = "DN40", Power = "100",
                    LotwEnabled = true, LotwUser = " k0gst ", LotwStationLocation = " Guest QTH " },
            new() { Id = "p3", CopyOf = "home", Name = "Fresh", Callsign = "K0NEW", Grid = "DN40", Power = "100" },
        };

        SettingsViewModel.ApplyProfiles(s, drafts, "p2");

        s.Profiles[0].Lotw.Username.Should().Be("N0CALL");
        s.Profiles[0].Lotw.StationLocation.Should().Be("Home");
        s.Profiles[0].Lotw.UploadOnComplete.Should().BeTrue();
        s.Profile.Lotw.Username.Should().Be("k0gst");
        s.Profile.Lotw.StationLocation.Should().Be("Guest QTH");
        s.Profile.Lotw.UploadOnComplete.Should().BeFalse();
        s.Profiles[2].Lotw.Enabled.Should().BeFalse("a new profile does not take over the account it was copied from");
        s.Profiles[2].Lotw.Username.Should().BeNull();
    }

    [Fact]
    public void ApplyProfiles_NewProfiles_CopyKeepsExchangesNewOperatorStartsWithNone()
    {
        var s = Home();
        s.Profile.ContestExchanges["FieldDay"] = "2A ID";
        var drafts = new List<ProfileDraft>
        {
            new() { Id = "home", Name = "Home", Callsign = "N0CALL", Grid = "DN40", Power = "100" },
            new() { Id = "p1", CopyOf = "home", Name = "Portable", Callsign = "N0CALL/P", Grid = "DN41", Power = "10" },
            new() { Id = "p2", CopyOf = "home", ForAnotherOperator = true, Name = "Operator", Callsign = "K0GST", Grid = "DN40", Power = "100" },
        };

        SettingsViewModel.ApplyProfiles(s, drafts, "home");

        s.Profiles[1].ContestExchanges.Should().ContainKey("FieldDay", "a portable setup is the same operator");
        s.Profiles[2].ContestExchanges.Should().BeEmpty("another operator gives their own");
        s.Profiles[2].Rig.Port.Should().Be("COM3");
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("FieldDay", "2A ID", null)]
    [InlineData("FieldDay", null, Core.Contests.ContestId.FieldDay)]
    [InlineData("RttyRoundup", "ZZ", Core.Contests.ContestId.RttyRoundup)]
    [InlineData("WwDigi", null, null)]
    public void ExchangeMissing_ActiveProfile_OnlyWhenTheRunningContestCannotBeSent(string contest, string? exchange, Core.Contests.ContestId? missing)
    {
        var s = Home();
        s.Operating.Contest = contest;
        if (exchange is not null) s.Profile.ContestExchanges[contest] = exchange;
        MainViewModel.ExchangeMissing(s).Should().Be(missing);
    }

    [Fact]
    public void NewProfileName_OperatorBase_NumbersFromTwo()
    {
        SettingsViewModel.NewProfileName(["Home"], "Operator").Should().Be("Operator");
        SettingsViewModel.NewProfileName(["Home", "operator"], "Operator").Should().Be("Operator 2");
    }

    [Fact]
    public void ApplyProfiles_DraftRemoved_DropsProfile()
    {
        var s = Home();
        s.Profiles.Add(s.Profile.CopyAs("p1", "Park"));

        SettingsViewModel.ApplyProfiles(s, [new ProfileDraft { Id = "p1", Name = "Park", Callsign = "N0CALL", Grid = "DN40", Power = "x" }], "p1");

        s.Profiles.Select(p => p.Id).Should().Equal("p1");
        s.Profile.PowerWatts.Should().Be(25);
    }

    [Fact]
    public void CopyAs_ChangingCopy_LeavesOriginal()
    {
        var home = Home().Profile;
        var copy = home.CopyAs("p1", "Park");
        copy.Rig.Port = "COM4";
        home.Rig.Port.Should().Be("COM3");
        copy.Id.Should().Be("p1");
    }

    [Fact]
    public void NewProfileName_Taken_Numbers()
    {
        SettingsViewModel.NewProfileName(["Home"]).Should().Be("Portable");
        SettingsViewModel.NewProfileName(["Home", "portable", "Portable 2"]).Should().Be("Portable 3");
    }
}
