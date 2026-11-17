// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ft8Client.App.ViewModels;

/// <summary>One person who may sign in to the remote page, in the Settings editor.</summary>
public sealed partial class RemoteUserEditViewModel : ObservableObject
{
    /// <summary>Creates a row.</summary>
    /// <param name="id">The user's stable id.</param>
    /// <param name="username">The name typed to sign in.</param>
    /// <param name="enabled">May sign in.</param>
    /// <param name="hasPassword">A password is already set.</param>
    public RemoteUserEditViewModel(string id, string username, bool enabled, bool hasPassword)
    {
        Id = id;
        Username = username;
        Enabled = enabled;
        HasPassword = hasPassword;
    }

    /// <summary>The user's stable id.</summary>
    public string Id { get; }

    /// <summary>A password is already set.</summary>
    public bool HasPassword { get; }

    /// <summary>The name typed to sign in.</summary>
    [ObservableProperty]
    public partial string Username { get; set; }

    /// <summary>May sign in.</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>A new password, set on save; empty keeps the current one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordPlaceholder))]
    public partial string NewPassword { get; set; } = string.Empty;

    /// <summary>What the empty password box says.</summary>
    public string PasswordPlaceholder => HasPassword ? "Password set (type to change)" : "Password (10 characters or more)";

    /// <summary>Said after "Sign out everywhere".</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Takes this row out of the list (the user goes when Settings is saved).</summary>
    public Action<RemoteUserEditViewModel>? OnRemove { get; init; }

    /// <summary>Ends this user's sign-ins on every device, at once.</summary>
    public Action<RemoteUserEditViewModel>? OnSignOut { get; init; }

    [RelayCommand]
    private void Remove() => OnRemove?.Invoke(this);

    [RelayCommand]
    private void SignOut() => OnSignOut?.Invoke(this);
}
