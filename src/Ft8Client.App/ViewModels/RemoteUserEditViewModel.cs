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
    /// <param name="profileId">The station profile they operate as, or null for whichever is in use.</param>
    public RemoteUserEditViewModel(string id, string username, bool enabled, bool hasPassword, string? profileId = null)
    {
        ProfileId = profileId;
        Id = id;
        Username = username;
        Enabled = enabled;
        HasPassword = hasPassword;
    }

    /// <summary>The user's stable id.</summary>
    public string Id { get; }

    /// <summary>A password is already set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordPlaceholder), nameof(Summary))]
    public partial bool HasPassword { get; private set; }

    /// <summary>
    /// The new password was stored (Settings applied without closing): the box empties so a second Apply does not set it
    /// again, which would sign this user out everywhere once more.
    /// </summary>
    public void PasswordSaved()
    {
        if (NewPassword.Length == 0) return;
        NewPassword = string.Empty;
        HasPassword = true;
    }

    /// <summary>The name typed to sign in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Username { get; set; }

    /// <summary>May sign in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool Enabled { get; set; }

    /// <summary>A new password, set on save; empty keeps the current one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordPlaceholder), nameof(Summary))]
    public partial string NewPassword { get; set; } = string.Empty;

    /// <summary>The editor is open; otherwise the user shows as one line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditLabel))]
    public partial bool IsEditing { get; set; }

    /// <summary>The station profile they operate as (D25), or null for whichever is in use.</summary>
    public string? ProfileId { get; private set; }

    /// <summary>The profiles to choose from, set by Settings (they can be added and renamed while it is open).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ProfileChoice> ProfileChoices { get; private set; } = [];

    /// <summary>The choice shown for <see cref="ProfileId"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial ProfileChoice? SelectedProfileChoice { get; set; }

    partial void OnSelectedProfileChoiceChanged(ProfileChoice? value)
    {
        if (value is not null) ProfileId = value.Id;
    }

    /// <summary>Refreshes the list, keeping the profile chosen; one no longer in the list becomes none.</summary>
    public void SetProfileChoices(IReadOnlyList<ProfileChoice> choices)
    {
        var id = ProfileId;
        ProfileChoices = choices;
        SelectedProfileChoice = choices.FirstOrDefault(c => c.Id == id) ?? choices.FirstOrDefault();
    }

    /// <summary>The name on the collapsed line.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Username) ? "New user" : Username.Trim();

    /// <summary>The state on the collapsed line: may they sign in, and is there a password.</summary>
    public string Summary =>
        (Enabled ? "Allowed" : "Not allowed") + " · "
        + (NewPassword.Length > 0 ? "new password when you save" : HasPassword ? "password set" : "no password yet")
        + (SelectedProfileChoice is { Id: not null } p ? " · operates as " + p.Label : string.Empty);

    /// <summary>The edit button's text.</summary>
    public string EditLabel => IsEditing ? "Done" : "Edit";

    /// <summary>What the empty password box says.</summary>
    public string PasswordPlaceholder => HasPassword ? "Password set (type to change)" : "Password (10 characters or more)";

    /// <summary>Said after "Sign out everywhere".</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Takes this row out of the list (the user goes when Settings is saved).</summary>
    public Action<RemoteUserEditViewModel>? OnRemove { get; init; }

    /// <summary>Ends this user's sign-ins on every device, at once.</summary>
    public Action<RemoteUserEditViewModel>? OnSignOut { get; init; }

    /// <summary>Called when this user's editor opens, so the others close.</summary>
    public Action<RemoteUserEditViewModel>? OnEdit { get; init; }

    [RelayCommand]
    private void Edit()
    {
        IsEditing = !IsEditing;
        if (IsEditing) OnEdit?.Invoke(this);
    }

    [RelayCommand]
    private void Remove() => OnRemove?.Invoke(this);

    [RelayCommand]
    private void SignOut() => OnSignOut?.Invoke(this);
}
