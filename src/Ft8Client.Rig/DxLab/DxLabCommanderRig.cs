// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Globalization;
using Ft8Client.Core.Time;
using Ft8Client.Core.Transmit;

namespace Ft8Client.Rig.DxLab;

/// <summary>
/// The radio through DXLab Commander (owner request 2026-11-22): Commander keeps the radio's serial port, so DXLab and
/// Costas share the radio as WSJT-X does with its "DX Lab Suite Commander" rig. Frequency, mode, split and PTT only, with
/// the same commands WSJT-X sends; Commander has no meter, power or tuner commands, so those read as unknown.
/// </summary>
public sealed class DxLabCommanderRig : IRig
{
    /// <summary>How long Commander may take to report the radio keyed or unkeyed after CmdTX or CmdRX (WSJT-X waits 1 s).</summary>
    public static readonly TimeSpan PttConfirmTimeout = TimeSpan.FromSeconds(1);

    private readonly CommanderClient _client;
    private readonly IClock _clock;
    private readonly CultureInfo _culture;
    private readonly bool _pttViaCat;
    private long _lastFrequency;
    private bool _ptt;
    private int _readFailures;
    private bool _disposed;

    private DxLabCommanderRig(CommanderClient client, IClock clock, CultureInfo culture, bool pttViaCat)
    {
        _client = client;
        _clock = clock;
        _culture = culture;
        _pttViaCat = pttViaCat;
    }

