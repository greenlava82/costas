// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Logbook;
using Ft8Client.Core.Neighbours;
using Ft8Client.Core.Ranking;

namespace Ft8Client.Core.Tests;

public class BandAdvisorTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 2, 0, 0, DateTimeKind.Utc);

    private static readonly LogIndex Log = LogIndex.Build([new LogContact("DL1XX", "20m", "FT8", "JO62", "DL", null, false, Now.AddDays(-3))]);

    // Receivers: "DL" gives Europe, "JA" Asia, "PY" South America, "W" North America (my own continent).
    private static IEnumerable<NeighbourReport> Heard(string band, string rxPrefix, int receivers, int senders = 2, int minutesAgo = 5, string mode = "FT8") =>
        Enumerable.Range(0, receivers).Select(i =>
            new NeighbourReport($"K7A{(char)('A' + i % senders)}", "DN41", $"{rxPrefix}1A{(char)('A' + i)}", band, mode, -10, Now.AddMinutes(-minutesAgo)));

    private static IEnumerable<ReceptionReport> HeardMe(string band, string rxPrefix, int receivers, int minutesAgo = 5, string mode = "FT8") =>
        Enumerable.Range(0, receivers).Select(i =>
            new ReceptionReport($"{rxPrefix}1B{(char)('A' + i)}", null, null, null, band, mode, -12, Now.AddMinutes(-minutesAgo)));

    private static IReadOnlyList<BandAdvice> Advise(IEnumerable<NeighbourReport> near, IEnumerable<ReceptionReport>? me = null, string band = "20m",
                                                    DateTime? followedSince = null, string? grid = "DN40")
    {
        var reports = near.ToList();
        var openings = BandOpenings.Build(reports, "W7LIT", "DN40", "FT8", TestData.Countries, Log, Now);
        return BandAdvisor.Advise(new BandAdviceInput(openings, reports, [.. me ?? []], band, "FT8", "W7LIT", grid, "NA", followedSince, Now),
            TestData.Countries);
    }

    [Fact]
    public void Advise_FiveEuropeanReceiversOfTwoNeighboursOnOtherBand_SaysOpenFromArea()
    {
        var a = Advise(Heard("15m", "DL", 5)).Should().ContainSingle().Subject;

        a.Kind.Should().Be(BandAdviceKind.OpenFromArea);
        a.Band.Should().Be("15m");
        a.Continents.Should().ContainSingle().Which.Should().BeEquivalentTo(new BandAdviceContinent("EU", 5, 2, Now.AddMinutes(-5)));
        a.NeededEntities.Should().Equal("Fed. Rep. of Germany");
    }

    [Fact]
    public void Advise_FourReceivers_SaysNothing()
    {
        Advise(Heard("15m", "DL", 4)).Should().BeEmpty();
    }

    [Fact]
    public void Advise_OneNeighbourHeard_SaysNothing()
    {
        Advise(Heard("15m", "DL", 9, senders: 1)).Should().BeEmpty();
    }

    [Fact]
    public void Advise_ReceiversOnMyOwnContinent_SaysNothing()
    {
        Advise(Heard("15m", "W", 20)).Should().BeEmpty();
    }

    [Fact]
    public void Advise_NoGrid_SaysNothing()
    {
        Advise(Heard("15m", "DL", 9), grid: null).Should().BeEmpty();
    }

    [Fact]
    public void Advise_NoReports_SaysNothing()
    {
        Advise([]).Should().BeEmpty();
    }

    [Fact]
    public void Advise_FiveFarStationsHeardMeOnMyBand_SaysOpenForYou()
    {
        var a = Advise([], HeardMe("20m", "JA", 5)).Should().ContainSingle().Subject;

        a.Kind.Should().Be(BandAdviceKind.OpenForYou);
        a.Band.Should().Be("20m");
        a.Continents.Should().ContainSingle().Which.Should().BeEquivalentTo(new BandAdviceContinent("AS", 5, 1, Now.AddMinutes(-5)));
    }

    [Fact]
    public void Advise_MyReportsOldOtherBandOrOtherMode_SaysNothing()
    {
        Advise([], [.. HeardMe("20m", "JA", 5, minutesAgo: 16), .. HeardMe("15m", "JA", 5), .. HeardMe("20m", "JA", 5, mode: "FT4")])
            .Should().BeEmpty();
    }

    [Fact]
    public void Advise_OtherBandOpenOnlyWhereMyBandAlreadyIs_NamesMyBand()
    {
        var a = Advise(Heard("15m", "DL", 12), HeardMe("20m", "DL", 6)).Should().ContainSingle().Subject;

        a.Kind.Should().Be(BandAdviceKind.OpenForYou);
        a.Band.Should().Be("20m");
    }

    [Fact]
    public void Advise_OtherBandOpenWhereMyBandIsNot_NamesOtherBand()
    {
        var a = Advise(Heard("15m", "JA", 6), HeardMe("20m", "DL", 9)).Should().ContainSingle().Subject;

        a.Band.Should().Be("15m");
        a.Continents.Select(c => c.Continent).Should().Equal("AS");
    }

    [Fact]
    public void Advise_TwoOtherBandsOpen_NamesTheOneMoreStationsHeard()
    {
        Advise([.. Heard("15m", "DL", 6), .. Heard("17m", "DL", 9)]).Single().Band.Should().Be("17m");
    }

    [Fact]
    public void Advise_ThreeContinentsOpen_NamesTheTwoWithMostStations()
    {
        var a = Advise([.. Heard("15m", "DL", 6), .. Heard("15m", "JA", 9), .. Heard("15m", "PY", 5)]).Single();

        a.Continents.Select(c => c.Continent).Should().Equal("AS", "EU");
    }

    [Fact]
    public void Advise_MyBandHeardWellEarlierAndHardlyNow_SaysClosing()
    {
        var a = Advise([.. Heard("20m", "DL", 9, minutesAgo: 20), .. Heard("20m", "DL", 3, minutesAgo: 5), .. Heard("20m", "W", 10, minutesAgo: 5)],
            followedSince: Now.AddMinutes(-40)).Should().ContainSingle().Subject;

        a.Kind.Should().Be(BandAdviceKind.Closing);
        a.Band.Should().Be("20m");
        a.Continents.Should().ContainSingle().Which.Should().BeEquivalentTo(new BandAdviceContinent("EU", 3, 2, Now.AddMinutes(-5), 9));
    }

    [Fact]
    public void Advise_ClosingButFollowedUnder30Minutes_SaysNothing()
    {
        Advise([.. Heard("20m", "DL", 9, minutesAgo: 20), .. Heard("20m", "W", 10, minutesAgo: 5)], followedSince: Now.AddMinutes(-25))
            .Should().BeEmpty();
    }

    [Fact]
    public void Advise_ClosingButMyBandHasFewReports_SaysNothing()
    {
        // The feed may have gone quiet: too few recent reports on the band to tell a closing band from no data.
        Advise([.. Heard("20m", "DL", 9, minutesAgo: 20), .. Heard("20m", "W", 9, minutesAgo: 5)], followedSince: Now.AddMinutes(-40))
            .Should().BeEmpty();
    }

    [Fact]
    public void Advise_RecentReceiversMoreThanAThird_SaysNothing()
    {
        Advise([.. Heard("20m", "DL", 9, minutesAgo: 20), .. Heard("20m", "DL", 4, minutesAgo: 5), .. Heard("20m", "W", 10, minutesAgo: 5)],
            followedSince: Now.AddMinutes(-40)).Should().BeEmpty();
    }

    [Fact]
    public void Advise_MyBandClosingAndOtherBandOpen_SaysBoth()
    {
        var a = Advise([.. Heard("20m", "DL", 9, minutesAgo: 20), .. Heard("20m", "W", 10, minutesAgo: 5), .. Heard("15m", "DL", 7)],
            followedSince: Now.AddMinutes(-40));

        a.Select(x => (x.Kind, x.Band)).Should().Equal((BandAdviceKind.OpenFromArea, "15m"), (BandAdviceKind.Closing, "20m"));
    }

    [Fact]
    public void Advise_SampledBand_KeepsHowLongItWasListenedTo()
    {
        var sample = new BandSample("15m", "FT8", Now.AddMinutes(-3), Now.AddMinutes(-2.5), [.. Heard("15m", "DL", 6, minutesAgo: 3)]);
        var openings = BandOpenings.BuildSampled([sample], "W7LIT", "DN40", "FT8", TestData.Countries, Log, Now);

        var a = BandAdvisor.Advise(new BandAdviceInput(openings, [], [], "20m", "FT8", "W7LIT", "DN40", "NA", null, Now), TestData.Countries).Single();

        a.SampledFor.Should().Be(TimeSpan.FromSeconds(30));
        a.Continents.Single().Receivers.Should().Be(6, "receivers are real counts, never scaled up from the sample");
    }
}
