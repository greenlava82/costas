// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Globalization;
using Ft8Client.Core.Bands;

namespace Ft8Client.Core.Neighbours;

/// <summary>Sentences for the band advice line and its tooltip (docs/04-domain-logic.md, section 14a).</summary>
public static class BandAdviceText
{
    /// <summary>
    /// The line: "15 m is open to Europe from your area: 23 stations there heard 6 stations near you in the last 15 min.",
    /// "20 m is open to Europe for you: 12 stations there heard you in the last 15 min.", "20 m is closing to Asia: 1 station
    /// there heard stations near you in the last 15 min, 9 in the 15 min before."
    /// </summary>
    /// <param name="a">The advice.</param>
    public static string Line(BandAdvice a)
    {
        var band = BandPlan.Display(a.Band);
        var where = Join(a.Continents.Select(c => NeighbourText.Continent(c.Continent)).ToList());
        var rx = Join(a.Continents.Select(c => c.Receivers.ToString("N0", CultureInfo.InvariantCulture)).ToList());
        var stations = a.Continents.Count == 1 ? Stations(a.Continents[0].Receivers) : $"{rx} stations";
        var when = a.SampledFor is { } t ? $" in {Listened(t)} of listening" : " in the last 15 min";
        return a.Kind switch
        {
            BandAdviceKind.OpenForYou => $"{band} is open to {where} for you: {stations} there heard you{when}.{Need(a)}",
            BandAdviceKind.OpenFromArea => $"{band} is open to {where} from your area: {stations} there heard "
                                           + (a.Continents.Count == 1 ? Stations(a.Continents[0].Senders) : "stations") + $" near you{when}.{Need(a)}",
            _ => $"{band} is closing to {where}: {stations} there heard stations near you in the last 15 min, "
                 + $"{Join(a.Continents.Select(c => c.EarlierReceivers.ToString("N0", CultureInfo.InvariantCulture)).ToList())} in the 15 min before.",
        };
    }

    /// <summary>The tooltip: the evidence per continent, how recent it is, the needed countries, and the rules.</summary>
    /// <param name="a">The advice.</param>
    /// <param name="units">For the radius of "near you".</param>
    /// <param name="nowUtc">Now, for how long ago the newest report was.</param>
    public static string Tip(BandAdvice a, DistanceUnit units, DateTime nowUtc)
    {
        var band = BandPlan.Display(a.Band);
        var radius = units == DistanceUnit.Miles ? "300 miles" : "480 km";
        var lines = new List<string>();
        foreach (var c in a.Continents)
        {
            var name = NeighbourText.Continent(c.Continent);
            var latest = c.LatestUtc is { } t ? $", the latest {Ago(nowUtc - t)}" : string.Empty;
            lines.Add(a.Kind switch
            {
                BandAdviceKind.OpenForYou => $"{name}: {Stations(c.Receivers)} heard you on {band} in the last 15 minutes{latest}.",
                BandAdviceKind.OpenFromArea => $"{name}: {Stations(c.Receivers)} heard {Stations(c.Senders)} within {radius} of you on {band}"
                                               + (a.SampledFor is { } s ? $" in {Listened(s)} of listening" : " in the last 15 minutes") + $"{latest}.",
                _ => $"{name}: {Stations(c.Receivers)} heard stations within {radius} of you on {band} in the last 15 minutes, "
                     + string.Create(CultureInfo.InvariantCulture, $"{c.EarlierReceivers:N0} in the 15 minutes before{latest}."),
            });
        }
        if (a.NeededEntities.Count > 0)
            lines.Add($"Countries you need on {band} that hear stations near you: {string.Join(", ", a.NeededEntities)}.");
        lines.Add(a.Kind switch
        {
            BandAdviceKind.OpenForYou =>
                string.Create(CultureInfo.InvariantCulture, $"From PSK Reporter and Reverse Beacon Network reports of your signal. Open means at least {BandAdvisor.MinReceiversOfMe} stations on a continent heard you; your own continent is left out."),
            BandAdviceKind.OpenFromArea =>
                string.Create(CultureInfo.InvariantCulture, $"From PSK Reporter reports of stations within {radius} of you. Open means at least {BandAdvisor.MinReceivers} stations on a continent heard at least {BandAdvisor.MinSenders} of them; your own continent is left out.")
                + (a.SampledFor is null ? string.Empty : " PSK Reporter sends too many reports to follow every band at once, so this band was listened to in turns."),
            _ => string.Create(CultureInfo.InvariantCulture, $"From PSK Reporter reports of stations within {radius} of you. Closing means at least {BandAdvisor.ClosingMinEarlierReceivers} stations on a continent heard them 15 to 30 minutes ago and a third as many or fewer since."),
        });
        lines.Add("Advice only: Costas does not change band for you.");
        return string.Join(Environment.NewLine, lines);
    }

    // " 3 countries you need on 15 m hear your area.", " Japan, which you need on 15 m, hears your area.", or empty.
    private static string Need(BandAdvice a)
    {
        var band = BandPlan.Display(a.Band);
        return a.NeededEntities.Count switch
        {
            0 => string.Empty,
            1 => $" {a.NeededEntities[0]}, which you need on {band}, hears your area.",
            var n => string.Create(CultureInfo.InvariantCulture, $" {n} countries you need on {band} hear your area."),
        };
    }

    private static string Stations(int n) =>
        string.Create(CultureInfo.InvariantCulture, $"{n:N0} {(n == 1 ? "station" : "stations")}");

    // "Europe", "Europe and Asia".
    private static string Join(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Join(string.Empty, items) : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    // "just now", "1 minute ago", "12 minutes ago".
    private static string Ago(TimeSpan t)
    {
        var m = (int)Math.Floor(t.TotalMinutes);
        return m <= 0 ? "just now" : string.Create(CultureInfo.InvariantCulture, $"{m} {(m == 1 ? "minute" : "minutes")} ago");
    }

    // "45 seconds", "1 minute", "1 minute 30 seconds".
    private static string Listened(TimeSpan t)
    {
        var seconds = (int)Math.Round(t.TotalSeconds);
        var (m, sec) = (seconds / 60, seconds % 60);
        var minutes = m == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{m} {(m == 1 ? "minute" : "minutes")}");
        var secs = sec == 0 && m > 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{sec} {(sec == 1 ? "second" : "seconds")}");
        return string.Join(' ', new[] { minutes, secs }.Where(x => x.Length > 0));
    }
}
