// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using FluentAssertions;
using Ft8Client.App.ViewModels;

namespace Ft8Client.App.Tests;

public class RemoteUsersCheckTests
{
    private static RemoteUserEditViewModel Row(string name, bool hasPassword = true, string newPassword = "", bool enabled = true) =>
        new(Guid.NewGuid().ToString("N"), name, enabled, hasPassword) { NewPassword = newPassword };

    [Fact]
    public void RemoteUsersCheck_SignInOffNoUsers_Fine()
    {
        SettingsViewModel.RemoteUsersCheck(false, []).Should().BeNull();
    }

    [Fact]
    public void RemoteUsersCheck_SignInOnWithoutAnyUsableUser_Refused()
    {
        SettingsViewModel.RemoteUsersCheck(true, []).Should().Contain("before requiring sign-in");
        SettingsViewModel.RemoteUsersCheck(true, [Row("dan", enabled: false)]).Should().Contain("before requiring sign-in");
        SettingsViewModel.RemoteUsersCheck(true, [Row("dan", hasPassword: false, newPassword: "correct horse battery")]).Should().BeNull();
    }

    [Theory]
    [InlineData("", "Enter a username")]
    [InlineData("  ", "Enter a username")]
    public void RemoteUsersCheck_BlankUsername_Refused(string name, string expected)
    {
        SettingsViewModel.RemoteUsersCheck(false, [Row(name)]).Should().Contain(expected);
    }

    [Fact]
    public void RemoteUsersCheck_SameNameTwice_RefusedWhateverTheCase()
    {
        SettingsViewModel.RemoteUsersCheck(true, [Row("Dan"), Row("dan ")]).Should().Contain("both called Dan");
    }

    [Fact]
    public void RemoteUsersCheck_ShortOrMissingPassword_Refused()
    {
        SettingsViewModel.RemoteUsersCheck(true, [Row("dan", newPassword: "short")]).Should().Contain("at least 10");
        SettingsViewModel.RemoteUsersCheck(false, [Row("guest", hasPassword: false)]).Should().Contain("Set a password for guest");
        SettingsViewModel.RemoteUsersCheck(false, [Row("guest", hasPassword: false, enabled: false)]).Should().BeNull("a user who may not sign in needs no password yet");
    }
}
