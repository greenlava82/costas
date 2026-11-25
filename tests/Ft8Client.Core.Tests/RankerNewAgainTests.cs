// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Decoding;
using Ft8Client.Core.Logbook;
using Ft8Client.Core.Ranking;
using Ft8Client.Core.Stations;

namespace Ft8Client.Core.Tests;

/// <summary>Settings › Who to call › Worked stations count as new again (issue #205).</summary>
public class RankerNewAgainTests
{
    private static readonly DateTime T0 = new(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);

    // K1ABC last worked on 20 m FT8 400 days ago, K3GHI 10 days ago.
    private static RankResult Rank(TimeSpan? newAgainAfter)
    {
        var t = new StationTracker("W7LIT", "DN40", TestData.Countries, Mode.Ft8);
        t.ApplySlot(T0, [HeardMessage.From(new Decode(T0, -10, 0.1, 900, "CQ K1ABC FN42", false)),
                         HeardMessage.From(new Decode(T0, -10, 0.1, 1500, "CQ K3GHI FN42", false))]);
        var us = TestData.Countries.Lookup("K1ABC")!.Entity.Key;
        var log = LogIndex.Build(
        [
            new LogContact("K1ABC", "20m", "FT8", "FN42", us, null, false, T0.AddDays(-400)),
            new LogContact("K3GHI", "20m", "FT8", "FN42", us, null, false, T0.AddDays(-10)),
        ]);
        return Ranker.Rank(new RankInput(t.Stations, log, null, new RankingSettings { NewAgainAfter = newAgainAfter, PreferNewParks = false },
            TestData.Countries.Lookup("W7LIT"), "20m", "FT8", T0 + TimeSpan.FromSeconds(16), TestData.Countries));
    }

    [Fact]
    public void Rank_NewAgainNever_EveryWorkedStationIsARepeat()
    {
        var r = Rank(null);

        r.Line.Should().BeEmpty();
        r.Worked.Select(x => x.Station.Call).Should().BeEquivalentTo("K1ABC", "K3GHI");
    }

    [Fact]
    public void Rank_NewAgainAfterAYear_StationWorkedLongerAgoIsInTheLineAsNewCall()
    {
        var r = Rank(TimeSpan.FromDays(365));

        r.Line.Should().ContainSingle().Which.Should().Match<RankedStation>(x => x.Station.Call == "K1ABC" && x.Need.Tag == NeedTag.NewCall);
        r.Worked.Should().ContainSingle().Which.Station.Call.Should().Be("K3GHI");
    }
}
