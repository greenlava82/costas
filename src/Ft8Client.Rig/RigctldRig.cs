// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Ft8Client.Core.Processes;
using Ft8Client.Core.Time;
using Ft8Client.Core.Transmit;

namespace Ft8Client.Rig;

/// <summary>
/// Radio control through a supervised <c>rigctld</c> child process. On a failed command the fault is raised, the
/// process is restarted with back-off (1, 2, 5, 10 s), and PTT release is retried over a fresh connection. A status
/// read (the dial, the mode) that fails while rigctld is still running is retried by the next poll instead: up to
/// <see cref="ReadFailuresTolerated"/> in a row are not a fault, because RF in the CAT cable at high power garbles an
/// odd reply without the radio link being lost, and stopping the transmission and killing rigctld for each one made
/// every high-power transmission end within seconds (issue reported by DF2RG, IC-7300 at 100 W).
/// </summary>
public sealed class RigctldRig : IRig
{
    private static readonly TimeSpan[] RestartDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    // The back-off wait before a restart; a test holds it to look at the rig while rigctld is down.
    internal Func<TimeSpan, CancellationToken, Task> RestartWaitForTests { get; set; } = Task.Delay;

    /// <summary>
    /// Failed status reads in a row that are retried quietly while rigctld runs; the next one is a fault. Commands that
    /// change the radio (PTT, frequency, mode) are never retried quietly, and neither is rigctld exiting.
    /// </summary>
    public const int ReadFailuresTolerated = 2;

    /// <summary>
    /// How long a status read may take. Longer than Hamlib's own worst case for one command on a garbled link (the Icom
    /// drivers wait 1 s and retry 3 times, about 4 s), so a reply Hamlib recovers by retrying is not cut off first, as
    /// WSJT-X, which calls Hamlib directly, never cuts it off. Reads have their own connection, so PTT commands never
    /// wait behind one in Costas.
    /// </summary>
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long meter reads stop after one fails, so a struggling CAT link carries only what matters.</summary>
    public static readonly TimeSpan MeterPause = TimeSpan.FromSeconds(15);

    private readonly RigctldOptions _options;
    private readonly IProcessGuard _guard;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _restartLock = new(1, 1);
    private Process? _process;
    private Process? _live;
    private volatile RigctldClient? _client;
    private RigctldClient? _meters;
    private RigctldClient? _reads;
    private int _port;
    private readonly Func<int> _pickPort;
    private int _restarts;
    private int _backoff;
    private bool _modeFallback;
    private volatile bool _disposed;
    private volatile bool _pttMayBeOn;
    private int _readFailures;
    private long _metersPausedUntilTicks;
    private readonly Queue<string> _stderr = new();

    private RigctldRig(RigctldOptions options, IProcessGuard guard, IClock clock, Func<int>? pickPort = null)
    {
        _options = options;
        _guard = guard;
        _clock = clock;
        _pickPort = pickPort ?? FreePort;
    }

    /// <inheritdoc />
    public string Name => $"Hamlib model {_options.Model}";

    /// <summary>Restarts so far.</summary>
    public int Restarts => _restarts;

    /// <summary>CAT round trip of the last command.</summary>
    public TimeSpan LastRoundTrip => _client?.LastRoundTrip ?? TimeSpan.Zero;

    /// <summary>True while the child process runs.</summary>
    public bool ProcessRunning => _process is { HasExited: false };

    /// <summary>
    /// The running rigctld's process id, for tests that kill it to check recovery. Tests run in parallel, each with its
    /// own rigctld, so killing every process named rigctld would break the others. An id rather than the Process: the
    /// supervisor disposes its Process as soon as it sees the exit, so a test waiting on that object could find it gone.
    /// </summary>
    internal int? ProcessIdForTests => _process?.Id;

    /// <inheritdoc />
    public event EventHandler<RigFault>? Faulted;

    /// <summary>
    /// Raised for a failed read that is retried quietly (see <see cref="ReadFailuresTolerated"/>) or a failed meter read,
    /// with what went wrong, so the log shows a CAT link that is struggling before it fails outright.
    /// </summary>
    public event EventHandler<string>? ReadFailed;

