// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Blocking;

/// <summary>
/// The operator's block list (owner request 2026-11-22, from user feedback): calls that keep calling on the same band.
/// A blocked call is left out of the station list, Next in line, Calling you and Auto-call, and a call from it to my CQ
/// is not answered. Raw decodes, the decode journal and other programs still get everything that was heard.
/// </summary>
public sealed class BlockList
{
    private readonly IReadOnlyList<BlockedCall> _entries;

    /// <summary>Creates the list.</summary>
    public BlockList(IEnumerable<BlockedCall> entries) => _entries = entries.ToList();

    /// <summary>The empty list.</summary>
    public static BlockList Empty { get; } = new([]);

    /// <summary>The entries.</summary>
    public IReadOnlyList<BlockedCall> Entries => _entries;

    /// <summary>True when the list has no entries.</summary>
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>True when <paramref name="call"/> is blocked on <paramref name="band"/> in <paramref name="mode"/>. False for null.</summary>
    public bool IsBlocked(string? call, string band, Mode mode)
    {
        if (call is null || _entries.Count == 0) return false;
        foreach (var e in _entries)
        {
            if (e.Blocks(call, band, mode)) return true;
        }
        return false;
    }
}
