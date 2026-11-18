// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using Ft8Client.App.Engine;
using Ft8Client.Core.Services;
using Ft8Client.Data.Settings;
using Ft8Client.Rig;
using Ft8Client.Rig.Flex;
using Serilog;

namespace Ft8Client.App.Services;

/// <summary>
/// The amplifier and tuner paired with a FlexRadio (a 4O3A PowerGenius XL and TunerGenius XL), shown and switched
/// through the radio's own API (docs/05-integrations.md section 2a, docs/02-ui-spec.md bottom bar "Amp and tuner").
/// Used only when the station's radio is set up as one of Hamlib's SmartSDR Slice models, whose address is the radio's
/// own; never while replaying recordings. Nothing is ever switched on its own: only the operator's click changes
/// operate, standby or bypass, and never while anything is on the air or the tuner is tuning.
/// </summary>
public sealed class FlexAccessoryService : IAsyncDisposable
{
    /// <summary>Said when a change is asked for while something is on the air.</summary>
    public const string OnAir = "Wait until this transmission ends to switch the amplifier or tuner.";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private readonly Session _session;
    private readonly AppSettingsAccessor _settings;
    private readonly Func<bool> _transmitting;
    private readonly bool _simulating;
    private readonly Func<string, int, FlexAccessoryClient> _newClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _busy = new(1, 1);
    private FlexAccessoryClient? _client;
    private volatile FlexAccessories? _shown;
    private (string Host, int Port)? _following;
    private bool _applied;
    private bool _disposed;
    private Task _identity = Task.CompletedTask;

    /// <summary>Creates the service. <paramref name="newClient"/> (given host and port) replaces the network in tests.</summary>
    /// <param name="session">Where the connection's status is shown (Diagnostics).</param>
    /// <param name="settings">The station's radio settings.</param>
    /// <param name="transmitting">True while anything is on the air (PTT, a transmission or a tune).</param>
    /// <param name="simulating">True while replaying recordings: no connection is made.</param>
    /// <param name="newClient">Makes the connection.</param>
    public FlexAccessoryService(Session session, AppSettingsAccessor settings, Func<bool> transmitting, bool simulating,
                                Func<string, int, FlexAccessoryClient>? newClient = null)
    {
        _session = session;
        _settings = settings;
        _transmitting = transmitting;
        _simulating = simulating;
        _newClient = newClient ?? ((host, port) => new FlexAccessoryClient(host, port));
    }

    /// <summary>Raised when the amplifier or tuner changes, on a background thread.</summary>
    public event Action? Changed;

    /// <summary>What the radio reports now; empty when not connected or not a FlexRadio.</summary>
    public FlexAccessories Current => _shown ?? _client?.Current ?? FlexAccessories.Empty;

    /// <summary>False while anything is on the air, the amplifier says it is transmitting, or the tuner is tuning.</summary>
    public bool CanChange =>
        !_transmitting() && Current is var c && c.Amplifier?.Transmitting != true && c.Tuner?.Tuning != true;

    /// <summary>Shows <paramref name="accessories"/> as if the radio had reported them (layout tests, no radio).</summary>
    internal void ShowForTests(FlexAccessories accessories)
    {
        _shown = accessories;
        Changed?.Invoke();
    }

    /// <summary>Waits until the latest setting change has been applied (tests).</summary>
    internal Task Settled => _identity;

