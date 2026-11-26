// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>
/// The messages a WSPR beacon sends for one station, in WSJT-X's order (<c>WSPR_message</c> in WSJT-X 3.0.1
/// <c>widgets/mainwindow.cpp</c>, V-WSPR6 in docs/08-open-questions.md): a call with a slash, or a 6-character grid (unless
/// "prefer type 1"), alternates a plain message with a type 3 message that carries the full locator; any other station sends
/// <c>CALL GRID4 DBM</c> every time. The first message sent is the plain one, then the type 3.
/// </summary>
public sealed class WsprMessageCycle
{
    private readonly WsprMessage[] _messages;
    private int _next;

    /// <summary>Builds the cycle. The messages are not checked here: <see cref="WsprPacker"/> refuses any that cannot be sent.</summary>
    /// <param name="callsign">My callsign, upper case.</param>
    /// <param name="grid">My locator, 4 or 6 characters, upper case.</param>
    /// <param name="dbm">The power to report.</param>
    /// <param name="preferType1">Keep to type 1 even with a 6-character locator.</param>
    public WsprMessageCycle(string callsign, string grid, int dbm, bool preferType1)
    {
        ArgumentNullException.ThrowIfNull(callsign);
        ArgumentNullException.ThrowIfNull(grid);
        var compound = callsign.IndexOf('/') > 0;
        if (compound || (grid.Length == 6 && !preferType1))
        {
            var plain = compound ? WsprMessage.Compound(callsign, dbm) : WsprMessage.Standard(callsign, grid.Length >= 4 ? grid[..4] : grid, dbm);
            _messages = [plain, WsprMessage.Hashed(callsign, grid, dbm)];
        }
        else
        {
            _messages = [WsprMessage.Standard(callsign, grid.Length >= 4 ? grid[..4] : grid, dbm)];
        }
    }

    /// <summary>Every message the cycle sends, so all of them can be checked before the beacon starts.</summary>
    public IReadOnlyList<WsprMessage> Messages => _messages;

    /// <summary>The next message, advancing the cycle.</summary>
    public WsprMessage Next()
    {
        var m = _messages[_next];
        _next = (_next + 1) % _messages.Length;
        return m;
    }
}
