// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ft8Client.Rig.DxLab;

namespace Ft8Client.Rig.Tests;

/// <summary>
/// A stand-in for DXLab Commander's command port: answers the commands WSJT-X sends, with the reply tags WSJT-X expects.
/// Frequencies are written in kilohertz with a comma group separator, as an English Windows does.
/// </summary>
internal sealed class FakeCommander : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accepting;
    private readonly List<string> _commands = [];
    private int _txPolls;

    public FakeCommander()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    public long FrequencyHz { get; set; } = 14_074_000;

    public string Mode { get; set; } = "DATA-U";

    public bool Split { get; set; }

    public bool Tx { get; private set; }

    /// <summary>CmdSendTx polls that still report the old state after CmdTX or CmdRX (Commander's command interval).</summary>
    public int TxLag { get; set; }

    /// <summary>When set, CmdTX is ignored (the radio never keys).</summary>
    public bool IgnoreTx { get; set; }

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_commands) return _commands.ToList();
        }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var c = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => ServeAsync(c));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ServeAsync(TcpClient c)
    {
        using var _ = c;
        var s = c.GetStream();
        var buffer = new byte[1024];
        var pending = new StringBuilder();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var n = await s.ReadAsync(buffer, _stop.Token);
                if (n <= 0) return;
                pending.Append(Encoding.Latin1.GetString(buffer, 0, n));
                while (true)
                {
                    var text = pending.ToString();
                    if (CommanderProtocol.ReadReply(text, out var used1) is not { Name: "command" } name) break;
                    if (CommanderProtocol.ReadReply(text[used1..], out var used2) is not { Name: "parameters" } p) break;
                    pending.Remove(0, used1 + used2);
                    lock (_commands) _commands.Add(name.Value + p.Value);
                    if (Answer(name.Value, p.Value) is { } reply) await s.WriteAsync(Encoding.Latin1.GetBytes(reply), _stop.Token);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }
    }

    private string? Answer(string command, string parameters)
    {
        var en = CultureInfo.InvariantCulture;
        switch (command)
        {
            case "CmdGetFreq":
                return CommanderProtocol.Field("CmdFreq", CommanderProtocol.FormatFrequency(FrequencyHz, en));
            case "CmdSendMode":
                return CommanderProtocol.Field("CmdMode", Mode);
            case "CmdSendSplit":
                return CommanderProtocol.Field("CmdSplit", Split ? "ON" : "OFF");
            case "CmdSendTx":
                if (_txPolls > 0) _txPolls--;
                var shown = _txPolls > 0 ? !Tx : Tx;
                return CommanderProtocol.Field("CmdTX", shown ? "ON" : "OFF");
            case "CmdTX":
                if (!IgnoreTx)
                {
                    Tx = true;
                    _txPolls = TxLag + 1;
                }
                return null;
            case "CmdRX":
                Tx = false;
                _txPolls = TxLag + 1;
                return null;
            case "CmdSetFreq":
                if (CommanderProtocol.ReadReply(parameters, out _) is { } f) FrequencyHz = CommanderProtocol.ParseFrequency(f.Value) ?? FrequencyHz;
                return null;
            case "CmdSetMode":
                if (CommanderProtocol.ReadReply(parameters, out _) is { } m) Mode = m.Value;
                return null;
            case "CmdQSXSplit":
                Split = true;
                return null;
            case "CmdSplit":
                Split = false;
                return null;
            default:
                return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        // The listener stops only after the accept loop has ended.
        await _accepting;
        _listener.Stop();
        _stop.Dispose();
    }
}
