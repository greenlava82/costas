// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Collections.Concurrent;
using System.Globalization;
using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.Core;
using Ft8Client.Core.Remote;
using Ft8Client.Core.Time;

namespace Ft8Client.App.Remote;

/// <summary>
/// The rules of remote operation, without the network: sign-in, who has control, the commands, and the link-loss
/// stop. A phone takes control with any command; operator input at the desk takes it back. While a phone has control the
/// Tx watchdog is capped at <see cref="ControlLease.WatchdogCapMinutes"/> minutes, and when it goes silent for
/// <see cref="ControlLease.LinkTimeout"/> transmit is halted.
/// </summary>
public sealed class RemoteControl
{
    private readonly IRemoteTarget _target;
    private readonly IClock _clock;
    private readonly Action<string> _saveTokens;
    private readonly IRemoteTuning? _tuning;
    private readonly RadioPowerService? _power;
    private readonly RadioControlsService? _controls;
    private readonly ConcurrentDictionary<string, string> _viewing = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _radioViewers = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Lowest Tx offset the phone can set, as on the desk.</summary>
    public const int MinOffsetHz = 200;

    /// <summary>Highest Tx offset the phone can set, as on the desk.</summary>
    public const int MaxOffsetHz = 2900;

    /// <summary>Creates the rules over a target session.</summary>
    /// <param name="target">The session.</param>
    /// <param name="clock">Clock.</param>
    /// <param name="tokens">The paired phones.</param>
    /// <param name="saveTokens">Saves the paired phones (to the OS credential store).</param>
    /// <param name="tuning">Band and Tx offset changes; without it the phone cannot change them.</param>
    /// <param name="power">The radio's power setting; without it the phone cannot change it.</param>
    /// <param name="controls">The radio's receive and tuner settings; without them the Radio tab shows only Tune and meters.</param>
    /// <param name="users">Username and password sign-in; without it phones only pair.</param>
    /// <param name="switchProfile">
    /// Switches to a signed-in user's own station profile (by user id) when they tap Operate as: the refusal while the
    /// station is busy, or the profile now in use. Without it everyone operates as the active profile.
    /// </param>
    /// <param name="profileOffer">The profile a signed-in user (by user id) is linked to, or null when they are not linked.</param>
    public RemoteControl(IRemoteTarget target, IClock clock, DeviceTokens tokens, Action<string> saveTokens,
                         IRemoteTuning? tuning = null, RadioPowerService? power = null, RadioControlsService? controls = null,
                         RemoteUsers? users = null, Func<string, (string? Refusal, string? NowAs)>? switchProfile = null,
                         Func<string, RemoteProfileOffer?>? profileOffer = null)
    {
        _switchProfile = switchProfile;
        _profileOffer = profileOffer;
        _target = target;
        _tuning = tuning;
        _power = power;
        _controls = controls;
        _clock = clock;
        Tokens = tokens;
        _saveTokens = saveTokens;
        Users = users;
    }

    /// <summary>Raised when control changes hands or a phone connects for the first time.</summary>
    public event Action? Changed;

    /// <summary>Who has control.</summary>
    public ControlLease Lease { get; } = new();

    /// <summary>The paired phones.</summary>
    public DeviceTokens Tokens { get; }

    /// <summary>Username and password sign-in, or null when this station has none.</summary>
    public RemoteUsers? Users { get; }

    /// <summary>True when every device must sign in with a username and password; pairing is refused.</summary>
    public bool SignInRequired => Users?.Required == true;

    /// <summary>
    /// Who holds <paramref name="token"/>, or null when it is not valid. With sign-in on only a user's sign-in counts, so
    /// turning it on cuts off every paired phone; with it off only paired phones count.
    /// </summary>
    public RemoteIdentity? SignIn(string? token)
    {
        if (SignInRequired)
        {
            var who = Users!.SignIn(token);
            if (who?.Username is { } name) _names[who.DeviceId] = name;
            if (who?.UserId is { } user) _userIds[who.DeviceId] = user;
            return who;
        }
        if (!Tokens.Verify(token)) return null;
        return new RemoteIdentity(RemoteIdentity.DeviceIdFor(token!));
    }

    /// <summary>Notices for the people already here that someone else signed in or took control.</summary>
    public RemoteNotices Notices { get; } = new();

    // Usernames by device, for the notices ("Ash took control"); a paired phone (sign-in off) has none.
    private readonly ConcurrentDictionary<string, string> _names = new(StringComparer.Ordinal);

    // User ids by device, for the profile each signed-in user operates as.
    private readonly ConcurrentDictionary<string, string> _userIds = new(StringComparer.Ordinal);