    /// <summary>
    /// The radio's address when <paramref name="rig"/> is a SmartSDR Slice model with a host given, else null. The
    /// port given is used (Hamlib connects to the same API), or 4992 when none is.
    /// </summary>
    public static (string Host, int Port)? RadioAddress(RigSettings rig)
    {
        if (rig.Mode != RigModes.Hamlib || rig.Model is not { } m || !HamlibModel.IsSmartSdrSlice(m)) return null;
        var p = rig.Port?.Trim();
        if (string.IsNullOrEmpty(p)) return null;
        var colon = p.LastIndexOf(':');
        if (colon < 0) return (p, FlexAccessoryClient.DefaultPort);
        var host = p[..colon].Trim();
        if (host.Length == 0 || !int.TryParse(p[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            return null;
        return (host, port);
    }

    /// <summary>Connects when the radio is a FlexRadio set up as a SmartSDR Slice model; later radio changes follow.</summary>
    public void Start()
    {
        _settings.Changed += OnSettingsChanged;
        _identity = Task.Run(() => FollowAsync(_settings.Current));
    }

    private void OnSettingsChanged(AppSettings s) =>
        _identity = _identity.ContinueWith(_ => FollowAsync(s), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();

    private async Task FollowAsync(AppSettings s)
    {
        var want = _simulating ? null : RadioAddress(s.Profile.Rig);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || (_applied && want == _following)) return;
            _applied = true;
            await StopAsync().ConfigureAwait(false);
            _following = want;
            if (want is not { } a)
            {
                _session.SetService(ServiceNames.Accessories, ServiceStatus.Off(_simulating
                    ? "Not while replaying recordings"
                    : "Only for a FlexRadio set up as SmartSDR Slice A to H"));
                return;
            }
            _session.SetService(ServiceNames.Accessories, new ServiceStatus(ServiceHealth.Degraded, $"Connecting to the radio at {a.Host}", DateTime.UtcNow));
            var client = _newClient(a.Host, a.Port);
            client.StatusChanged += st =>
            {
                if (ReferenceEquals(client, _client)) _session.SetService(ServiceNames.Accessories, st);
            };
            client.Changed += now =>
            {
                if (!ReferenceEquals(client, _client)) return;
                Log.Information("FlexRadio accessories: amplifier {Amp}, tuner {Tuner}", now.Amplifier, now.Tuner);
                Changed?.Invoke();
            };
            _client = client;
            client.Start();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopAsync()
    {
        var c = _client;
        _client = null;
        if (c is null) return;
        await c.DisposeAsync().ConfigureAwait(false);
        Changed?.Invoke();
    }

    /// <summary>Puts the amplifier in operate or standby. Returns null when the radio took it, else one line saying why not.</summary>
    public Task<string?> SetAmpOperateAsync(bool operate) => ChangeAsync(c =>
    {
        if (c.Amplifier is not { } amp) return (null, "No amplifier is paired with the radio.");
        if (amp.Transmitting) return (null, OnAir);
        if (operate && amp.State is FlexAmpState.Unknown or FlexAmpState.PowerUp or FlexAmpState.SelfCheck)
            return (null, "The amplifier is still starting up.");
        if (amp.Operate == operate) return (null, null);
        return (FlexCommands.AmpOperate(amp.Handle, operate), null);
    }, operate ? "Amplifier to operate" : "Amplifier to standby");

    /// <summary>Puts the tuner in operate or standby. Returns null when the radio took it, else one line saying why not.</summary>
    public Task<string?> SetTunerOperateAsync(bool operate) => ChangeAsync(c =>
    {
        if (c.Tuner is not { } t) return (null, "No tuner is paired with the radio.");
        if (t.Tuning) return (null, "Wait until the tuner has finished tuning.");
        if (t.Operate == operate) return (null, null);
        return (FlexCommands.TunerOperate(t.Handle, operate), null);
    }, operate ? "Tuner to operate" : "Tuner to standby");

    /// <summary>Bypasses the tuner or puts it back in line. Returns null when the radio took it, else one line saying why not.</summary>
    public Task<string?> SetTunerBypassAsync(bool bypass) => ChangeAsync(c =>
    {
        if (c.Tuner is not { } t) return (null, "No tuner is paired with the radio.");
        if (t.Tuning) return (null, "Wait until the tuner has finished tuning.");
        if (!t.Operate) return (null, "Put the tuner in operate first.");
        if (t.Bypass == bypass) return (null, null);
        return (FlexCommands.TunerBypass(t.Handle, bypass), null);
    }, bypass ? "Tuner bypassed" : "Tuner in line");

    // One change at a time, checked again once it has its turn, so a transmission that started meanwhile still stops it.
    private async Task<string?> ChangeAsync(Func<FlexAccessories, (string? Command, string? Refusal)> plan, string what)
    {
        if (_transmitting()) return OnAir;
        await _busy.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_transmitting()) return OnAir;
            if (_client is not { } client) return "Not connected to the radio.";
            var (command, refusal) = plan(client.Current);
            if (refusal is not null || command is null) return refusal;
            string? result;
            using (var cts = new CancellationTokenSource(Timeout))
                result = await client.SendAsync(command, cts.Token).ConfigureAwait(false);
            Log.Information("{What} ({Command}): {Result}", what, command, result ?? "taken");
            return result;
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _settings.Changed -= OnSettingsChanged;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
