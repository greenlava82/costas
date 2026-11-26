// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>
/// Which 2-minute slots a WSPR beacon transmits in, by WSJT-X's method (owner decision 2026-11-26): a table of 60 slots for
/// each 2-hour block of the UTC day, rebuilt when the block or the percentage changes. A port of <c>next_tx_state</c> and
/// <c>create_tx_schedule</c> in WSJT-X 3.0.1 <c>WSPR/WsprTxScheduler.cpp</c> (GPLv3), with the random numbers taken from an
/// injected source in place of the C library's <c>rand()</c> (so a Costas beacon follows the same method, not the same slots).
/// </summary>
/// <remarks>
/// Up to 25 %, the gaps between transmissions are random (K1JT's method): at 20 % there are 3 to 5 receive slots between
/// transmissions, never two transmissions in a row, and the first slots of a block are receive. Above 25 % a banded table
/// makes sure every one of the 10 coordinated bands is visited.
/// </remarks>
public sealed class WsprTxSchedule(Random random)
{
    private const int Slots = 60;
    private readonly bool[] _tx = new bool[Slots];
    private (DateTime Block, int Percent)? _built;

    /// <summary>The 60-slot table in use (for tests).</summary>
    public IReadOnlyList<bool> Table => _tx;

    /// <summary>True when the slot that starts at <paramref name="slotStartUtc"/> is a transmit slot at <paramref name="percent"/> %.</summary>
    public bool IsTx(DateTime slotStartUtc, int percent)
    {
        if (percent <= 0) return false;
        if (percent >= 100) return true;
        var block = new DateTime(slotStartUtc.Year, slotStartUtc.Month, slotStartUtc.Day, slotStartUtc.Hour / 2 * 2, 0, 0, DateTimeKind.Utc);
        if (_built is not { } b || b.Block != block || b.Percent != percent)
        {
            Create(percent);
            _built = (block, percent);
        }
        var in20 = (slotStartUtc.Hour - slotStartUtc.Hour / 2 * 2) * 3 + slotStartUtc.Minute / 20;
        var in2 = slotStartUtc.Minute % 20 / 2;
        return _tx[in20 * 10 + in2];
    }

    /// <summary>The band slot, 0 to 9, of a slot within its 20 minutes: the index into the 10 coordinated bands.</summary>
    public static int HoppingIndex(DateTime slotStartUtc) => slotStartUtc.Minute % 20 / 2;

    private void Create(int pctx)
    {
        Array.Clear(_tx);
        var needed = (int)(60 * (pctx / 100.0) + 0.5);
        if (pctx == 0) return;
        int ntxlim, ntxbandmin;
        if (pctx <= 25)
        {
            // K1JT's algorithm: random gaps. rxavg = 100/pct - 1 (4 at 20 %), so 3 to 5 receive slots between transmissions.
            var rxavg = 100.0 / pctx - 1.0;
            var i = 0;
            while (true)
            {
                var x = random.Next(100) / 100.0;
                var nrx = (int)(rxavg + 3.0 * x - 1.0);
                i += nrx + 1;
                if (i < Slots) _tx[i] = true;
                else break;
            }
            return;
        }
        if (pctx < 33) (ntxlim, ntxbandmin) = (1, 1);
        else if (pctx < 50) (ntxlim, ntxbandmin) = (1, 2);
        else if (pctx < 60) (ntxlim, ntxbandmin) = (2, 3);
        else (ntxlim, ntxbandmin) = (3, 4);

        // Above 25 %: a table that visits every band 1 to 4 times, as appropriate.
        for (var i = 0; i < ntxbandmin; i++)
        {
            for (var j = 0; j < 10; j++) AddToBand(j);
        }
        var sum = Trim(ntxlim);
        var tries = 0;
        var done = false;
        while (tries < 100 && !done)
        {
            done = true;
            for (var band = 0; band < 10; band++)
            {
                var bsum = BandSum(band);
                if (bsum >= ntxbandmin) continue;
                done = false;
                for (var k = 0; k < ntxbandmin - bsum; k++) AddToBand(band);
            }
            sum = Trim(ntxlim);
            tries++;
        }
        for (var j = 0; j < needed - sum; j++) AddOne();
    }

    // tx_add_to_band: a transmit slot in a band's column (the table is 6 rows of 10), without regard to the run limit.
    private bool AddToBand(int band)
    {
        for (var i = 0; i < 10; i++)
        {
            var islot = random.Next(6);
            if (!_tx[islot * 10 + band])
            {
                _tx[islot * 10 + band] = true;
                return true;
            }
        }
        return false;
    }

    private int BandSum(int band)
    {
        var s = 0;
        for (var i = 0; i < 6; i++) s += _tx[i * 10 + band] ? 1 : 0;
        return s;
    }

    // tx_trim: no more than ntxlim transmissions in a row, and the first slot is never one (so two tables never meet in two).
    private int Trim(int ntxlim)
    {
        _tx[0] = false;
        var nrun = 0;
        for (var i = 0; i < Slots; i++)
        {
            if (_tx[i])
            {
                nrun++;
                if (nrun > ntxlim)
                {
                    _tx[i] = false;
                    nrun = 0;
                }
            }
            else
            {
                nrun = 0;
            }
        }
        return _tx.Count(t => t);
    }

    // tx_add_one: one more transmission in the biggest gap, never next to another.
    private bool AddOne()
    {
        for (var ngap = 13; ngap >= 3; ngap -= 2)
        {
            for (var i = 0; i < Slots - ngap; i++)
            {
                var any = false;
                for (var j = 0; j < ngap; j++) any |= _tx[i + j];
                if (!any)
                {
                    _tx[i + ngap / 2] = true;
                    return true;
                }
            }
        }
        if (!_tx[58] && !_tx[59])
        {
            _tx[59] = true;
            return true;
        }
        return false;
    }
}
