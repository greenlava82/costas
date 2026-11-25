// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.Core;

namespace Ft8Client.App.Views;

/// <summary>About, licences and credits.</summary>
public partial class AboutWindow : Window
{
    private const int TapsToToggle = 7;
    private readonly AppSettingsAccessor? _settings;
    private readonly UpdateService? _updates;
    private readonly UpdateBarViewModel? _bar;
    private int _taps;
    private DateTime _lastTap;

    /// <summary>Creates the window (for the designer).</summary>
    public AboutWindow()
        : this(null, null, null)
    {
    }

    /// <summary>
    /// Creates the window. Clicking the product name seven times in a row toggles developer mode (the easter egg).
    /// <paramref name="bar"/> is the main window's update bar: its Update is offered here too, because this window is a
    /// dialog and the bar can't be clicked until it closes.
    /// </summary>
    public AboutWindow(AppSettingsAccessor? settings, UpdateService? updates, UpdateBarViewModel? bar)
    {
        InitializeComponent();
        _settings = settings;
        _updates = updates;
        _bar = bar;
        Product.Text = $"{AppInfo.ProductName} {AppInfo.Version}";
        Channel.Text = ChannelText(updates?.RunningDevelopmentBuild == true, settings?.Current.Updates.DevelopmentBuilds == true);
        Product.PointerPressed += OnProductPressed;
        CheckUpdates.IsEnabled = updates is not null;
        CheckUpdates.Click += OnCheckUpdates;
        InstallUpdate.Click += OnInstallUpdate;
        if (bar is not null)
        {
            bar.PropertyChanged += OnBarChanged;
            if (bar.Visible) UpdateStatus.Text = bar.Detail.Length > 0 ? bar.Detail : bar.Text;
        }
        RefreshInstall();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (_bar is not null) _bar.PropertyChanged -= OnBarChanged;
        base.OnClosed(e);
    }

    // Same as the bar's Update: download, verify, then close and reopen on the new build (refused mid-contact).
    private void OnInstallUpdate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_bar?.InstallCommand.CanExecute(null) == true) _bar.InstallCommand.Execute(null);
    }

    private void OnBarChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_bar is null) return;
        if (e.PropertyName == nameof(UpdateBarViewModel.Detail) && _bar.Detail.Length > 0) UpdateStatus.Text = _bar.Detail;
        RefreshInstall();
    }

    private void RefreshInstall()
    {
        var busy = _bar?.Busy == true;
        InstallUpdate.IsVisible = _bar?.Visible == true;
        InstallUpdate.IsEnabled = !busy;
        CheckUpdates.IsEnabled = _updates is not null && !busy;
    }

    // Checks even when the daily check is off, and offers a build that was skipped before: the operator asked.
    private async void OnCheckUpdates(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_updates is null) return;
        CheckUpdates.IsEnabled = false;
        UpdateStatus.Text = "Checking…";
        _settings?.Update(s => s.Updates.SkippedTag = null);
        var r = await _updates.CheckAsync(CancellationToken.None, manual: true);
        UpdateStatus.Text = r is not null ? (_bar is not null ? UpdateBarViewModel.BarText(r, AppInfo.Version, UpdateBarViewModel.DownloadSize(r.InstallerSize))
                                             : $"{AppInfo.ProductName} {r.Version} is available: see the bar at the top of the main window.")
            : _updates.LastError is { } err ? $"Could not check: {err}"
            : $"You have the latest build ({AppInfo.Version}).";
        CheckUpdates.IsEnabled = true;
        // The bar learns of the release through a posted refresh; show Update now rather than wait for it.
        if (r is not null && _bar is not null) InstallUpdate.IsVisible = true;
        RefreshInstall();
    }

    /// <summary>Which kind of build this is and which updates it takes, with where to change that.</summary>
    internal static string ChannelText(bool runningDevelopment, bool followsDevelopment) =>
        (runningDevelopment ? "Development build. " : "Release. ")
        + (followsDevelopment ? "Updates to each new development build" : "Updates to releases only")
        + "; change this in Settings, Updates, Update channel.";

    private void OnProductPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_settings is null) return;
        var now = DateTime.UtcNow;
        _taps = now - _lastTap < TimeSpan.FromSeconds(2) ? _taps + 1 : 1;
        _lastTap = now;
        var left = TapsToToggle - _taps;
        var on = _settings.Current.Developer.Enabled;
        if (left > 0)
        {
            if (_taps >= 3) Show(on ? $"{left} more to leave developer mode." : $"{left} more to developer mode.");
            return;
        }
        _taps = 0;
        _settings.Update(s => s.Developer.Enabled = !on);
        Show(!on
            ? "Developer mode is on. Its options are in Settings, under Developer; Setup now lists Hamlib's test radios."
            : "Developer mode is off. Restart to leave any replay that is running.");
    }

    private void Show(string text)
    {
        Secret.Text = text;
        Secret.IsVisible = true;
    }
}
