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
using Ft8Client.Decoding;

namespace Ft8Client.App.Tests;

internal sealed class SilentSource : ISlotSource
{
    public void BeginSlot(DateTime slotStartUtc, Mode mode)
    {
    }

    public void CancelSlot()
    {
    }

    public SlotAudio? Cutoff(DateTime slotStartUtc, Mode mode) => new(slotStartUtc, mode, new short[16]);
}

/// <summary>
/// Returns scripted decodes for a slot a set time after its capture cut-off (13.6 s into the slot): the full decode
/// after one delay, the session's quick decode (narrow band or shallow) after another. Unscripted runs return
/// nothing at the cut-off. Results are released from the test loop, which waits until the session has applied them.
/// </summary>
internal sealed class ScriptedDecoder : IDecoder
{
    private static readonly TimeSpan Cutoff = TimeSpan.FromSeconds(13.6);
    private readonly object _gate = new();
    private readonly Dictionary<(DateTime Slot, bool Quick), (TimeSpan Delay, string[] Lines)> _script = [];
    private readonly List<(DateTime Due, DateTime Slot, string[] Lines, TaskCompletionSource<DecodeResult> Tcs)> _parked = [];
    private int _calls;
    private int _released;

    public int QuickCalls { get; private set; }

    public int Released
    {
        get { lock (_gate) return _released; }
    }

    public int Calls
    {
        get { lock (_gate) return _calls; }
    }

    /// <summary>Scripts a slot. The quick decode finishes after <paramref name="quick"/>, or with the full one.</summary>
    public void Script(DateTime slot, TimeSpan full, TimeSpan? quick, params string[] lines)
    {
        lock (_gate)
        {
            _script[(slot, false)] = (full, lines);
            _script[(slot, true)] = (quick ?? full, lines);
        }
    }

    public void Script(DateTime slot, TimeSpan delay, params string[] lines) => Script(slot, delay, null, lines);

    public Task<DecodeResult> DecodeAsync(SlotAudio slot, DecodeContext ctx, CancellationToken ct)
    {
        var quick = ctx.Depth < 3 || ctx.HighHz - ctx.LowHz < 2800;
        lock (_gate)
        {
            _calls++;
            if (quick) QuickCalls++;
            var (delay, lines) = _script.Remove((slot.SlotStartUtc, quick), out var s) ? s : (TimeSpan.Zero, []);
            var tcs = new TaskCompletionSource<DecodeResult>();
            _parked.Add((slot.SlotStartUtc + Cutoff + delay, slot.SlotStartUtc, lines, tcs));
            return tcs.Task;
        }
    }

    /// <summary>Completes the decodes that are due. The session applies them on the thread pool.</summary>
    public void ReleaseDue(DateTime now)
    {
        List<(DateTime Due, DateTime Slot, string[] Lines, TaskCompletionSource<DecodeResult> Tcs)> due;
        lock (_gate)
        {
            due = _parked.Where(p => p.Due <= now).ToList();
            _parked.RemoveAll(p => p.Due <= now);
            _released += due.Count;
        }
        foreach (var p in due)
        {
            var decodes = p.Lines.Select(t => new Decode(p.Slot, -9, 0.1, 1264, t, false)).ToList();
            p.Tcs.SetResult(new DecodeResult(decodes, p.Due - p.Slot - Cutoff, DecodeStatus.Ok));
        }
    }
}
