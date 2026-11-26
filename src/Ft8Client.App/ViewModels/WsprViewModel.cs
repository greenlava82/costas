// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ft8Client.App.Engine;
using Ft8Client.Core;
using Ft8Client.Core.Ranking;
using Ft8Client.Core.Services;
using Ft8Client.Core.Wspr;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.ViewModels;

/// <summary>What the WSPR screen asks of the session and the settings.</summary>
public interface IWsprCommands
{
    /// <summary>Starts the beacon; returns null once started, else why not.</summary>
    Task<string?> StartBeaconAsync();

    /// <summary>Stops the beacon (a transmission on the air finishes).</summary>
    void StopBeacon();

    /// <summary>"I'm here": operator input for the presence rule.</summary>
    void ImHere();

    /// <summary>The active profile's WSPR settings.</summary>
    WsprOptions Options { get; }

    /// <summary>Changes and saves the active profile's WSPR settings.</summary>
    void UpdateOptions(Action<WsprOptions> change);

    /// <summary>Changes band and mode after the operator agreed to stop the beacon.</summary>
    void LeaveWspr(string band, Mode mode);
}

/// <summary>One heard signal as the WSPR list shows it.</summary>
/// <param name="Time">Slot time, UTC, "14:02".</param>
/// <param name="Call">Callsign (<c>&lt;...&gt;</c> for a hash that did not resolve).</param>
/// <param name="Grid">Locator, or blank.</param>
/// <param name="Snr">SNR in dB.</param>
/// <param name="Drift">Drift in Hz per minute.</param>
/// <param name="Distance">Distance from my locator in the operator's unit, or blank.</param>
/// <param name="Power">The power the sender reported, "37 dBm".</param>
/// <param name="Frequency">Audio frequency above the dial in hertz.</param>
public sealed record WsprSpotRowViewModel(string Time, string Call, string Grid, string Snr, string Drift, string Distance, string Power, string Frequency);

/// <summary>
/// The WSPR screen (docs/10-wspr.md section 8), shown in place of Operate in WSPR mode: Start or Stop beacon, the countdown to
/// the next slot and whether it transmits, how long the operator has before the beacon stops, the power line and its warning,
/// the beacon's settings, WSPRnet's state and the signals heard.
/// </summary>
public sealed partial class WsprViewModel : ObservableObject
{
    private readonly IWsprCommands _commands;
    private readonly Func<DateTime> _now;
    private WsprView? _view;
    private bool _loading;
    private (int Count, DateTime? First, string? Call, DistanceUnit Units) _rowsKey;
    private (string Band, Mode Mode)? _leaveTo;

    /// <summary>Creates the view model.</summary>
    public WsprViewModel(IWsprCommands commands, Func<DateTime> now)
    {
        _commands = commands;
        _now = now;
    }

    /// <summary>The legal power values, for the reported-dBm picker.</summary>
    public IReadOnlyList<int> DbmChoices { get; } = WsprPower.Legal;

    /// <summary>The signals heard, newest first.</summary>
    public ObservableCollection<WsprSpotRowViewModel> Spots { get; } = [];

    /// <summary>The screen is showing (WSPR mode, on the Operate tab).</summary>
    [ObservableProperty]
    public partial bool Visible { get; set; }

    /// <summary>Distance units for the list.</summary>
    public DistanceUnit Units { get; set; } = DistanceUnit.Miles;

    /// <summary>The beacon is running.</summary>
    [ObservableProperty]
    public partial bool BeaconOn { get; set; }

    /// <summary>"Start beacon" or "Stop beacon".</summary>
    [ObservableProperty]
    public partial string StartStopText { get; set; } = "Start beacon";

    /// <summary>The radio is transmitting now.</summary>
    [ObservableProperty]
    public partial bool OnAir { get; set; }

    /// <summary>"Transmitting W7LIT DN40 37", "Beacon off. Receiving.", "Next slot transmits", and so on.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "Beacon off. Receiving.";

    /// <summary>"Next slot 14:06 UTC in 1:23".</summary>
    [ObservableProperty]
    public partial string Countdown { get; set; } = string.Empty;

    /// <summary>"Stops in 4:12 unless you press a key or tap I'm here", or blank when the beacon is off.</summary>
    [ObservableProperty]
    public partial string Presence { get; set; } = string.Empty;

    /// <summary>The slot as a fraction of its 2 minutes, for the progress bar.</summary>
    [ObservableProperty]
    public partial double SlotProgress { get; set; }

    /// <summary>Why the beacon did not start, or the radio-power note; blank when there is nothing to say.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>"Reports 37 dBm (5 W). Radio set for about 37.0 dBm."</summary>
    [ObservableProperty]
    public partial string PowerLine { get; set; } = string.Empty;

