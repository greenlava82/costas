// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Integrations.Wsjtx;

/// <summary>
/// Parses the "In" datagrams this app acts on: Reply (4), Replay (7), Halt Tx (8), Highlight Callsign (13) and Configure
/// (15). Field layouts are
/// from WSJT-X <c>Network/NetworkMessage.hpp</c>.
/// </summary>
public static class WsjtxInbound
{
    /// <summary>Reply.</summary>
    public const uint Reply = 4;

    /// <summary>Replay.</summary>
    public const uint Replay = 7;

    /// <summary>Halt Tx.</summary>
    public const uint HaltTx = 8;

    /// <summary>Highlight Callsign.</summary>
    public const uint Highlight = 13;

    /// <summary>Configure.</summary>
    public const uint Configure = 15;

    /// <summary>
    /// Parses a datagram. Returns null for a datagram that is not WSJT-X, has an unknown schema, is one of the types
    /// this app ignores, or is malformed.
    /// </summary>
    public static WsjtxInboundMessage? Parse(byte[] datagram)
    {
        try
        {
            var r = new QDataStreamParser(datagram);
            if (r.UInt32() != WsjtxMessages.Magic) return null;
            var schema = r.UInt32();
            if (schema < 2 || schema > WsjtxMessages.MaxSchema) return null;
            var type = r.UInt32();
            var id = r.Utf8() ?? string.Empty;
            return type switch
            {
                Reply => new WsjtxReply(id, r.Time(), r.Int32(), r.Double(), r.UInt32(), r.Utf8() ?? string.Empty, r.Utf8() ?? string.Empty, r.Bool(), r.UInt8()),
                Replay => new WsjtxReplay(id),
                HaltTx => new WsjtxHaltTx(id, r.Bool()),
                Highlight => new WsjtxHighlight(id, r.Utf8() ?? string.Empty, r.Color(), r.Color(), r.Bool()),
                Configure => ReadConfigure(id, r),
                _ => null,
            };
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    // Mode, frequency tolerance, submode, fast mode, T/R period, Rx DF, DX call, DX grid, generate messages, in that order.
    private static WsjtxConfigure ReadConfigure(string id, QDataStreamParser r)
    {
        var mode = r.Utf8() ?? string.Empty;
        r.UInt32();
        r.Utf8();
        r.Bool();
        r.UInt32();
        r.UInt32();
        var call = r.Utf8() ?? string.Empty;
        var grid = r.Utf8() ?? string.Empty;
        return new WsjtxConfigure(id, mode, call, grid, r.Bool());
    }
}
