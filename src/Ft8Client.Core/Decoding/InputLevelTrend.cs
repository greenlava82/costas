// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Decoding;

/// <summary>
/// Settles the once-a-second receive level readings into one state for the "why is it quiet" line, so one loud
/// crash or a short pause does not decide it. A state is reported when it held for at least
/// <see cref="SettledReadings"/> of the last <see cref="WindowReadings"/> readings. Seconds spent transmitting are
/// left out by the caller, since the radio mutes its receive audio then. Pure.
/// </summary>
public sealed class InputLevelTrend
{
    /// <summary>Readings kept, one a second.</summary>
    public const int WindowReadings = 10;

    /// <summary>Readings in the window that must agree for a state to be reported.</summary>
    public const int SettledReadings = 7;

    private readonly Queue<(InputLevelState State, double RmsDbfs)> _readings = new();

    /// <summary>The state that held for most of the last ten seconds, or null when none did or too few were taken.</summary>
    public InputLevelState? State { get; private set; }

    /// <summary>The average RMS level over the readings in the window, dBFS, or null with none.</summary>
    public double? RmsDbfs { get; private set; }

    /// <summary>Takes one second's reading. Returns true when <see cref="State"/> changed.</summary>
    public bool Add(InputLevelState state, double rmsDbfs)
    {
        _readings.Enqueue((state, rmsDbfs));
        while (_readings.Count > WindowReadings) _readings.Dequeue();
        RmsDbfs = _readings.Average(r => r.RmsDbfs);
        var top = _readings.GroupBy(r => r.State).MaxBy(g => g.Count());
        var settled = top is not null && top.Count() >= SettledReadings ? top.Key : (InputLevelState?)null;
        var changed = settled != State;
        State = settled;
        return changed;
    }

    /// <summary>Forgets every reading (a new input device). Returns true when <see cref="State"/> changed.</summary>
    public bool Clear()
    {
        var changed = State is not null;
        _readings.Clear();
        State = null;
        RmsDbfs = null;
        return changed;
    }
}