    /// <summary>The 3 dB warning, or blank. Advice only: the reported power is never changed.</summary>
    [ObservableProperty]
    public partial string PowerWarning { get; set; } = string.Empty;

    /// <summary>One line on what the decoder is doing.</summary>
    [ObservableProperty]
    public partial string DecoderState { get; set; } = string.Empty;

    /// <summary>The WSPRnet upload state.</summary>
    [ObservableProperty]
    public partial string UploadStatus { get; set; } = "WSPRnet off";

    /// <summary>A question waiting for an answer: stop the beacon and change band or mode?</summary>
    [ObservableProperty]
    public partial string LeaveQuestion { get; set; } = string.Empty;

    /// <summary>Tx percentage (1 to 100).</summary>
    [ObservableProperty]
    public partial decimal TxPercent { get; set; } = 20;

    /// <summary>The power the message reports, in dBm.</summary>
    [ObservableProperty]
    public partial int ReportedDbm { get; set; } = 37;

    /// <summary>Audio attenuation in dB (0 to -20).</summary>
    [ObservableProperty]
    public partial decimal AttenuationDb { get; set; }

    /// <summary>Radio power while the beacon runs, percent of its maximum.</summary>
    [ObservableProperty]
    public partial decimal PowerPercent { get; set; } = 5;

    /// <summary>Audio offset in hertz (1400 to 1600): the centre of the four tones.</summary>
    [ObservableProperty]
    public partial decimal OffsetHz { get; set; } = 1500;

    /// <summary>Keep to type 1 messages with a 6-character locator.</summary>
    [ObservableProperty]
    public partial bool PreferType1 { get; set; }

    /// <summary>Report the signals heard to WSPRnet.</summary>
    [ObservableProperty]
    public partial bool UploadEnabled { get; set; }

    /// <summary>The settings can be edited (not while the beacon runs).</summary>
    public bool CanEditSettings => !BeaconOn;

    partial void OnBeaconOnChanged(bool value) => OnPropertyChanged(nameof(CanEditSettings));

    partial void OnTxPercentChanged(decimal value) => Save(o => o.TxPercent = (int)Math.Clamp(value, 1, 100));

    partial void OnReportedDbmChanged(int value) => Save(o => o.ReportedDbm = value);

    partial void OnAttenuationDbChanged(decimal value) => Save(o => o.AttenuationDb = (int)Math.Clamp(value, -20, 0));

    partial void OnPowerPercentChanged(decimal value) => Save(o => o.PowerPercent = (int)Math.Clamp(value, 1, 100));

    partial void OnOffsetHzChanged(decimal value) => Save(o => o.OffsetHz = (int)value);

    partial void OnPreferType1Changed(bool value) => Save(o => o.PreferType1 = value);

    partial void OnUploadEnabledChanged(bool value) => Save(o => o.UploadEnabled = value);

    private void Save(Action<WsprOptions> change)
    {
        if (!_loading) _commands.UpdateOptions(change);
    }

    /// <summary>Starts or stops the beacon: an operator action, never anything else.</summary>
    [RelayCommand]
    private async Task StartStopAsync()
    {
        if (BeaconOn)
        {
            _commands.StopBeacon();
            return;
        }
        Notice = string.Empty;
        var why = await _commands.StartBeaconAsync().ConfigureAwait(true);
        if (why is not null) Notice = why;
    }

    /// <summary>"I'm here": tells the beacon the operator is present.</summary>
    [RelayCommand]
    private void ImHere() => _commands.ImHere();

    /// <summary>Asks whether to stop the beacon and move to <paramref name="band"/> and <paramref name="mode"/>.</summary>
    public void AskLeave(string band, Mode mode)
    {
        _leaveTo = (band, mode);
        LeaveQuestion = ModeInfo.IsWspr(mode) ? $"Stop the beacon and change band to {band}?" : $"Stop the beacon and switch to {ModeInfo.Name(mode)}?";
    }

    /// <summary>Yes: stop the beacon and make the change.</summary>
    [RelayCommand]
    private void ConfirmLeave()
    {
        if (_leaveTo is not { } to) return;
        _leaveTo = null;
        LeaveQuestion = string.Empty;
        _commands.LeaveWspr(to.Band, to.Mode);
    }

    /// <summary>No: keep the beacon and stay.</summary>
    [RelayCommand]
    private void KeepBeacon()
    {
        _leaveTo = null;
        LeaveQuestion = string.Empty;
    }

