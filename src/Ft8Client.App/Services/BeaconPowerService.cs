// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;

namespace Ft8Client.App.Services;

/// <summary>
/// Sets the radio's power for the WSPR beacon (a share of its maximum, at least 1 W) and puts the old setting back when the
/// beacon stops, only while nothing is being transmitted (<see cref="RadioPowerService.CanChange"/>). A radio that does not
/// report its power is left alone and the operator is told to set it. Nothing here keys the radio.
/// </summary>
public sealed class BeaconPowerService(RadioPowerService power, TimeSpan? restoreWait = null) : IBeaconPower
{
    /// <summary>What the operator is told when the radio's power cannot be changed from here.</summary>
    public const string SetItYourself = "Set the power on the radio; Costas cannot.";

    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly TimeSpan _restoreWait = restoreWait ?? TimeSpan.FromMinutes(3);
    private int? _previousWatts;

    /// <inheritdoc />
    public async Task<string?> ApplyAsync(int percent)
    {
        await _busy.WaitAsync().ConfigureAwait(false);
        try
        {
            await power.RefreshAsync().ConfigureAwait(false);
            if (power.Power is not { } now) return SetItYourself;
            // Only the first Apply of a run remembers: the setting before the beacon is what comes back.
            _previousWatts ??= now.Watts;
            var watts = Math.Max(RadioPowerService.MinWatts, (int)Math.Round(now.MaxWatts * percent / 100.0));
            var note = await power.SetAsync(watts).ConfigureAwait(false);
            return note is null ? null : $"{SetItYourself} ({note})";
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <inheritdoc />
    public async Task RestoreAsync()
    {
        await _busy.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_previousWatts is not { } watts) return;
            // A transmission in progress when the beacon stopped finishes first; its power is not changed under it.
            var until = DateTime.UtcNow + _restoreWait;
            while (!power.CanChange && DateTime.UtcNow < until) await Task.Delay(250).ConfigureAwait(false);
            if (!power.CanChange) return; // left as it is; the operator sees the setting on the screen
            await power.SetAsync(watts).ConfigureAwait(false);
            _previousWatts = null;
        }
        finally
        {
            _busy.Release();
        }
    }
}
