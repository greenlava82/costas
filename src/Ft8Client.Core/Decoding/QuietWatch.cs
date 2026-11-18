// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Decoding;

/// <summary>
/// Counts the FT8 or FT4 slots in a row that were received and decoded with nothing in them, for the "why is it
/// quiet" line. A slot I transmitted in is skipped (the radio mutes its receive audio), a slot with any decode starts
/// the count again, and so does a band, mode or dial change (<see cref="Reset"/>). A slot that began before the watch
/// started or was reset is only partly recorded, and is not counted. Pure.
/// </summary>
public sealed class QuietWatch
{
    /// <summary>
    /// Empty received slots before the line shows: two, so both an even and an odd slot have passed with nothing
    /// heard. One empty slot is common on a quiet band or when everyone is on the other parity.
    /// </summary>
    public const int SlotsBeforeQuiet = 2;

    private const int TxSlotsKept = 4;

    private readonly List<DateTime> _txSlots = [];
    private DateTime _fromUtc;
    private DateTime? _lastSlotUtc;

    /// <summary>Starts watching at <paramref name="startUtc"/>.</summary>
    public QuietWatch(DateTime startUtc) => _fromUtc = startUtc;

    /// <summary>Received slots in a row with no decodes.</summary>
    public int EmptySlots { get; private set; }

    /// <summary>True once <see cref="SlotsBeforeQuiet"/> received slots in a row had no decodes.</summary>
    public bool Quiet => EmptySlots >= SlotsBeforeQuiet;

    /// <summary>Starts counting again from <paramref name="nowUtc"/> (band, mode or dial change).</summary>
    public void Reset(DateTime nowUtc)
    {
        _fromUtc = nowUtc;
        EmptySlots = 0;
        _lastSlotUtc = null;
        _txSlots.Clear();
    }

    /// <summary>Notes that I transmitted in the slot starting at <paramref name="slotStartUtc"/>.</summary>
    public void Transmitted(DateTime slotStartUtc)
    {
        if (_txSlots.Contains(slotStartUtc)) return;
        _txSlots.Add(slotStartUtc);
        if (_txSlots.Count > TxSlotsKept) _txSlots.RemoveAt(0);
    }

    /// <summary>Takes a decoded slot and how many decodes it had (0 when the decoder failed).</summary>
    public void Slot(DateTime slotStartUtc, int decodes)
    {
        if (slotStartUtc < _fromUtc) return;
        if (decodes > 0)
        {
            EmptySlots = 0;
            _lastSlotUtc = slotStartUtc;
            return;
        }
        // The same slot twice (an early pass, then the full decode) is one slot.
        if (_lastSlotUtc is { } last && slotStartUtc <= last) return;
        _lastSlotUtc = slotStartUtc;
        if (_txSlots.Contains(slotStartUtc)) return;
        EmptySlots++;
    }
}
