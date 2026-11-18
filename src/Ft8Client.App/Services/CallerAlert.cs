// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Contacts;
using Ft8Client.Core.Messages;

namespace Ft8Client.App.Services;

/// <summary>
/// Decides when Waiting for you has someone new: a station not in the previous list, or one that has just been seen
/// replying after I stopped its contact.
/// </summary>
public sealed class CallerAlert
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenAfterStop = new(StringComparer.Ordinal);
    private readonly List<WaitingCaller> _fresh = [];

    /// <summary>The callers that were new in the last <see cref="Update"/>, in list order.</summary>
    public IReadOnlyList<WaitingCaller> Fresh => _fresh;

    /// <summary>Takes the current Waiting for you list; true when it has someone new since the last call.</summary>
    public bool Update(IReadOnlyList<WaitingCaller> waiting)
    {
        _fresh.Clear();
        var now = new HashSet<string>(StringComparer.Ordinal);
        var nowAfterStop = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in waiting)
        {
            var call = Callsign.Normalize(w.Call);
            now.Add(call);
            if (w.AfterStop) nowAfterStop.Add(call);
            if (!_seen.Contains(call) || (w.AfterStop && !_seenAfterStop.Contains(call))) _fresh.Add(w);
        }
        _seen.Clear();
        _seen.UnionWith(now);
        _seenAfterStop.Clear();
        _seenAfterStop.UnionWith(nowAfterStop);
        return _fresh.Count > 0;
    }
}
