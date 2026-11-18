// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

/// <summary>A Club Log 403 stop is kept in settings until the credentials change or the operator resumes uploads.</summary>
public class ClubLogStopSettingsTests
{
    private static ClubLogStop Stop() => new()
    {
        Email = "op@example.com", Callsign = "N0CALL", StoppedUtc = new DateTime(2026, 11, 15, 2, 0, 0, DateTimeKind.Utc), Reason = "Invalid login",
    };

    private static AppSettings Stopped()
    {
        var s = new AppSettings();
        s.Profile.Callsign = "N0CALL";
        s.Profile.Grid = "DN40";
        s.Profile.ClubLog.Email = "op@example.com";
        s.Profile.ClubLog.Stop = Stop();
        return s;
    }

    private static ProfileDraft Draft(AppSettings s, string password = "app-pass") => new()
    {
        Id = s.Profile.Id, Name = s.Profile.Name, Callsign = s.Profile.Callsign, Grid = s.Profile.Grid, Power = "100",
        ClubLogEmail = s.Profile.ClubLog.Email, ClubLogPassword = password, ClubLogPasswordSaved = "app-pass", ClubLogStop = s.Profile.ClubLog.Stop,
    };

    [Fact]
    public void Save_StopThenNewAccessor_StopIsStillThere()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ft8clublog").FullName, "settings.json");
        var store = new SettingsStore(path);
        var accessor = new AppSettingsAccessor(store, store.Load());
        var id = accessor.Current.Profile.Id;

        new SettingsClubLogStopStore(accessor).Save(id, Stop());

        var reopened = new SettingsStore(path);
        var stop = new SettingsClubLogStopStore(new AppSettingsAccessor(reopened, reopened.Load())).Load(id);
        stop.Should().NotBeNull();
        stop!.Email.Should().Be("op@example.com");
        stop.Reason.Should().Be("Invalid login");
    }

    [Fact]
    public void ApplyProfiles_UnchangedCredentials_KeepsStop()
    {
        var s = Stopped();
        SettingsViewModel.ApplyProfiles(s, [Draft(s)], s.Profile.Id);
        s.Profile.ClubLog.Stop.Should().NotBeNull();
    }

    [Fact]
    public void ApplyProfiles_PasswordChanged_ClearsStop()
    {
        var s = Stopped();
        SettingsViewModel.ApplyProfiles(s, [Draft(s, password: "new-app-pass")], s.Profile.Id);
        s.Profile.ClubLog.Stop.Should().BeNull();
    }

    [Fact]
    public void ApplyProfiles_EmailChanged_ClearsStop()
    {
        var s = Stopped();
        var d = Draft(s);
        d.ClubLogEmail = "other@example.com";
        SettingsViewModel.ApplyProfiles(s, [d], s.Profile.Id);
        s.Profile.ClubLog.Stop.Should().BeNull();
    }

    [Fact]
    public void ApplyProfiles_OperatorResumed_ClearsStop()
    {
        var s = Stopped();
        var d = Draft(s);
        d.ClubLogResume = true;
        SettingsViewModel.ApplyProfiles(s, [d], s.Profile.Id);
        s.Profile.ClubLog.Stop.Should().BeNull();
    }
}