    /// <summary>Starts rigctld and waits until it answers.</summary>
    public static Task<RigctldRig> StartAsync(RigctldOptions options, IProcessGuard guard, IClock clock, CancellationToken ct) =>
        StartAsync(options, guard, clock, ct, pickPort: null);

    /// <summary>As above, with the TCP port for rigctld chosen by <paramref name="pickPort"/> (tests force a port in use).</summary>
    internal static async Task<RigctldRig> StartAsync(RigctldOptions options, IProcessGuard guard, IClock clock, CancellationToken ct, Func<int>? pickPort)
    {
        var rig = new RigctldRig(options, guard, clock, pickPort);
        await rig.LaunchAsync(ct).ConfigureAwait(false);
        return rig;
    }

    /// <summary>
    /// Port auto-scan: tries each baud rate on every port, starting rigctld and asking for the frequency, up to 3 s per
    /// attempt. Ports are tried at the same time (each has its own rigctld), so the search takes about as long as one
    /// port's baud rates. Returns the first combination to read back a plausible frequency. <paramref name="progress"/> hears each port
    /// and baud rate before it is tried. <paramref name="config"/> is passed as rigctld's <c>-C</c> (an Icom CI-V
    /// address must be set before the radio can answer).
    /// </summary>
    public static async Task<(string Port, int Baud, long Hz)?> ScanAsync(string rigctldPath, int model, IEnumerable<string> ports, IEnumerable<int> bauds,
                                                                     IProcessGuard guard, IClock clock, CancellationToken ct,
                                                                     IProgress<(string Port, int Baud)>? progress = null, string? config = null)
    {
        var portList = ports.ToList();
        var baudList = bauds.ToList();
        using var found = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var hits = new (int Baud, long Hz)?[portList.Count];

        async Task TryPortAsync(int index)
        {
            var port = portList[index];
            foreach (var baud in baudList)
            {
                if (found.IsCancellationRequested) return;
                progress?.Report((port, baud));
                try
                {
                    await using var rig = await StartAsync(new RigctldOptions(rigctldPath, model, port, baud, Config: config), guard, clock, found.Token)
                        .ConfigureAwait(false);
                    var hz = await rig._client!.GetFrequencyAsync(found.Token).ConfigureAwait(false);
                    if (hz is > 100_000 and < 1_000_000_000)
                    {
                        hits[index] = (baud, hz);
                        await found.CancelAsync().ConfigureAwait(false);
                        return;
                    }
                }
                catch (RigException)
                {
                }
                catch (OperationCanceledException) when (found.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    return; // another port answered
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, portList.Count).Select(i => Task.Run(() => TryPortAsync(i), CancellationToken.None))).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        for (var i = 0; i < portList.Count; i++)
        {
            if (hits[i] is { } h) return (portList[i], h.Baud, h.Hz);
        }
        return null;
    }

    /// <summary>Serial ports on this computer.</summary>
    public static IReadOnlyList<string> SerialPorts() =>
        CleanSerialPorts(System.IO.Ports.SerialPort.GetPortNames(), OperatingSystem.IsWindows());

    /// <summary>
    /// A saved serial port with any trailing garbage removed (settings written before <see cref="CleanSerialPorts"/>
    /// can hold "COM3" followed by registry junk). Network addresses and Unix device paths are returned unchanged.
    /// </summary>
    public static string? CleanSerialPort(string? port, bool windows) =>
        windows && port is not null && port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            ? CleanSerialPorts([port], windows).FirstOrDefault() ?? port
            : port;

    /// <summary>
    /// Windows drivers can return COM names with trailing registry garbage. Keep only the COM number,
    /// discard invalid names and duplicates, and leave Unix device paths intact.
    /// </summary>
    public static IReadOnlyList<string> CleanSerialPorts(IEnumerable<string> ports, bool windows) =>
        ports.Select(p => windows
                ? System.Text.RegularExpressions.Regex.Match(p, @"^COM[0-9]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Value.ToUpperInvariant()
                : p)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToList();

    /// <inheritdoc />
    public async Task<RigState> GetStateAsync(CancellationToken ct)
    {
        var f = await Read(c => c.GetFrequencyAsync(ct), ct).ConfigureAwait(false);
        var m = await Read(c => c.SendAsync("m", ct), ct).ConfigureAwait(false);
        var t = await Read(c => c.SendAsync("t", ct), ct).ConfigureAwait(false);
        var s = await Read(c => c.SendAsync("s", ct), ct).ConfigureAwait(false);
        return new RigState(f, m["Mode"] ?? "?", t["PTT"] == "1", s["Split"] == "1", RigState.ParsePassband(m["Passband"]));
    }

    /// <inheritdoc />
    public Task<long> GetFrequencyAsync(CancellationToken ct) => Read(c => c.GetFrequencyAsync(ct), ct);

    /// <inheritdoc />
    public Task SetFrequencyAsync(long hz, CancellationToken ct) =>
        Run(c => c.SetAsync($"F {hz.ToString(System.Globalization.CultureInfo.InvariantCulture)}", ct), ct);

    /// <inheritdoc />
    public async Task SetModeAsync(RigMode mode, CancellationToken ct)
    {
        if (mode == RigMode.PktUsb && !_modeFallback)
        {
            try
            {
                await Run(c => c.SetAsync("M PKTUSB -1", ct), ct).ConfigureAwait(false);
                return;
            }
            catch (RigException ex) when (ex.Refused)
            {
                _modeFallback = true; // the radio rejects PKTUSB: use USB from now on
            }
        }
        await Run(c => c.SetAsync("M USB -1", ct), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetPttAsync(bool on, CancellationToken ct)
    {
        if (on)
        {
            // Set before sending: a "T 1" that times out may still have keyed the radio.
            _pttMayBeOn = true;
            try
            {
                await Run(c => c.SetAsync("T 1", ct), ct).ConfigureAwait(false);
            }
            catch (RigException ex) when (ex.LinkLostCode && !_disposed)
            {
                // Hamlib answers every command with RPRT -1 once it has lost the radio's port (comm_state 0), so a PTT
                // command refused means the link is gone even though rigctld is running: restart it to reopen the port.
                RaiseFault(LostLinkText(ex));
                _ = Task.Run(() => RestartAsync(CancellationToken.None), CancellationToken.None);
                throw new RigException(LostLinkText(ex));
            }
            return;
        }
        // Releasing PTT: try hard, over a fresh connection if needed. A restart already under way (started by the
        // fault that stopped the transmission) is waited for, so "T 0" goes to the new rigctld, not the one being killed.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var c = _client ?? throw new RigException("rigctld is not running.");
                await c.SetAsync("T 0", CancellationToken.None).ConfigureAwait(false);
                _pttMayBeOn = false;
                return;
            }
            catch (Exception ex) when (ex is RigException or ObjectDisposedException)
            {
                var refused = ex is RigException { LinkLostCode: true };
                RaiseFault(refused ? $"PTT release failed: {LostLinkText((RigException)ex)}" : $"PTT release failed: {ex.Message}");
                if (_disposed) break;
                await _restartLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                _restartLock.Release();
                // A refused "T 0" comes from a rigctld that lost the radio's port (see SetPttAsync): only a new rigctld
                // reopens it, and it sends "T 0" itself once it is up.
                if (!ProcessRunning || refused) await RestartAsync(CancellationToken.None).ConfigureAwait(false);
                if (!_pttMayBeOn) return;
            }
        }
        throw new RigException("PTT could not be released through rigctld. Switch the radio off or unplug the PTT line.");
    }

    /// <inheritdoc />
    public async Task<TxMeterReading> ReadTxMetersAsync(CancellationToken ct)
    {
        // Its own connection, so a slow meter reply never holds up a PTT command waiting on the main client's lock.
        var m = _meters;
        if (m is null || !ProcessRunning || MetersPaused) return TxMeterReading.None;
        try
        {
            var alc = await m.GetLevelAsync("ALC", ct).ConfigureAwait(false);
            var swr = await m.GetLevelAsync("SWR", ct).ConfigureAwait(false);
            // Power output in watts from the radio's PO meter (Hamlib 4.6 and later; older releases answer RPRT -1 at
            // once, without asking the radio).
            var watts = await m.GetLevelAsync("RFPOWER_METER_WATTS", ct).ConfigureAwait(false);
            return new TxMeterReading(alc, swr, watts);
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
            PauseMeters(ex);
            return TxMeterReading.None;
        }
    }

    /// <inheritdoc />
    public async Task<bool?> ReadPttAsync(CancellationToken ct)
    {
        // The meter connection, for the same reason as the meters: a PTT command must never wait behind this read.
        var m = _meters;
        if (m is null || !ProcessRunning) return null;
        try
        {
            var t = await m.SendAsync("t", ct).ConfigureAwait(false);
            return t["PTT"] switch
            {
                "1" or "2" or "3" => true,
                "0" => false,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The operator's figure for the most power the radio can send, or null to use Hamlib's power2mW table. Set when the
    /// table is wrong for the radio (an FTDX10 shown as 0 to 200 W): the power setting is then read and set as a share
    /// of this figure, and power2mW is not asked. Read on every power read or change, so a new figure takes effect at once.
    /// </summary>
    public Func<int?>? MaxPowerOverride { get; set; }

    private int? OverrideWatts => MaxPowerOverride?.Invoke() is int w and > 0 ? w : null;

    /// <inheritdoc />
    public async Task<int?> ReadPowerWattsAsync(CancellationToken ct)
    {
        // The meter connection, for the same reason as the meters. RFPOWER is the power setting as a fraction of the
        // radio's maximum; power2mW turns it into milliwatts using the driver's table for the frequency and mode.
        var m = _meters;
        if (m is null || !ProcessRunning || MetersPaused) return null;
        try
        {
            var level = await m.GetLevelAsync("RFPOWER", ct).ConfigureAwait(false);
            if (level is not > 0) return null;
            if (OverrideWatts is { } top) return (int)Math.Round(Math.Min(level.Value, 1) * top);
            var hz = await m.GetFrequencyAsync(ct).ConfigureAwait(false);
            var mode = (await m.SendAsync("m", ct).ConfigureAwait(false))["Mode"];
            if (string.IsNullOrEmpty(mode)) return null;
            var mw = await m.PowerToMilliwattsAsync(level.Value, hz, mode, ct).ConfigureAwait(false);
            return TxPower.Watts(mw);
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
            PauseMeters(ex);
            return null;
        }
    }

    /// <summary>Hamlib model number of the Yaesu FT-710.</summary>
    public const int Ft710Model = 1049;

    /// <summary>
    /// False when this Hamlib release is known to scale the power setting wrongly for the radio, so setting it in watts
    /// would send a different power: Hamlib before 4.6 reads and sets the FT-710's <c>PC</c> (watts, 5 to 100) as a
    /// fraction of 255, so asking for 25 W would send <c>PC064</c>. An unknown version counts as wrong for the FT-710.
    /// </summary>
    public static bool PowerScaleTrusted(int model, Version? hamlib) => model != Ft710Model || hamlib >= new Version(4, 6);

    /// <inheritdoc />
    public async Task<RigPower?> ReadPowerAsync(CancellationToken ct)
    {
        // The meter connection, as ReadPowerWattsAsync. The most the radio can be set to is power2mW of a full-scale 1.0.
        var m = _meters;
        if (m is null || !ProcessRunning || !PowerScaleTrusted(_options.Model, HamlibVersion.Of(_options.ExecutablePath))) return null;
        try
        {
            var level = await m.GetLevelAsync("RFPOWER", ct).ConfigureAwait(false);
            if (level is not (>= 0 and <= 1)) return null;
            if (OverrideWatts is { } top) return new RigPower((int)Math.Round(level.Value * top), top);
            var (hz, mode) = await TuningAsync(m, ct).ConfigureAwait(false);
            if (mode is null) return null;
            var max = TxPower.Watts(await m.PowerToMilliwattsAsync(1.0, hz, mode, ct).ConfigureAwait(false));
            var now = TxPower.Watts(await m.PowerToMilliwattsAsync(level.Value, hz, mode, ct).ConfigureAwait(false));
            return max is { } mx && now is { } w ? new RigPower(Math.Min(w, mx), mx) : null;
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<RigPower?> SetPowerWattsAsync(int watts, CancellationToken ct)
    {
        // RFPOWER is a fraction of the radio's maximum on this band and mode, which power2mW of 1.0 gives in milliwatts.
        // Hamlib's drivers scale it linearly, so the fraction is watts over that maximum. The radio rounds or clamps it
        // (the FT-710 accepts 5 to 100 W in steps of 1), so the setting is read back rather than assumed. Sent on the
        // meter connection so a PTT command never waits behind it; a refusal or no answer is not a radio fault.
        var m = _meters;
        if (m is null || !ProcessRunning || watts < TxPower.MinWatts || !PowerScaleTrusted(_options.Model, HamlibVersion.Of(_options.ExecutablePath))) return null;
        try
        {
            double level;
            if (OverrideWatts is { } top)
            {
                level = Math.Clamp((double)watts / top, 0, 1);
            }
            else
            {
                var (hz, mode) = await TuningAsync(m, ct).ConfigureAwait(false);
                if (mode is null) return null;
                var maxMw = await m.PowerToMilliwattsAsync(1.0, hz, mode, ct).ConfigureAwait(false);
                if (maxMw is not > 0) return null;
                level = Math.Clamp(watts * 1000.0 / maxMw.Value, 0, 1);
            }
            var r = await m.SendAsync(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"L RFPOWER {level:0.####}"), ct).ConfigureAwait(false);
            if (!r.Ok) return null;
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
            return null;
        }
        return await ReadPowerAsync(ct).ConfigureAwait(false);
    }

    private static async Task<(long Hz, string? Mode)> TuningAsync(RigctldClient m, CancellationToken ct)
    {
        var hz = await m.GetFrequencyAsync(ct).ConfigureAwait(false);
        var mode = (await m.SendAsync("m", ct).ConfigureAwait(false))["Mode"];
        return (hz, string.IsNullOrEmpty(mode) ? null : mode);
    }

    /// <inheritdoc />
    public async Task<double?> ReadPowerLevelAsync(CancellationToken ct)
    {
        var c = _client;
        if (c is null || !ProcessRunning) return null;
        try
        {
            var level = await c.GetLevelAsync("RFPOWER", ct).ConfigureAwait(false);
            return level is >= 0 and <= 1 ? level : null;
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetPowerLevelAsync(double level, CancellationToken ct)
    {
        var c = _client;
        if (c is null || !ProcessRunning || level is < 0 or > 1) return false;
        try
        {
            var command = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"L RFPOWER {level:0.###}");
            return (await c.SendAsync(command, ct).ConfigureAwait(false)).Ok;
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool?> ReadSpeechProcessorAsync(CancellationToken ct)
    {
        var c = _client;
        if (c is null || !ProcessRunning) return null;
        try
        {
            var r = await c.SendAsync("u COMP", ct).ConfigureAwait(false);
            if (!r.Ok || r.BareValues.Count == 0) return null;
            return r.BareValues[0] switch
            {
                "1" => true,
                "0" => false,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> StartTunerAsync(CancellationToken ct)
    {
        var c = _client;
        if (c is null || !ProcessRunning) return false;
        try
        {
            return (await c.SendAsync("G TUNE", ct).ConfigureAwait(false)).Ok;
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
            return false;
        }
    }

    private RigCapabilities? _caps;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RigControlValue>> ReadControlsAsync(CancellationToken ct)
    {
        // The meter connection, as the power reads: a PTT command must never wait behind these.
        var m = _meters;
        if (m is null || !ProcessRunning) return [];
        try
        {
            var caps = _caps ??= await ReadCapabilitiesAsync(m, ct).ConfigureAwait(false);
            if (caps is null) return [];
            var list = new List<RigControlValue>();
            foreach (var c in Enum.GetValues<RigControl>())
            {
                if (!caps.Supports(c)) continue;
                var name = RigCapabilities.HamlibName(c);
                int? value;
                if (RigCapabilities.IsFunction(c))
                {
                    value = await m.GetFuncAsync(name, ct).ConfigureAwait(false) is { } on ? (on ? 1 : 0) : null;
                }
                else
                {
                    var v = await m.GetLevelAsync(name, ct).ConfigureAwait(false);
                    value = v is null ? null : c is RigControl.AfGain or RigControl.RfGain
                        ? (int)Math.Round(Math.Clamp(v.Value, 0, 1) * 100)
                        : (int)Math.Round(v.Value);
                }
                if (value is { } x) list.Add(new RigControlValue(c, x, caps.Options(c)));
            }
            return list;
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
            return [];
        }
    }

    // \dump_caps lists what the driver reads and sets; it is read once, as it depends only on the radio model.
    private static async Task<RigCapabilities?> ReadCapabilitiesAsync(RigctldClient m, CancellationToken ct)
    {
        var r = await m.SendAsync("\\dump_caps", ct).ConfigureAwait(false);
        return r.Ok ? RigCapabilities.Parse(k => r[k]) : null;
    }

    /// <inheritdoc />
    public async Task<bool> SetControlAsync(RigControl control, int value, CancellationToken ct)
    {
        var m = _meters;
        if (m is null || !ProcessRunning || _caps is not { } caps || !caps.Supports(control)) return false;
        var options = caps.Options(control);
        var ok = control switch
        {
            RigControl.AfGain or RigControl.RfGain => value is >= 0 and <= 100,
            RigControl.Preamp or RigControl.Attenuator or RigControl.Agc => options.Contains(value),
            _ => value is 0 or 1,
        };
        if (!ok) return false;
        var name = RigCapabilities.HamlibName(control);
        var command = RigCapabilities.IsFunction(control)
            ? $"U {name} {value}"
            : control is RigControl.AfGain or RigControl.RfGain
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"L {name} {value / 100.0:0.###}")
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"L {name} {value}");
        try
        {
            return (await m.SendAsync(command, ct).ConfigureAwait(false)).Ok;
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task SetSplitAsync(bool on, long txHz, CancellationToken ct)
    {
        if (on)
        {
            await Run(c => c.SetAsync("S 1 VFOB", ct), ct).ConfigureAwait(false);
            await Run(c => c.SetAsync($"I {txHz.ToString(System.Globalization.CultureInfo.InvariantCulture)}", ct), ct).ConfigureAwait(false);
        }
        else
        {
            await Run(c => c.SetAsync("S 0 VFOA", ct), ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // First, so a command failing because of the kill below does not start a new rigctld that would hold the
        // serial port until Costas exits.
        _disposed = true;
        try
        {
            if (_client is not null) await _client.SetAsync("T 0", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RigException or ObjectDisposedException)
        {
        }
        // Wait for a restart in progress to finish (it sees _disposed and launches nothing) before killing.
        await _restartLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopProcessAsync().ConfigureAwait(false);
            if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false);
            if (_meters is not null) await _meters.DisposeAsync().ConfigureAwait(false);
            if (_reads is not null) await _reads.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _restartLock.Release();
        }
    }

    private async Task<T> Run<T>(Func<RigctldClient, Task<T>> f, CancellationToken ct)
    {
        var client = _client;
        try
        {
            // A dead rigctld is a rig error (the transmitter must stop), not something to paper over by restarting quietly.
            if (client is null || !ProcessRunning) throw new RigException("rigctld is not running; restarting it.");
            var result = await f(client).ConfigureAwait(false);
            _backoff = 0; // working again: the next fault starts the back-off from the shortest delay
            Volatile.Write(ref _readFailures, 0);
            return result;
        }
        // A command that was still on the client a restart has since replaced fails, but the fault was raised and the
        // restart done already: restarting again would kill the rigctld that was just started.
        catch (RigException ex) when (!ex.Refused && !_disposed && client is not null && !ReferenceEquals(client, _client))
        {
            throw;
        }
        catch (RigException ex) when (!ex.Refused && !_disposed)
        {
            RaiseFault(ex.Message);
            _ = Task.Run(() => RestartAsync(CancellationToken.None), CancellationToken.None);
            throw;
        }
    }

    // A status read, on its own connection with a longer wait (ReadTimeout). Unlike Run, a failure while rigctld is still running is passed to the caller without a fault or a
    // restart, up to ReadFailuresTolerated in a row: the polls read again a second later. Hamlib's Icom drivers retry a
    // garbled CI-V reply three times with a 1 s timeout, so one bad frame used to outlast the client's 2 s wait without
    // anything being wrong with the link. Treating that as a dead radio stopped the transmission and killed rigctld,
    // whose restart then failed the PTT release ("PTT release failed", "rigctld is not running"). A dead rigctld, a
    // refused command and the PTT, frequency and mode commands all still fault at once.
    private async Task<T> Read<T>(Func<RigctldClient, Task<T>> f, CancellationToken ct)
    {
        var c = _reads;
        if (c is null || !ProcessRunning) return await Run(f, ct).ConfigureAwait(false);
        try
        {
            var result = await f(c).ConfigureAwait(false);
            _backoff = 0;
            Volatile.Write(ref _readFailures, 0);
            return result;
        }
        catch (RigException ex) when (!ex.Refused && !_disposed)
        {
            var inARow = Interlocked.Increment(ref _readFailures);
            if (inARow <= ReadFailuresTolerated && ProcessRunning)
            {
                ReadFailed?.Invoke(this, $"Radio read failed ({inARow} in a row), reading again: {ex.Message}");
                throw;
            }
            Volatile.Write(ref _readFailures, 0);
            var why = inARow > 1 ? $"The radio did not answer {inARow} reads in a row ({ex.Message}); restarting rigctld." : ex.Message;
            RaiseFault(why);
            _ = Task.Run(() => RestartAsync(CancellationToken.None), CancellationToken.None);
            throw new RigException(why, ex);
        }
    }

    private bool MetersPaused => _clock.UtcNow.Ticks < Volatile.Read(ref _metersPausedUntilTicks);

    // A meter read that fails stops meter reads for a while: they are display only, and on a CAT link that RF is
    // disturbing every extra command is another chance for a garbled reply that holds up the commands that matter.
    private void PauseMeters(Exception ex)
    {
        if (_disposed || ex is ObjectDisposedException) return;
        Volatile.Write(ref _metersPausedUntilTicks, (_clock.UtcNow + MeterPause).Ticks);
        ReadFailed?.Invoke(this, $"Radio meter read failed; meters paused for {MeterPause.TotalSeconds:0} s: {ex.Message}");
    }

    private async Task Run(Func<RigctldClient, Task> f, CancellationToken ct) =>
        await Run<bool>(async c =>
        {
            await f(c).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

    private static string LostLinkText(RigException ex) =>
        $"rigctld lost its connection to the radio ({ex.Message}); reconnecting. If this happens while transmitting, RF may be getting into the USB cable.";

    private void RaiseFault(string message) => Faulted?.Invoke(this, new RigFault(message, _clock.UtcNow));

    private async Task RestartAsync(CancellationToken ct)
    {
        if (_disposed || !await _restartLock.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            if (_disposed) return;
            var delay = RestartDelays[Math.Min(_backoff, RestartDelays.Length - 1)];
            _backoff++;
            _restarts++;
            await StopProcessAsync().ConfigureAwait(false);
            await RestartWaitForTests(delay, ct).ConfigureAwait(false);
            if (_disposed) return;
            await LaunchAsync(ct).ConfigureAwait(false);
            if (_pttMayBeOn)
            {
                // The rigctld that keyed the radio was killed before it could unkey it: unkey through the new one.
                try
                {
                    await _client!.SetAsync("T 0", CancellationToken.None).ConfigureAwait(false);
                    _pttMayBeOn = false;
                }
                catch (RigException)
                {
                }
            }
        }
        catch (RigException)
        {
        }
        finally
        {
            _restartLock.Release();
        }
    }

    // The TCP port is free when chosen, but another program can take it before rigctld opens it; rigctld then exits at
    // once, saying nothing. Try a fresh port up to twice before giving up.
    private async Task LaunchAsync(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (await LaunchOnceAsync(ct, mayRetryPort: attempt < 3).ConfigureAwait(false)) return;
        }
    }

    // True once rigctld answers; false when it exited because its TCP port was taken and mayRetryPort is set.
    private async Task<bool> LaunchOnceAsync(CancellationToken ct, bool mayRetryPort)
    {
        _port = _pickPort();
        var psi = new ProcessStartInfo(_options.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in _options.Arguments(_port)) psi.ArgumentList.Add(a);
        try
        {
            _process = Process.Start(psi) ?? throw new RigException("rigctld did not start.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new RigException($"rigctld could not be started: {ex.Message}", ex);
        }
        lock (_stderr) _stderr.Clear();
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (_stderr)
            {
                _stderr.Enqueue(e.Data.Trim());
                if (_stderr.Count > 4) _stderr.Dequeue();
            }
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        _guard.Adopt(_process);

        if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false);
        _client = new RigctldClient("127.0.0.1", _port);
        if (_meters is not null) await _meters.DisposeAsync().ConfigureAwait(false);
        _meters = new RigctldClient("127.0.0.1", _port, TimeSpan.FromSeconds(1));
        if (_reads is not null) await _reads.DisposeAsync().ConfigureAwait(false);
        _reads = new RigctldClient("127.0.0.1", _port, ReadTimeout);
        // rigctld listens only once it has opened the radio. A network model opens another program's server first, and
        // Hamlib waits 10 s for each start-up command that server leaves unanswered (\get_powerstat, \chk_vfo,
        // \dump_state), so give those up to 40 s before calling the server silent.
        var deadline = DateTime.UtcNow + (HamlibModel.IsNetworkModel(_options.Model) ? TimeSpan.FromSeconds(40) : TimeSpan.FromSeconds(3));
        while (true)
        {
            try
            {
                await _client.GetFrequencyAsync(ct).ConfigureAwait(false);
                // An answer while our rigctld has already exited came from whatever holds the port, not from ours.
                if (_process.HasExited) throw new RigException("rigctld exited.");
                WatchForExit(_process);
                return true;
            }
            catch (RigException) when (DateTime.UtcNow < deadline && !_process.HasExited)
            {
                try
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Cancelled between tries, typically Setup's port scan once another port has answered. The catch
                    // below does not see an exception thrown here, so this rigctld was left running, holding its serial
                    // port (often the radio's second one) until Costas exited.
                    await StopProcessAsync().ConfigureAwait(false);
                    throw;
                }
            }
            catch (RigException ex)
            {
                var exited = _process.HasExited;
                if (exited) _process.WaitForExit(); // lets the stderr reader finish
                await StopProcessAsync().ConfigureAwait(false);
                // rigctld says why it gave up (a bad setting, the serial port busy or missing) on stderr; that is what
                // the operator needs, not our internal TCP port number.
                string said;
                lock (_stderr) said = string.Join(" ", _stderr);
                if (exited && said.Length == 0 && mayRetryPort && PortInUse(_port)) return false;
                throw new RigException(exited && said.Length > 0
                    ? $"rigctld stopped: {said}"
                    : $"rigctld is not answering: {ex.Message}" + (said.Length > 0 ? $" ({said})" : string.Empty), ex);
            }
            catch (OperationCanceledException)
            {
                // Stopped by the operator (Setup's Stop button): a rigctld left running would hold the serial port,
                // and every later test would find the port in use.
                await StopProcessAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    // From here on an exit we did not ask for is reported at once, even with no command pending, so a transmission in
    // progress is stopped and the process is restarted.
    private void WatchForExit(Process p)
    {
        Volatile.Write(ref _live, p);
        void OnExit()
        {
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _live, null, p), p)) return;
            RaiseFault("rigctld stopped unexpectedly; restarting it.");
            _ = Task.Run(() => RestartAsync(CancellationToken.None), CancellationToken.None);
        }
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => OnExit();
        if (p.HasExited) OnExit();
    }

    private async Task StopProcessAsync()
    {
        var p = _process;
        _process = null;
        Volatile.Write(ref _live, null);
        if (p is null) return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                await p.WaitForExitAsync(new CancellationTokenSource(2000).Token).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) { }
        finally
        {
            p.Dispose();
        }
    }

    private static bool PortInUse(int port)
    {
        try
        {
            var l = new TcpListener(IPAddress.Loopback, port);
            l.Start();
            l.Stop();
            return false;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return true;
        }
    }

    private static readonly Queue<int> RecentPorts = new();

    // A free TCP port for a new rigctld. The port search starts several at once, so a port handed out recently is never
    // handed out again: two rigctlds on one port would let a client read another port's radio.
    internal static int FreePort()
    {
        lock (RecentPorts)
        {
            for (var attempt = 0; ; attempt++)
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                var port = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                if (RecentPorts.Contains(port) && attempt < 20) continue;
                RecentPorts.Enqueue(port);
                if (RecentPorts.Count > 64) RecentPorts.Dequeue();
                return port;
            }
        }
    }
}