    /// <summary>
    /// Connects to Commander at <paramref name="hostPort"/> (<c>127.0.0.1:52002</c>; the port defaults to 52002) and
    /// reads the frequency once to check it answers. Throws <see cref="RigException"/> when it does not.
    /// </summary>
    /// <param name="hostPort">Commander's address.</param>
    /// <param name="clock">Clock for fault times.</param>
    /// <param name="culture">The number format Commander uses: this computer's (WSJT-X uses the system locale).</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="timeout">Per-command timeout; 3 s by default.</param>
    /// <param name="pttViaCat">True to key through Commander (CmdTX/CmdRX); false when the radio keys on audio (VOX).</param>
    public static async Task<DxLabCommanderRig> ConnectAsync(string? hostPort, IClock clock, CultureInfo culture, CancellationToken ct, TimeSpan? timeout = null,
        bool pttViaCat = true)
    {
        var (host, port) = ParseAddress(hostPort);
        var client = new CommanderClient(host, port, timeout);
        var rig = new DxLabCommanderRig(client, clock, culture, pttViaCat);
        try
        {
            rig._lastFrequency = await rig.ReadFrequencyAsync(ct).ConfigureAwait(false);
            return rig;
        }
        catch (RigException ex)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw new RigException($"DXLab Commander is not answering at {host}:{port}. Start Commander and check its Network Service settings (a Base Port of 52000 means port 52002). {ex.Message}", ex);
        }
    }

    /// <summary>Host and port from <c>host:port</c>, <c>host</c> or blank (127.0.0.1:52002).</summary>
    public static (string Host, int Port) ParseAddress(string? hostPort)
    {
        var t = (hostPort ?? string.Empty).Trim();
        if (t.Length == 0) return ("127.0.0.1", CommanderProtocol.DefaultPort);
        var colon = t.LastIndexOf(':');
        if (colon > 0 && int.TryParse(t[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and < 65536)
            return (t[..colon].Trim('[', ']'), port);
        return (t, CommanderProtocol.DefaultPort);
    }

    /// <inheritdoc />
    public string Name => $"DXLab Commander ({_client.Host}:{_client.Port})";

    /// <inheritdoc />
    public event EventHandler<RigFault>? Faulted;

    /// <inheritdoc />
    public async Task<RigState> GetStateAsync(CancellationToken ct)
    {
        var hz = await GetFrequencyAsync(ct).ConfigureAwait(false);
        var mode = await ReadAsync(() => _client.QueryAsync(CommanderProtocol.Command("CmdSendMode"), "CmdMode", ct)).ConfigureAwait(false);
        var split = await ReadAsync(() => _client.QueryAsync(CommanderProtocol.Command("CmdSendSplit"), "CmdSplit", ct)).ConfigureAwait(false);
        return new RigState(hz, CommanderProtocol.HamlibMode(mode), _ptt, CommanderProtocol.OnOff(split) == true);
    }

    /// <inheritdoc />
    public async Task<long> GetFrequencyAsync(CancellationToken ct)
    {
        // WSJT-X: "Commander is not reliable on frequency polls while transmitting", so the last receive reading stands.
        if (_ptt && _lastFrequency > 0) return _lastFrequency;
        var hz = await ReadFrequencyAsync(ct).ConfigureAwait(false);
        _lastFrequency = hz;
        return hz;
    }

    private async Task<long> ReadFrequencyAsync(CancellationToken ct)
    {
        var text = await ReadAsync(() => _client.QueryAsync(CommanderProtocol.Command("CmdGetFreq"), "CmdFreq", ct)).ConfigureAwait(false);
        return CommanderProtocol.ParseFrequency(text) ?? throw new RigException($"DXLab Commander sent a frequency Costas cannot read: {text}");
    }

    /// <inheritdoc />
    public async Task SetFrequencyAsync(long hz, CancellationToken ct)
    {
        await CommandAsync(CommanderProtocol.Command("CmdSetFreq", ("xcvrfreq", CommanderProtocol.FormatFrequency(hz, _culture))), ct).ConfigureAwait(false);
        _lastFrequency = hz;
    }

    /// <inheritdoc />
    public Task SetModeAsync(RigMode mode, CancellationToken ct) =>
        CommandAsync(CommanderProtocol.Command("CmdSetMode", ("1", CommanderProtocol.ModeName(mode))), ct);

    /// <inheritdoc />
    public async Task SetPttAsync(bool on, CancellationToken ct)
    {
        if (!_pttViaCat)
        {
            _ptt = on; // VOX: the radio keys on the audio; only the frequency reading needs to know
            return;
        }
        // Set before sending: a CmdTX that times out may still have keyed the radio.
        if (on) _ptt = true;
        var command = CommanderProtocol.Command(on ? "CmdTX" : "CmdRX");
        // Releasing tries hard, whatever the caller's token says: the radio must not stay keyed.
        var token = on ? ct : CancellationToken.None;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _client.SendAsync(command, token).ConfigureAwait(false);
                if (await ConfirmPttAsync(on, token).ConfigureAwait(false))
                {
                    _ptt = on;
                    return;
                }
                throw new RigException(on
                    ? "The radio did not key through DXLab Commander within 1 s. Lower Commander's Command Interval, or check its PTT setting."
                    : "The radio did not unkey through DXLab Commander within 1 s.");
            }
            catch (RigException ex) when (!on && attempt < 3 && !_disposed)
            {
                RaiseFault($"PTT release failed: {ex.Message}");
            }
            catch (RigException ex)
            {
                RaiseFault(on ? ex.Message : $"PTT release failed: {ex.Message}");
                throw;
            }
        }
    }

    // Polls CmdSendTx until Commander reports the wanted state, for up to PttConfirmTimeout (as WSJT-X does).
    private async Task<bool> ConfirmPttAsync(bool on, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + PttConfirmTimeout;
        while (true)
        {
            var state = CommanderProtocol.OnOff(await _client.QueryAsync(CommanderProtocol.Command("CmdSendTx"), "CmdTX", ct).ConfigureAwait(false));
            if (state == on) return true;
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(TimeSpan.FromMilliseconds(10), ct).ConfigureAwait(false); // WSJT-X: don't thrash Commander
        }
    }

    /// <inheritdoc />
    public Task SetSplitAsync(bool on, long txHz, CancellationToken ct) => on
        ? CommandAsync(CommanderProtocol.Command("CmdQSXSplit", ("xcvrfreq", CommanderProtocol.FormatFrequency(txHz, _culture)),
            ("SuppressDual", "Y"), ("SuppressModeChange", "Y")), ct)
        : CommandAsync(CommanderProtocol.Command("CmdSplit", ("1", "off")), ct);

    /// <inheritdoc />
    public async Task<bool?> ReadPttAsync(CancellationToken ct)
    {
        try
        {
            return CommanderProtocol.OnOff(await _client.QueryAsync(CommanderProtocol.Command("CmdSendTx"), "CmdTX", ct).ConfigureAwait(false));
        }
        catch (RigException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task<TxMeterReading> ReadTxMetersAsync(CancellationToken ct) => Task.FromResult(TxMeterReading.None);

    /// <inheritdoc />
    public Task<int?> ReadPowerWattsAsync(CancellationToken ct) => Task.FromResult<int?>(null);

    /// <inheritdoc />
    public Task<RigPower?> ReadPowerAsync(CancellationToken ct) => Task.FromResult<RigPower?>(null);

    /// <inheritdoc />
    public Task<RigPower?> SetPowerWattsAsync(int watts, CancellationToken ct) => Task.FromResult<RigPower?>(null);

    /// <inheritdoc />
    public Task<bool> StartTunerAsync(CancellationToken ct) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<double?> ReadPowerLevelAsync(CancellationToken ct) => Task.FromResult<double?>(null);

    /// <inheritdoc />
    public Task<bool> SetPowerLevelAsync(double level, CancellationToken ct) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool?> ReadSpeechProcessorAsync(CancellationToken ct) => Task.FromResult<bool?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<RigControlValue>> ReadControlsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<RigControlValue>>([]);

    /// <inheritdoc />
    public Task<bool> SetControlAsync(RigControl control, int value, CancellationToken ct) => Task.FromResult(false);

    // A setting that fails is a fault at once: whatever is transmitting must stop.
    private async Task CommandAsync(string command, CancellationToken ct)
    {
        try
        {
            await _client.SendAsync(command, ct).ConfigureAwait(false);
        }
        catch (RigException ex)
        {
            RaiseFault(ex.Message);
            throw;
        }
    }

    // A status read that fails is retried quietly by the next poll, up to RigctldRig.ReadFailuresTolerated in a row (the
    // same rule as for rigctld); after that it is a fault.
    private async Task<string> ReadAsync(Func<Task<string>> read)
    {
        try
        {
            var value = await read().ConfigureAwait(false);
            _readFailures = 0;
            return value;
        }
        catch (RigException ex)
        {
            if (++_readFailures > RigctldRig.ReadFailuresTolerated) RaiseFault(ex.Message);
            throw;
        }
    }

    private void RaiseFault(string message)
    {
        if (!_disposed) Faulted?.Invoke(this, new RigFault(message, _clock.UtcNow));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_ptt && _pttViaCat)
        {
            try
            {
                await _client.SendAsync(CommanderProtocol.Command("CmdRX"), CancellationToken.None).ConfigureAwait(false);
            }
            catch (RigException)
            {
                // Closing anyway: the transmitter has already tried to release PTT and reported a fault if it could not.
            }
        }
        _disposed = true;
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}