    /// <summary>The signed-in user behind <paramref name="deviceId"/>, or null for a paired phone or one not seen yet.</summary>
    public string? UserIdOf(string deviceId) => _userIds.TryGetValue(deviceId, out var id) ? id : null;
    private readonly Func<string, (string? Refusal, string? NowAs)>? _switchProfile;
    private readonly Func<string, RemoteProfileOffer?>? _profileOffer;

    /// <summary>The profile the user behind <paramref name="deviceId"/> is linked to, for their phone; null when there is none.</summary>
    public RemoteProfileOffer? ProfileFor(string deviceId) =>
        _profileOffer is not null && UserIdOf(deviceId) is { } user ? _profileOffer(user) : null;

    private string NameOf(string deviceId) => _names.TryGetValue(deviceId, out var n) ? n : "A phone";

    /// <summary>A user signed in with their password on a device: everyone else using the station is told.</summary>
    public void SignedIn(string token, string username)
    {
        var device = RemoteIdentity.DeviceIdFor(token);
        _names[device] = username;
        Notices.Add($"{username} signed in on another device.", device, _clock.UtcNow);
        Changed?.Invoke();
    }

    /// <summary>
    /// Gives a phone its token the first time it opens the page. There is no code to type (owner decision, 2026-10-25):
    /// the server only answers addresses on the home network, and the token just identifies the phone.
    /// </summary>
    public string Pair()
    {
        var token = Tokens.Issue();
        try
        {
            _saveTokens(Tokens.Serialize());
        }
        catch (Exception ex)
        {
            // The phone is paired until Costas restarts; it will have to pair again after that.
            Serilog.Log.Warning(ex, "Could not save the paired phones");
        }
        // With sign-in off a first visit is the sign-in, so the people already here hear of it the same way.
        Notices.Add("A new phone opened the remote page.", RemoteIdentity.DeviceIdFor(token), _clock.UtcNow);
        Changed?.Invoke();
        return token;
    }

    /// <summary>"you", "desk" or "other": who has control, as seen by <paramref name="deviceId"/>.</summary>
    public string ControlFor(string deviceId)
    {
        var holder = Lease.Holder;
        return holder is null ? "desk" : string.Equals(holder, deviceId, StringComparison.Ordinal) ? "you" : "other";
    }

    /// <summary>The station <paramref name="deviceId"/> has open, or null.</summary>
    public string? ViewingFor(string deviceId) => _viewing.TryGetValue(deviceId, out var call) ? call : null;

    /// <summary>Operator input at the desk: the desk is the control point again.</summary>
    public void DeskActivity()
    {
        string? holder;
        lock (_gate)
        {
            holder = Lease.Holder;
            Lease.Release();
        }
        if (holder is null) return;
        _target.SetWatchdogCap(null);
        Notices.Add("The desk took control.", null, _clock.UtcNow);
        Changed?.Invoke();
    }

