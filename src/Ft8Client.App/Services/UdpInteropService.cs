// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Messages;
using Ft8Client.Data.Adif;
using Ft8Client.Data.Log;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.Wsjtx;
using Serilog;

namespace Ft8Client.App.Services;

/// <summary>
/// WSJT-X UDP interop. Out: heartbeat every 15 s, status on change, decodes, clear on band change, logged contacts (both
/// forms) and close on exit. In: Reply, Halt Tx, Highlight Callsign and Configure addressed to this app, passed on through
/// <see cref="Inbound"/>; Replay is answered here. Lets GridTracker, JTAlert and loggers follow and drive this app unchanged. Everything goes to
/// one address and port, and to a second one when that is on (DXLab SpotCollector and JTAlert cannot share a port).
/// </summary>
public sealed class UdpInteropService : IDisposable
{
    /// <summary>The Id field in every datagram.</summary>
    public const string ClientId = AppInfo.Id;

    private static readonly TimeSpan Pulse = TimeSpan.FromSeconds(15);

    private readonly Session _session;
    private readonly AppSettingsAccessor _settings;
    private readonly bool _offAir;
    private readonly Lock _gate = new();
    private readonly Timer _heartbeat;
    private readonly WsjtxUdpBroadcaster?[] _udp = new WsjtxUdpBroadcaster?[2];
    private readonly (bool Enabled, string Address, int Port)[] _target = new (bool, string, int)[2];
    private WsjtxStatus? _lastStatus;
    private bool _warned;

    /// <summary>Creates the service. <paramref name="offAir"/> marks decodes as from recordings (simulation).</summary>
    public UdpInteropService(Session session, AppSettingsAccessor settings, bool offAir)
    {
        _session = session;
        _settings = settings;
        _offAir = offAir;
        _heartbeat = new Timer(_ => Send(WsjtxMessages.Heartbeat(ClientId, AppInfo.Version, string.Empty)));
    }

    /// <summary>
    /// Reply, Halt Tx or Highlight Callsign from another program, addressed to this app. Raised on a thread-pool thread.
    /// </summary>
    public event Action<WsjtxInboundMessage>? Inbound;

    /// <summary>Opens the socket and starts sending.</summary>
    public void Start()
    {
        Reconfigure(_settings.Current);
        _settings.Changed += Reconfigure;
        _session.Changed += OnChanged;
        _session.SlotDecoded += OnSlotDecoded;
        _session.BandChanged += OnBandChanged;
        _heartbeat.Change(TimeSpan.Zero, Pulse);
    }

    /// <summary>
    /// Sends a contact from the log again, for a logger that was not running when it was logged (Log view, "Send to
    /// loggers again"). Returns one line for the operator: what was sent, or why nothing was.
    /// </summary>
    public string Resend(QsoRecord q)
    {
        ArgumentNullException.ThrowIfNull(q);
        List<(WsjtxUdpBroadcaster? Udp, string Where)> targets;
        lock (_gate)
        {
            targets = Enumerable.Range(0, _target.Length).Where(i => _target[i].Enabled)
                .Select(i => (_udp[i], $"{_target[i].Address}:{_target[i].Port}")).ToList();
        }
        if (targets.Count == 0) return "UDP to other programs is off. Turn it on in Settings › Other programs, then try again.";
        if (targets.FirstOrDefault(t => t.Udp is null) is { Where: { } bad }) return $"Costas cannot send to {bad}. Check the address in Settings › Other programs.";
        var where = string.Join(" and ", targets.Select(t => t.Where));
        var ok = SendLogged(q);
        var error = targets.Select(t => t.Udp!.LastError).FirstOrDefault(e => !string.IsNullOrEmpty(e));
        return ok ? $"Sent {q.Call} to {where} again (QSO Logged and Logged ADIF)."
            : $"Sending {q.Call} to {where} failed: {error}";
    }

    /// <summary>Sends a logged contact as QSO Logged and Logged ADIF.</summary>
    public void OnLogged(QsoRecord q) => SendLogged(q);

