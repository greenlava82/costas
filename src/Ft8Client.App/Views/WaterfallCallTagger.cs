// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.Core;
using Ft8Client.Core.Contacts;
using Ft8Client.Core.Logbook;
using Ft8Client.Core.Messages;
using Ft8Client.Core.Ranking;

namespace Ft8Client.App.Views;

/// <summary>
/// Picks out the FT8 and FT4 decodes that arrived since the last call, as callsign tags for the waterfall (owner request
/// 2026-11-25, after WSJT-X's Wide Graph). Each sender is tagged once a slot: the early decode pass and the full decode
/// can both report it. Decodes older than <see cref="MaxAge"/> are never tagged, so starting up, switching mode or
/// switching the tags on does not put old decodes on new audio.
/// </summary>
public sealed class WaterfallCallTagger
{
    /// <summary>Oldest slot start, before now, whose decodes are still tagged: two FT8 slots.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    private readonly HashSet<RawDecodeLine> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(DateTime Slot, string Call)> _tagged = [];
    private RawDecodeLine? _last;
    private int _lastCount;

    /// <summary>Returns tags for the lines in <paramref name="s"/> not seen before, lowest frequency first.</summary>
    public IReadOnlyList<WaterfallTag> Take(SessionSnapshot s, DateTime utcNow)
    {
        var lines = s.RawDecodes;
        if (lines.Count == _lastCount && (lines.Count == 0 || ReferenceEquals(lines[^1], _last))) return [];
        _lastCount = lines.Count;
        _last = lines.Count > 0 ? lines[^1] : null;
        var oldest = utcNow - MaxAge;
        // New lines are appended, so they are the unseen run at the end.
        var fresh = new List<RawDecodeLine>();
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var l = lines[i];
            if (l.Decode.SlotStartUtc < oldest || !_seen.Add(l)) break;
            fresh.Add(l);
        }
        Prune(oldest);
        if (fresh.Count == 0 || !ModeInfo.IsSlotted(s.Mode)) return [];

        var partner = s.Contact is { Outcome: ContactOutcome.InProgress } c ? c.DxCall : null;
        Dictionary<string, RankedStation>? stations = null;
        var tags = new List<WaterfallTag>();
        for (var i = fresh.Count - 1; i >= 0; i--)
        {
            var l = fresh[i];
            if (l.Transmitted || !ModeInfo.IsSlotted(l.Mode) || Label(l.Message.SenderCall) is not { } call) continue;
            if (!_tagged.Add((l.Decode.SlotStartUtc, call))) continue;
            stations ??= ByCall(s.Rank);
            var kind = l.ToMe || (partner is not null && Callsign.EqualsCall(call, partner))
                ? WaterfallTagKind.You
                : stations.TryGetValue(call, out var r) ? KindOf(r.Need.Tag) : WaterfallTagKind.Normal;
            tags.Add(new WaterfallTag(l.Decode.OffsetHz + ModeInfo.OccupiedBandwidthHz(l.Mode) / 2, call, kind));
        }
        tags.Sort((a, b) => a.Hz.CompareTo(b.Hz));
        return tags;
    }

    /// <summary>The colour for what working a station would add, as the station list colours its Why column.</summary>
    public static WaterfallTagKind KindOf(NeedTag tag) => tag switch
    {
        NeedTag.NewCountry => WaterfallTagKind.NewCountry,
        NeedTag.NewBand or NeedTag.NewGrid => WaterfallTagKind.NewBand,
        NeedTag.Worked => WaterfallTagKind.Worked,
        _ => WaterfallTagKind.Normal,
    };

    /// <summary>The text for a sender: the call without the angle brackets of a hashed call; null for none or one not resolved.</summary>
    public static string? Label(string? sender)
    {
        if (string.IsNullOrWhiteSpace(sender)) return null;
        var call = sender.Trim().TrimStart('<').TrimEnd('>');
        return call.Length == 0 || call.Contains("...", StringComparison.Ordinal) ? null : call;
    }

    private void Prune(DateTime oldest)
    {
        if (_seen.Count > 2000) _seen.RemoveWhere(l => l.Decode.SlotStartUtc < oldest);
        if (_tagged.Count > 2000) _tagged.RemoveWhere(t => t.Slot < oldest);
    }

    private static Dictionary<string, RankedStation> ByCall(RankResult rank)
    {
        var map = new Dictionary<string, RankedStation>(StringComparer.Ordinal);
        foreach (var r in rank.CallingMe.Concat(rank.Line).Concat(rank.Watching).Concat(rank.Worked)) map.TryAdd(r.Station.Call, r);
        return map;
    }
}
