// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.Audio.Timing;

namespace Ft8Client.App.Services;

/// <summary>The dedicated slot thread: polls the slot clock and the transmitter every millisecond. Never waits on I/O.</summary>
public sealed class SlotLoop : IDisposable
{
    /// <summary>Errors on the slot thread are logged at most this often, as a fault can repeat every millisecond.</summary>
    public static readonly TimeSpan ErrorLogInterval = TimeSpan.FromSeconds(10);

    private readonly SlotClock _clock;
    private readonly Transmitter _tx;
    private readonly Func<long> _ticks;
    private readonly Action<Exception> _logError;
    private Thread? _thread;
    private volatile bool _run;
    private long? _lastErrorTicks; // slot thread only

    /// <summary>Creates the loop.</summary>
    public SlotLoop(SlotClock clock, Transmitter tx)
        : this(clock, tx, () => Environment.TickCount64, ex => Serilog.Log.Error(ex, "Slot thread error; transmit halted"))
    {
    }

    /// <summary>Test seam: a millisecond tick source and the error log.</summary>
    internal SlotLoop(SlotClock clock, Transmitter tx, Func<long> ticks, Action<Exception> logError)
    {
        _clock = clock;
        _tx = tx;
        _ticks = ticks;
        _logError = logError;
    }

    /// <summary>Starts the thread.</summary>
    public void Start()
    {
        _run = true;
        _thread = new Thread(() =>
        {
            while (_run)
            {
                Guarded(_clock.Poll);
                Guarded(_tx.Poll);
                Thread.Sleep(1);
            }
        })
        { IsBackground = true, Priority = ThreadPriority.Highest, Name = "SlotLoop" };
        _thread.Start();
    }

    // An exception on this thread would end the process, possibly with PTT keyed over CAT. Stop transmitting instead,
    // say why in the log (at most every ErrorLogInterval), and keep the slots running. A repeat inside the interval is
    // not logged, but still halts if something went on the air since the last Halt.
    internal void Guarded(Action poll)
    {
        try
        {
            poll();
        }
        catch (Exception ex)
        {
            var now = _ticks();
            if (_lastErrorTicks is { } last && now - last < (long)ErrorLogInterval.TotalMilliseconds)
            {
                if (_tx.Transmitting) _ = _tx.HaltAsync();
                return;
            }
            _lastErrorTicks = now;
            _logError(ex);
            _ = _tx.HaltAsync();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _run = false;
        _thread?.Join(500);
    }
}
