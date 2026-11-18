// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Bands;
using Ft8Client.Core.Geo;
using Ft8Client.Core.Messages;

namespace Ft8Client.Core.Neighbours;

/// <summary>
/// Answers "which band should I be on?" from evidence Costas already holds about this station: who hears stations near me
/// on every band, and who hears me on the band I am on (docs/04-domain-logic.md, section 14a). It says a band is open only
/// when the counts below are met, and nothing when unsure. Display only: it never changes band, ranking or who is called.
/// Pure.
/// </summary>
public static class BandAdvisor
{
    /// <summary>A continent is open on a band from my area when at least this many distinct stations there heard stations near me.</summary>
    public const int MinReceivers = 5;

    /// <summary>... and they heard at least this many distinct stations near me, so one loud neighbour is not a band opening.</summary>
    public const int MinSenders = 2;

    /// <summary>The band I am on is open for me when at least this many distinct stations on a continent heard my own signal.</summary>
    public const int MinReceiversOfMe = 5;

    /// <summary>A closing continent had at least this many distinct receivers 15 to 30 minutes ago.</summary>
    public const int ClosingMinEarlierReceivers = 8;

    /// <summary>A closing continent has at most a third of its earlier receivers in the last 15 minutes.</summary>
    public const int ClosingDropFactor = 3;

    /// <summary>
    /// The band I am on needs at least this many reports of stations near me in the last 15 minutes before it is called
    /// closing, so a feed that went quiet is not read as a closing band.
    /// </summary>
    public const int ClosingMinRecentSpots = 10;

    /// <summary>At most this many continents are named on one line.</summary>
    public const int MaxContinents = 2;

    /// <summary>How long the band I am on must have been followed for the closing line.</summary>
    public static readonly TimeSpan ClosingSpan = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The advice to show: at most one "open" line, then at most one "closing" line for the band I am on. Empty when the
    /// evidence does not support any.
    /// </summary>
    /// <param name="input">The evidence.</param>
    /// <param name="countries">For receivers' continents.</param>
    public static IReadOnlyList<BandAdvice> Advise(BandAdviceInput input, CountryFile countries)
    {
        if (Grid.ToLatLon(input.MyGrid) is null) return [];
        var current = input.Band.ToLowerInvariant();
        var closing = Closing(input, countries);
        var closingTo = closing?.Continents.Select(c => c.Continent).ToHashSet(StringComparer.Ordinal) ?? [];

        // The band I am on: my own signal first, then stations near me. A continent it is closing to is not called open.
        var forYou = OpenForYou(input, countries, current, closingTo);
        var here = input.Openings.GetValueOrDefault(current);
        List<BandAdviceContinent> hereFromArea = here is null ? [] : OpenContinents(here, input.MyContinent).Where(c => !closingTo.Contains(c.Continent)).ToList();
        var openHere = forYou.Select(c => c.Continent).Concat(hereFromArea.Select(c => c.Continent)).ToHashSet(StringComparer.Ordinal);

        // Another band is worth naming only for continents the band I am on is not open to.
        var other = input.Openings.Values
            .Where(o => !string.Equals(o.Band, current, StringComparison.OrdinalIgnoreCase))
            .Select(o => (Opening: o, Continents: OpenContinents(o, input.MyContinent).Where(c => !openHere.Contains(c.Continent)).ToList()))
            .Where(x => x.Continents.Count > 0)
            .OrderByDescending(x => x.Continents[0].Receivers)
            .ThenByDescending(x => x.Opening.NeededEntities.Count)
            .ThenBy(x => BandIndex(x.Opening.Band))
            .FirstOrDefault();

        var result = new List<BandAdvice>(2);
        if (other.Opening is { } o)
            result.Add(new BandAdvice(BandAdviceKind.OpenFromArea, o.Band.ToLowerInvariant(), [.. other.Continents.Take(MaxContinents)], o.NeededEntities,
                o.SampledFor));
        else if (forYou.Count > 0)
            result.Add(new BandAdvice(BandAdviceKind.OpenForYou, current, [.. forYou.Take(MaxContinents)], here?.NeededEntities ?? []));
        else if (hereFromArea.Count > 0)
            result.Add(new BandAdvice(BandAdviceKind.OpenFromArea, current, [.. hereFromArea.Take(MaxContinents)], here!.NeededEntities, here.SampledFor));
        if (closing is not null) result.Add(closing);
        return result;
    }

    // Far continents where stations heard enough stations near me on this band, most receivers first.
    private static List<BandAdviceContinent> OpenContinents(BandOpening o, string? myContinent) =>
        (o.ReachByContinent ?? new Dictionary<string, ContinentReach>())
        .Where(kv => kv.Key != myContinent && kv.Value.Receivers >= MinReceivers && kv.Value.Senders >= MinSenders)
        .OrderByDescending(kv => kv.Value.Receivers).ThenBy(kv => ContinentIndex(kv.Key))
        .Select(kv => new BandAdviceContinent(kv.Key, kv.Value.Receivers, kv.Value.Senders, kv.Value.LatestUtc))
        .ToList();