    /// <summary>Applies a snapshot. UI thread.</summary>
    public void Apply(SessionSnapshot s)
    {
        _view = s.Wspr;
        if (s.Wspr is not { } view)
        {
            BeaconOn = false;
            return;
        }
        var b = view.Beacon;
        BeaconOn = b.On;
        StartStopText = b.On ? "Stop beacon" : "Start beacon";
        OnAir = s.Transmitting;
        DecoderState = view.DecoderState;
        PowerWarning = b.PowerWarning ?? string.Empty;
        if (b.PowerNote is { } note && b.On) Notice = note;
        PowerLine = PowerText(b);
        UploadStatus = s.Services.TryGetValue(ServiceNames.Wsprnet, out var st) ? st.Message : "WSPRnet off";
        LoadOptions();
        FillSpots(view);
        Tick(_now(), s);
    }

    /// <summary>The status line: what is on the air, or what the next slot does.</summary>
    public static string StatusText(WsprBeaconView b, bool onAir) =>
        onAir ? $"Transmitting {b.LastMessage}"
        : !b.On ? "Beacon off. Receiving."
        : b.NextSlotTransmits switch
        {
            true => "Next slot transmits.",
            false => string.IsNullOrEmpty(b.NextSlotNote) ? "Next slot receives." : $"Next slot receives ({b.NextSlotNote.ToLowerInvariant()}).",
            null => "Beacon on. Planning the next slot.",
        };

    /// <summary>"Reports 37 dBm (5 W)." with the estimate and meter reading when there are any.</summary>
    public static string PowerText(WsprBeaconView b)
    {
        var watts = WsprPower.Watts(b.ReportedDbm);
        var text = string.Create(CultureInfo.InvariantCulture, $"Reports {b.ReportedDbm} dBm ({FormatWatts(watts)}).");
        if (b.EstimateDbm is { } e) text += string.Create(CultureInfo.InvariantCulture, $" Radio and attenuation give about {e:0.0} dBm.");
        if (b.MeterDbm is { } m) text += string.Create(CultureInfo.InvariantCulture, $" Meter reads about {m:0.0} dBm.");
        return text;
    }

    private static string FormatWatts(double w) =>
        w >= 1 ? string.Create(CultureInfo.InvariantCulture, $"{w:0.#} W") : string.Create(CultureInfo.InvariantCulture, $"{w * 1000:0.#} mW");

    // The settings boxes show the saved values; they are not written back while loading.
    private void LoadOptions()
    {
        var o = _commands.Options;
        _loading = true;
        try
        {
            if (TxPercent != o.TxPercent) TxPercent = o.TxPercent;
            if (ReportedDbm != o.ReportedDbm) ReportedDbm = o.ReportedDbm;
            if (AttenuationDb != o.AttenuationDb) AttenuationDb = o.AttenuationDb;
            if (PowerPercent != o.PowerPercent) PowerPercent = o.PowerPercent;
            if (OffsetHz != o.OffsetHz) OffsetHz = o.OffsetHz;
            if (PreferType1 != o.PreferType1) PreferType1 = o.PreferType1;
            if (UploadEnabled != o.UploadEnabled) UploadEnabled = o.UploadEnabled;
        }
        finally
        {
            _loading = false;
        }
    }

    private void FillSpots(WsprView view)
    {
        var key = (view.Recent.Count, view.Recent.Count > 0 ? view.Recent[0].Spot.SlotStartUtc : (DateTime?)null,
            view.Recent.Count > 0 ? view.Recent[0].Spot.Callsign : null, Units);
        if (key == _rowsKey) return;
        _rowsKey = key;
        Spots.Clear();
        foreach (var h in view.Recent)
        {
            var s = h.Spot;
            Spots.Add(new WsprSpotRowViewModel(
                s.SlotStartUtc.ToString("HH:mm", CultureInfo.InvariantCulture),
                s.Callsign,
                s.Grid ?? string.Empty,
                s.Snr.ToString(CultureInfo.InvariantCulture),
                s.DriftHz.ToString(CultureInfo.InvariantCulture),
                h.DistanceKm is { } km ? StationText.Distance(km, Units) : string.Empty,
                string.Create(CultureInfo.InvariantCulture, $"{s.PowerDbm} dBm"),
                s.AudioHz.ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>Once a second: the countdown, the progress bar, the presence time and the status line.</summary>
    public void Tick(DateTime now, SessionSnapshot? s = null)
    {
        if (_view is not { } view) return;
        var b = view.Beacon;
        var left = view.NextSlotUtc - now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        SlotProgress = Math.Clamp(1 - left / WsprTiming.SlotLength, 0, 1);
        Countdown = string.Create(CultureInfo.InvariantCulture, $"Next slot {view.NextSlotUtc:HH:mm} UTC in {(int)left.TotalMinutes}:{left.Seconds:00}");
        Presence = b is { On: true, PresenceEndsUtc: { } end }
            ? string.Create(CultureInfo.InvariantCulture, $"Stops in {Format(end - now)} unless you press a key or tap I'm here.")
            : string.Empty;
        Status = StatusText(b, OnAir);
    }

    private static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes}:{t.Seconds:00}");
    }
}