    // True when both datagrams went out.
    private bool SendLogged(QsoRecord q)
    {
        var (logged, adif) = LoggedMessages(q, DateTime.UtcNow);
        var first = Send(logged);
        var second = Send(adif);
        return first && second;
    }

    /// <summary>
    /// The two datagrams WSJT-X sends for a logged contact, built from the log row: QSO Logged (what DXLab SpotCollector
    /// passes to DXKeeper, and what JTAlert, Log4OM and GridTracker read) and Logged ADIF (one ADIF record, as uploads
    /// and exports write it).
    /// </summary>
    internal static (byte[] QsoLogged, byte[] LoggedAdif) LoggedMessages(QsoRecord q, DateTime nowUtc) =>
        (WsjtxMessages.QsoLogged(ClientId, new WsjtxQso
        {
            TimeOnUtc = q.QsoDateOn, TimeOffUtc = q.QsoDateOff ?? q.QsoDateOn, DxCall = q.Call, DxGrid = q.Gridsquare ?? string.Empty,
            FrequencyHz = q.FreqHz ?? 0, Mode = q.Submode ?? q.Mode, ReportSent = q.RstSent ?? string.Empty,
            ReportReceived = q.RstRcvd ?? string.Empty, TxPower = q.TxPwr ?? string.Empty, Comments = q.Comment ?? string.Empty, Name = q.Name ?? string.Empty,
            MyCall = q.StationCallsign ?? string.Empty, MyGrid = q.MyGridsquare ?? string.Empty,
        }),
        WsjtxMessages.LoggedAdif(ClientId, AdifWriter.Document([AdifMapper.ToAdif(q)], nowUtc)));

    private void Reconfigure(AppSettings s)
    {
        // The second program only while UDP is on at all, so one switch turns everything off.
        (bool Enabled, string Address, int Port)[] wanted =
            [(s.Udp.Enabled, s.Udp.Address, s.Udp.Port), (s.Udp.Enabled && s.Udp.SecondEnabled, s.Udp.SecondAddress, s.Udp.SecondPort)];
        var opened = false;
        lock (_gate)
        {
            for (var i = 0; i < wanted.Length; i++)
            {
                var t = wanted[i];
                if (t == _target[i] && (_udp[i] is not null || !t.Enabled)) continue;
                _target[i] = t;
                _udp[i]?.Dispose();
                _udp[i] = null;
                _lastStatus = null;
                _warned = false;
                if (!t.Enabled) continue;
                try
                {
                    var udp = new WsjtxUdpBroadcaster(t.Address, t.Port);
                    udp.Received += OnReceived;
                    _udp[i] = udp;
                    opened = true;
                }
                catch (Exception ex) when (ex is FormatException or System.Net.Sockets.SocketException or ArgumentException)
                {
                    Log.Warning("UDP interop off: cannot send to {Address}:{Port}: {Error}", t.Address, t.Port, ex.Message);
                }
            }
        }
        if (opened) Send(WsjtxMessages.Heartbeat(ClientId, AppInfo.Version, string.Empty));
    }

    private void OnChanged(SessionSnapshot s)
    {
        var c = s.Contact;
        var status = new WsjtxStatus
        {
            DialHz = s.DialHz, Mode = ModeInfo.Name(s.Mode), DxCall = c?.DxCall ?? string.Empty,
            Report = StatusReport(s.Mode, c?.ReportSent), TxEnabled = c is not null || s.CallingCq,
            Transmitting = s.Transmitting, RxDf = s.TxOffsetHz, TxDf = s.TxOffsetHz, DeCall = s.MyCall, DeGrid = s.MyGrid,
            DxGrid = c?.DxGrid ?? string.Empty, TrPeriodSeconds = ModeInfo.IsSlotted(s.Mode) ? (int)ModeInfo.SlotLength(s.Mode).TotalSeconds : 0,
            ConfigurationName = _settings.Current.Profile.Name, TxMessage = s.TransmittingMessage ?? string.Empty,
        };
        lock (_gate)
        {
            if (status == _lastStatus) return;
            _lastStatus = status;
        }
        Send(WsjtxMessages.Status(ClientId, status));
    }