    /// <summary>Carries out a message from a signed-in phone. Returns a line to show on the phone, or null.</summary>
    public async Task<string?> HandleAsync(string deviceId, RemoteCommand cmd)
    {
        var now = _clock.UtcNow;
        if (cmd.Type == "ping")
        {
            lock (_gate) Lease.Heartbeat(deviceId, now);
            return null;
        }
        if (cmd.Type == "show")
        {
            // Looking at a station changes nothing at the station, so it does not take control.
            var shown = cmd.Call?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(shown)) _viewing.TryRemove(deviceId, out _);
            else _viewing[deviceId] = shown;
            return null;
        }
        if (cmd.Type == "radioView")
        {
            // Looking at the Radio tab changes nothing either; it only asks the station to read the radio's settings.
            if (cmd.On == true) _radioViewers[deviceId] = true;
            else _radioViewers.TryRemove(deviceId, out _);
            if (cmd.On == true && _controls is not null) _ = _controls.RefreshAsync();
            return null;
        }
        var call = cmd.Call?.Trim().ToUpperInvariant() ?? string.Empty;
        // A message the station cannot act on changes nothing: it must not take control or reset the Tx watchdog.
        if (!Understood(cmd, call)) return "The station did not understand that.";
        // A user linked to a station profile switches to it deliberately, with Operate as (D25). Until they have, what
        // would go out or be logged under the profile in use is refused, so nobody transmits under another operator's
        // callsign; everything else (Stop, Stop CQ, Abandon, the band, the offset, the radio) works as for anyone.
        if (cmd.Type == "switchProfile") return SwitchProfile(deviceId, now);
        if (OperatesAs(cmd.Type) && ProfileFor(deviceId) is { OnMine: false } offer) return NotYetYours(offer);
        TakeControl(deviceId, now);
        switch (cmd.Type)
        {
            case "halt":
                await _target.HaltAsync(null).ConfigureAwait(false);
                return null;
            case "abandon":
                await _target.AbandonAsync().ConfigureAwait(false);
                return null;
        }
        _target.Activity();
        switch (cmd.Type)
        {
            case "call" when call.Length > 0:
                return await _target.CallStationAsync(call).ConfigureAwait(false) ? null : $"{call} is not in the station list.";
            case "answer" when call.Length > 0:
                _target.AnswerCaller(call);
                return null;
            case "answerAfter" when call.Length > 0:
                _target.AnswerAfterThisContact(call);
                return null;
            case "dismiss" when call.Length > 0:
                _target.DismissCaller(call);
                return null;
            case "cq":
                _target.ToggleCq();
                return null;
            case "cqStop":
                _target.StopCq();
                return null;
            case "resend":
                _target.Resend();
                return null;
            case "macro" when cmd.Key is { } key:
                return ModeInfo.IsSlotted(_target.Snapshot.Mode) ? "Macros are for PSK and RTTY." : _target.SendMacro(key);
            case "logNow":
                if (_target.Snapshot.Contact?.CanLogNow == true) _target.LogNow();
                return null;
            case "jump" when cmd.Step is { } step:
                _target.JumpTo(step);
                return null;
            case "band":
                return await BandAsync(cmd).ConfigureAwait(false);
            case "goTo":
                return await GoToAsync().ConfigureAwait(false);
            case "offset" when cmd.Hz is { } hz:
                if (_tuning is null) return "The station cannot change that from the phone.";
                if (hz is < MinOffsetHz or > MaxOffsetHz) return $"The offset must be between {MinOffsetHz} and {MaxOffsetHz} Hz.";
                _tuning.SetTxOffset(hz);
                return null;
            case "offsetAuto":
                if (_tuning is null) return "The station cannot change that from the phone.";
                _tuning.SetTxOffset(null);
                return null;
            case "power" when cmd.Watts is { } watts:
                // Only the setting changes, never PTT; refused while transmitting, like a band change.
                return _power is null ? "The station cannot change that from the phone." : await _power.SetAsync(watts).ConfigureAwait(false);
            case "tune":
                // The desk's Tune button: a deliberate tap starts the radio's tuner (or a tone with the Tune limit),
                // under every transmit guard; a second tap stops it.
                if (TuneState.Tuning(_target.Snapshot, now))
                {
                    await _target.HaltAsync(null).ConfigureAwait(false);
                    return null;
                }
                return await _target.TuneAsync(ViewModels.MainViewModel.TuneSeconds).ConfigureAwait(false);
            case "radio" when cmd.Value is { } value:
                // Only the setting changes, never PTT; refused while transmitting.
                if (_controls is null || RemoteRadioBuilder.Parse(cmd.Control) is not { } control) return "The station cannot change that from the phone.";
                return await _controls.SetAsync(control, value).ConfigureAwait(false);
            default:
                return "The station did not understand that.";
        }
    }

    // Commands that put the station's callsign on the air or in the log: a linked user sends them only from their own
    // profile. Tune is not one: its carrier carries no callsign.
    private static bool OperatesAs(string type) =>
        type is "cq" or "call" or "answer" or "answerAfter" or "resend" or "macro" or "jump" or "logNow";

    private static string NotYetYours(RemoteProfileOffer offer) => offer.Busy is { } busy
        ? $"Not done: the station is on {offer.InUse}, busy {busy}. You can switch to {offer.Mine} when it is idle."
        : $"Not done: the station is on {offer.InUse}. Tap Operate as {offer.MineCall} first.";

    // The phone's Operate as button: the user's own profile, only while the station is idle. It takes control, as any
    // deliberate tap does, and everyone else is told.
    private string? SwitchProfile(string deviceId, DateTime now)
    {
        if (_switchProfile is null || UserIdOf(deviceId) is not { } user || _profileOffer?.Invoke(user) is not { } offer)
            return "Only a signed-in user linked to a station profile can switch. Link one at the desk in Settings > Mobile connection.";
        string? nowAs = null;
        if (!offer.OnMine)
        {
            (var refusal, nowAs) = _switchProfile(user);
            if (refusal is not null) return refusal;
        }
        TakeControl(deviceId, now);
        _target.Activity();
        if (nowAs is not null)
        {
            Notices.Add($"{NameOf(deviceId)} switched the station to {nowAs}.", deviceId, now);
            Changed?.Invoke();
        }
        return null;
    }

    // The device that sent a deliberate command becomes the control point; the others are told when that changes.
    private void TakeControl(string deviceId, DateTime now)
    {
        bool changed;
        string? previous;
        lock (_gate)
        {
            previous = Lease.Holder;
            changed = !string.Equals(previous, deviceId, StringComparison.Ordinal);
            Lease.Take(deviceId, now);
        }
        _target.SetWatchdogCap(ControlLease.WatchdogCapMinutes);
        if (!changed) return;
        var from = previous is null ? "the desk" : _names.TryGetValue(previous, out var p) ? p : "another phone";
        Notices.Add($"{NameOf(deviceId)} took control from {from}.", deviceId, now);
        Changed?.Invoke();
    }

    private static bool Understood(RemoteCommand cmd, string call) => cmd.Type switch
    {
        "halt" or "abandon" or "cq" or "cqStop" or "switchProfile" or "resend" or "logNow" or "band" or "offsetAuto" or "goTo" => true,
        "call" or "answer" or "answerAfter" or "dismiss" => call.Length > 0,
        "jump" => cmd.Step is not null,
        "offset" => cmd.Hz is not null,
        "power" => cmd.Watts is not null,
        "tune" => true,
        "radio" => cmd.Value is not null && cmd.Control is { Length: > 0 },
        "macro" => cmd.Key is not null,
        _ => false,
    };

    // The "why is it quiet" line's Go to button: the frequency is the one in the station's own readout, never one the phone
    // sends, so a tap can only do what the desk's button would.
    private async Task<string?> GoToAsync()
    {
        if (_tuning is null) return "The station cannot change that from the phone.";
        var s = _target.Snapshot;
        if (s.Transmitting) return "Wait until this transmission ends to change frequency.";
        if (s.Quiet?.GoToDialHz is not { } hz) return "There is no frequency to go to now.";
        return await _tuning.TuneDialAsync(hz).ConfigureAwait(false);
    }

    // As the band menu on the desk: not while transmitting, and only to a band in the frequency table.
    private async Task<string?> BandAsync(RemoteCommand cmd)
    {
        if (_tuning is null) return "The station cannot change that from the phone.";
        if (_target.Snapshot.Transmitting) return "Wait until this transmission ends to change band.";
        var mode = _target.Snapshot.Mode;
        if (cmd.Mode is { Length: > 0 } m && !ModeInfo.TryParse(m, out mode)) return $"{m} is not a mode the station knows.";
        var bands = _tuning.Bands(mode);
        var band = cmd.Band?.Trim().Replace(" ", string.Empty, StringComparison.Ordinal) ?? _target.Snapshot.Band;
        if (!bands.Contains(band, StringComparer.OrdinalIgnoreCase))
        {
            // A mode change keeps the band when it can, like the desk; otherwise the first band of that mode.
            if (cmd.Band is { Length: > 0 } && cmd.Mode is null) return $"{cmd.Band} is not in the frequency table for {ModeInfo.Name(mode)}.";
            if (bands.Count == 0) return $"There are no {ModeInfo.Name(mode)} frequencies in the frequency table.";
            band = bands[0];
        }
        else
        {
            band = bands.First(b => string.Equals(b, band, StringComparison.OrdinalIgnoreCase));
        }
        await _tuning.SetBandAsync(band, mode).ConfigureAwait(false);
        return null;
    }

    /// <summary>True while a phone has its Radio tab open, so the radio's settings are worth reading.</summary>
    public bool RadioWanted => !_radioViewers.IsEmpty;

    /// <summary>A phone's connection closed: it is no longer looking at anything.</summary>
    public void Closed(string deviceId)
    {
        _radioViewers.TryRemove(deviceId, out _);
    }

    /// <summary>The Radio tab: Tune, the meters and the radio's settings.</summary>
    public RemoteRadio RadioFor(SessionSnapshot s) =>
        RemoteRadioBuilder.Build(s, _clock.UtcNow, _controls?.Controls ?? [], _controls?.CanChange ?? !s.Transmitting, _power?.Power?.MaxWatts);

    /// <summary>The radio's power as the phone shows it, or null when the radio does not report it.</summary>
    public RemotePower? PowerFor() => _power?.Power is { } p ? new RemotePower(p.Watts, p.MaxWatts, _power.CanChange) : null;

    /// <summary>
    /// Checks the link of the phone in control. When it has been silent too long, control returns to the desk and transmit
    /// is halted, with a line saying why if anything was going out. Call about once a second.
    /// </summary>
    public async Task TickAsync()
    {
        var now = _clock.UtcNow;
        lock (_gate)
        {
            if (!Lease.LinkLost(now)) return;
            Lease.Release();
        }
        var wanted = _target.WantsTransmit;
        _target.SetWatchdogCap(null);
        await _target.HaltAsync(wanted
            ? $"Transmit stopped at {now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}: lost touch with the phone."
            : null).ConfigureAwait(false);
        Changed?.Invoke();
    }
}
