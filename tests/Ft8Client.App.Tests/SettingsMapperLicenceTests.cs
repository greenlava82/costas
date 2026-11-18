// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.Core.Bands;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

public class SettingsMapperLicenceTests
{
    [Fact]
    public void Licence_DefaultSettings_AmateurExtraAsBefore() =>
        SettingsMapper.Licence(new AppSettings()).Should().Be(UsLicenceClass.AmateurExtra);

    [Fact]
    public void Licence_NotChosenAndOldSwitchOff_NotUs()
    {
        var s = new AppSettings();
        s.Operating.UsDataSegmentsOnly = false;
        SettingsMapper.Licence(s).Should().Be(UsLicenceClass.NotUs);
    }

    [Theory]
    [InlineData("extra", UsLicenceClass.AmateurExtra)]
    [InlineData("advanced", UsLicenceClass.Advanced)]
    [InlineData("General", UsLicenceClass.General)]
    [InlineData("technician", UsLicenceClass.Technician)]
    [InlineData("none", UsLicenceClass.NotUs)]
    [InlineData("novice", UsLicenceClass.AmateurExtra)]
    public void Licence_Stored_ReadBack(string stored, UsLicenceClass expected)
    {
        var s = new AppSettings();
        s.Operating.UsDataSegmentsOnly = false; // a chosen class wins over the old switch
        s.Profile.LicenceClass = stored;
        SettingsMapper.Licence(s).Should().Be(expected);
    }

    [Fact]
    public void Licence_PerProfile_FollowsTheActiveProfile()
    {
        var s = new AppSettings();
        s.Profile.LicenceClass = "general";
        var tech = s.Profile.CopyAs("p2", "Club");
        tech.LicenceClass = "technician";
        s.Profiles.Add(tech);
        s.ActiveProfile = "p2";
        SettingsMapper.Licence(s).Should().Be(UsLicenceClass.Technician);
        s.ActiveProfile = "home";
        SettingsMapper.Licence(s).Should().Be(UsLicenceClass.General);
    }

    [Fact]
    public void ApplyProfiles_LicenceChosen_StoredOnTheProfile()
    {
        var s = new AppSettings();
        var drafts = new[] { new ProfileDraft { Id = "home", Name = "Home", Callsign = "K1ABC", Licence = UsLicenceClass.Technician } };
        SettingsViewModel.ApplyProfiles(s, drafts, "home");
        s.Profile.LicenceClass.Should().Be("technician");
    }

    [Theory]
    [InlineData(UsLicenceClass.AmateurExtra)]
    [InlineData(UsLicenceClass.Advanced)]
    [InlineData(UsLicenceClass.General)]
    [InlineData(UsLicenceClass.Technician)]
    [InlineData(UsLicenceClass.NotUs)]
    public void LicenceOf_ShownName_RoundTrips(UsLicenceClass c) =>
        SettingsViewModel.LicenceOf(SettingsViewModel.LicenceName(c)).Should().Be(c);
}