    /// <summary>
    /// The report in a Status message as the logger would write it: <c>-07</c> in FT8 and FT4, RST (<c>599</c>) in PSK and
    /// RTTY; empty before a report is sent.
    /// </summary>
    internal static string StatusReport(Mode mode, int? reportSent) => reportSent is { } rs ? LogComment.Rst(mode, rs) : string.Empty;

    private void OnSlotDecoded(DateTime slot, Mode mode, IReadOnlyList<Decode> decodes)
    {
        var name = ModeInfo.Name(mode);
        foreach (var d in decodes)
            Send(WsjtxMessages.Decode(ClientId, new WsjtxDecode(slot, d.Snr, d.Dt, d.OffsetHz, name, d.Text, d.LowConfidence, _offAir)));
    }

    private void OnReceived(byte[] datagram, System.Net.IPEndPoint from)
    {
        var m = WsjtxInbound.Parse(datagram);
        if (m is null || m.TargetId != ClientId) return;
        if (m is WsjtxReplay)
        {
            Log.Information("UDP Replay from {From}", from);
            Replay();
            return;
        }
        if (m is not WsjtxHighlight) Log.Information("UDP {Kind} from {From}", m.GetType().Name, from);
        try
        {
            Inbound?.Invoke(m);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "UDP {Kind} from {From} not handled", m.GetType().Name, from);
        }
    }

    /// <summary>
    /// Answers Replay as WSJT-X does: the decodes still on screen (this band and mode, the last 15 minutes) again, marked
    /// not new, then a Status. Lets SpotCollector or GridTracker catch up when they start after Costas.
    /// </summary>
    private void Replay()
    {
        var s = _session.Snapshot;
        if (!ModeInfo.IsSlotted(s.Mode)) return;
        foreach (var d in ReplayDecodes(s.RawDecodes, s.Mode, DateTime.UtcNow, _offAir))
            Send(WsjtxMessages.Decode(ClientId, d));
        lock (_gate) _lastStatus = null;
        OnChanged(s);
    }

    /// <summary>
    /// The decodes a Replay sends again: received lines (not this station's own transmissions) in <paramref name="mode"/>
    /// from the last 15 minutes, oldest first, marked not new.
    /// </summary>
    public static IEnumerable<WsjtxDecode> ReplayDecodes(IEnumerable<RawDecodeLine> lines, Mode mode, DateTime nowUtc, bool offAir)
    {
        var since = nowUtc - TimeSpan.FromMinutes(15);
        var name = ModeInfo.Name(mode);
        return lines
            .Where(l => !l.Transmitted && l.Mode == mode && l.Decode.SlotStartUtc >= since)
            .OrderBy(l => l.Decode.SlotStartUtc)
            .Select(l => new WsjtxDecode(l.Decode.SlotStartUtc, l.Decode.Snr, l.Decode.Dt, l.Decode.OffsetHz, name, l.Decode.Text,
                l.Decode.LowConfidence, offAir, IsNew: false));
    }

    private void OnBandChanged() => Send(WsjtxMessages.Clear(ClientId));

    // True when the datagram went out to every program it is meant for (false when none is open).
    private bool Send(byte[] datagram)
    {
        WsjtxUdpBroadcaster[] open;
        lock (_gate) open = _udp.OfType<WsjtxUdpBroadcaster>().ToArray();
        if (open.Length == 0) return false;
        var all = true;
        foreach (var udp in open)
        {
            if (udp.Send(datagram)) continue;
            all = false;
            if (_warned) continue;
            _warned = true;
            Log.Warning("UDP interop send failed: {Error}", udp.LastError);
        }
        return all;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _settings.Changed -= Reconfigure;
        _session.Changed -= OnChanged;
        _session.SlotDecoded -= OnSlotDecoded;
        _session.BandChanged -= OnBandChanged;
        _heartbeat.Dispose();
        Send(WsjtxMessages.Clear(ClientId));
        Send(WsjtxMessages.Close(ClientId));
        lock (_gate)
        {
            for (var i = 0; i < _udp.Length; i++)
            {
                _udp[i]?.Dispose();
                _udp[i] = null;
            }
        }
    }
}
