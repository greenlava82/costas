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
    public void Summary_Collapsed_SaysAllowedAndPassword()
    {
        Row("dan").Summary.Should().Be("Allowed · password set");
        Row("dan", enabled: false, hasPassword: false).Summary.Should().Be("Not allowed · no password yet");
        Row("", hasPassword: false, newPassword: "correct horse battery").Summary.Should().Be("Allowed · new password when you save");
        Row("").DisplayName.Should().Be("New user");
    }

    [Fact]
    public void EditCommand_Opened_OthersToldToClose()
    {
        RemoteUserEditViewModel? opened = null;
        var row = new RemoteUserEditViewModel("a", "dan", true, true) { OnEdit = r => opened = r };
        row.EditLabel.Should().Be("Edit");
        row.EditCommand.Execute(null);
        row.IsEditing.Should().BeTrue();
        row.EditLabel.Should().Be("Done");
        opened.Should().BeSameAs(row);
        opened = null;
        row.EditCommand.Execute(null);
        row.IsEditing.Should().BeFalse();
        opened.Should().BeNull("closing does not close the others");
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(" 192.168.1.10 ", null)]
    [InlineData("fd00::10", null)]
    [InlineData("nginx.local", "not an IP address")]
    [InlineData("127.0.0.1", "needs no address")]
    [InlineData("203.0.113.7", "home network")]
    public void ProxyAddressCheck_Address_OnlyAnotherLocalComputer(string text, string? problem)
    {
        var why = SettingsViewModel.ProxyAddressCheck(text);
        if (problem is null) why.Should().BeNull();
        else why.Should().Contain(problem);
    }

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
