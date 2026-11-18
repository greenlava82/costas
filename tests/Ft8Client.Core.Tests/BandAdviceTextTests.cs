// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Neighbours;

namespace Ft8Client.Core.Tests;

public class BandAdviceTextTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 2, 0, 0, DateTimeKind.Utc);

    private static BandAdviceContinent C(string continent, int receivers, int senders = 6, int minutesAgo = 2, int earlier = 0) =>
        new(continent, receivers, senders, Now.AddMinutes(-minutesAgo), earlier);

    [Fact]
    public void Line_OpenFromArea_SaysHowManyHeardStationsNearYou()
    {
        BandAdviceText.Line(new BandAdvice(BandAdviceKind.OpenFromArea, "15m", [C("EU", 23)], []))
            .Should().Be("15 m is open to Europe from your area: 23 stations there heard 6 stations near you in the last 15 min.");
    }

    [Fact]
    public void Line_OpenForYou_SaysHowManyHeardYou()
    {
        BandAdviceText.Line(new BandAdvice(BandAdviceKind.OpenForYou, "20m", [C("EU", 12, senders: 1)], []))
            .Should().Be("20 m is open to Europe for you: 12 stations there heard you in the last 15 min.");
    }

    [Fact]
    public void Line_TwoContinents_NamesBoth()
    {
        BandAdviceText.Line(new BandAdvice(BandAdviceKind.OpenFromArea, "15m", [C("AS", 9), C("EU", 6)], []))
            .Should().Be("15 m is open to Asia and Europe from your area: 9 and 6 stations there heard stations near you in the last 15 min.");
    }

    [Fact]
    public void Line_SampledBand_SaysHowLongItWasListenedTo()
    {
        BandAdviceText.Line(new BandAdvice(BandAdviceKind.OpenFromArea, "10m", [C("SA", 7, senders: 3)], [], TimeSpan.FromSeconds(90)))
            .Should().Be("10 m is open to South America from your area: 7 stations there heard 3 stations near you in 1 minute 30 seconds of listening.");
    }

    [Fact]
    public void Line_OneNeededCountry_NamesIt()
    {
        BandAdviceText.Line(new BandAdvice(BandAdviceKind.OpenFromArea, "15m", [C("AS", 5, senders: 2)], ["Mongolia"]))
            .Should().EndWith("in the last 15 min. Mongolia, which you need on 15 m, hears your area.");
    }

    [Fact]
    public void Line_SeveralNeededCountries_CountsThem()
    {
        BandAdviceText.Line(new BandAdvice(BandAdviceKind.OpenFromArea, "15m", [C("EU", 23)], ["Italy", "Spain", "Austria"]))
            .Should().EndWith("in the last 15 min. 3 countries you need on 15 m hear your area.");
    }

    [Fact]
    public void Line_Closing_ComparesWithTheQuarterHourBefore()
    {
        BandAdviceText.Line(new BandAdvice(BandAdviceKind.Closing, "20m", [C("EU", 1, senders: 1, earlier: 14)], []))
            .Should().Be("20 m is closing to Europe: 1 station there heard stations near you in the last 15 min, 14 in the 15 min before.");
    }

    [Fact]
    public void Tip_Miles_GivesEvidenceRecencyAndRule()
    {
        var tip = BandAdviceText.Tip(new BandAdvice(BandAdviceKind.OpenFromArea, "15m", [C("EU", 23, minutesAgo: 1)], ["Italy", "Spain"]),
            DistanceUnit.Miles, Now);

        tip.Should().Contain("Europe: 23 stations heard 6 stations within 300 miles of you on 15 m in the last 15 minutes, the latest 1 minute ago.")
            .And.Contain("Countries you need on 15 m that hear stations near you: Italy, Spain.")
            .And.Contain("at least 5 stations on a continent heard at least 2 of them")
            .And.Contain("Costas does not change band for you");
    }

    [Fact]
    public void Tip_Kilometres_GivesRadiusInKilometres()
    {
        BandAdviceText.Tip(new BandAdvice(BandAdviceKind.OpenFromArea, "15m", [C("EU", 23, minutesAgo: 0)], []), DistanceUnit.Kilometres, Now)
            .Should().Contain("within 480 km of you").And.Contain("the latest just now");
    }
}