    // Far continents where enough stations heard my own signal on the band I am on in the last 15 minutes.
    private static List<BandAdviceContinent> OpenForYou(BandAdviceInput input, CountryFile countries, string current, HashSet<string> closingTo)
    {
        var since = input.NowUtc - BandOpenings.Window;
        return input.MyReports
            .Where(r => r.TimeUtc >= since && r.TimeUtc <= input.NowUtc && string.Equals(r.Band, current, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.Mode, input.Mode, StringComparison.OrdinalIgnoreCase) && !Callsign.EqualsCall(r.ReceiverCall, input.MyCall))
            .Select(r => (Report: r, Continent: countries.Lookup(r.ReceiverCall)?.Continent))
            .Where(x => x.Continent is { } c && c != input.MyContinent && !closingTo.Contains(c))
            .GroupBy(x => x.Continent!, StringComparer.Ordinal)
            .Select(g => new BandAdviceContinent(g.Key, g.Select(x => Callsign.Normalize(x.Report.ReceiverCall)).Distinct(StringComparer.Ordinal).Count(), 1,
                g.Max(x => x.Report.TimeUtc)))
            .Where(c => c.Receivers >= MinReceiversOfMe)
            .OrderByDescending(c => c.Receivers).ThenBy(c => ContinentIndex(c.Continent))
            .ToList();
    }

    // The band I am on is closing to a far continent when it was heard well from my area 15 to 30 minutes ago and hardly since,
    // while the band itself still has reports (so the feed is working). Needs the band followed for the whole 30 minutes.
    private static BandAdvice? Closing(BandAdviceInput input, CountryFile countries)
    {
        if (input.CurrentBandFollowedSinceUtc is not { } since || input.NowUtc - since < ClosingSpan) return null;
        if (Grid.ToLatLon(input.MyGrid) is not { } me) return null;
        var current = input.Band.ToLowerInvariant();
        var recentFrom = input.NowUtc - BandOpenings.Window;
        var earlierFrom = input.NowUtc - ClosingSpan;
        var near = input.NeighbourReports
            .Where(r => r.TimeUtc >= earlierFrom && r.TimeUtc <= input.NowUtc && string.Equals(r.Band, current, StringComparison.OrdinalIgnoreCase)
                        && r.Mode == input.Mode && !Callsign.EqualsCall(r.SenderCall, input.MyCall) && NeighbourComparison.IsNeighbour(me, r.SenderGrid))
            .ToList();
        if (near.Count(r => r.TimeUtc >= recentFrom) < ClosingMinRecentSpots) return null;
        var continents = near
            .Select(r => (Report: r, Continent: countries.Lookup(r.ReceiverCall)?.Continent))
            .Where(x => x.Continent is { } c && c != input.MyContinent)
            .GroupBy(x => x.Continent!, StringComparer.Ordinal)
            .Select(g =>
            {
                var recent = g.Where(x => x.Report.TimeUtc >= recentFrom).Select(x => x.Report).ToList();
                var earlier = g.Where(x => x.Report.TimeUtc < recentFrom).Select(x => x.Report).ToList();
                return (Continent: new BandAdviceContinent(g.Key, Distinct(recent, r => r.ReceiverCall), Distinct(recent, r => r.SenderCall),
                        recent.Count == 0 ? null : recent.Max(r => r.TimeUtc), Distinct(earlier, r => r.ReceiverCall)),
                    EarlierSenders: Distinct(earlier, r => r.SenderCall));
            })
            .Where(x => x.Continent.EarlierReceivers >= ClosingMinEarlierReceivers && x.EarlierSenders >= MinSenders
                        && x.Continent.Receivers * ClosingDropFactor <= x.Continent.EarlierReceivers)
            .Select(x => x.Continent)
            .OrderByDescending(c => c.EarlierReceivers).ThenBy(c => ContinentIndex(c.Continent))
            .Take(MaxContinents)
            .ToList();
        return continents.Count == 0 ? null : new BandAdvice(BandAdviceKind.Closing, current, continents, []);
    }

    private static int Distinct(IEnumerable<NeighbourReport> reports, Func<NeighbourReport, string> call) =>
        reports.Select(r => Callsign.Normalize(call(r))).Distinct(StringComparer.Ordinal).Count();

    private static int ContinentIndex(string continent)
    {
        for (var i = 0; i < BandOpenings.Continents.Count; i++)
            if (BandOpenings.Continents[i] == continent) return i;
        return int.MaxValue;
    }

    private static int BandIndex(string band)
    {
        for (var i = 0; i < BandPlan.Bands.Count; i++)
            if (string.Equals(BandPlan.Bands[i].Band, band, StringComparison.OrdinalIgnoreCase)) return i;
        return int.MaxValue;
    }
}
