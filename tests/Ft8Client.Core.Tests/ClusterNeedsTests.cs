// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using FluentAssertions;
using Ft8Client.Core.Bands;
using Ft8Client.Core.Cluster;
using Ft8Client.Core.Logbook;

namespace Ft8Client.Core.Tests;

public class ClusterNeedsTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);

    private static readonly FrequencyTable Table = new(
    [
        new FrequencyEntry("20m", Mode.Ft8, 14_074_000, 14_000_000, 14_350_000),
        new FrequencyEntry("15m", Mode.Ft8, 21_074_000, 21_000_000, 21_450_000),
    ]);

    private static ClusterSpot Spot(string call, long hz, int minutes = 0, string comment = "FT8") => new(call, hz, "DL1ABC", comment, T0.AddMinutes(minutes));

    private static LogIndex LogWith(params (string Call, string Band)[] qsos)
    {
        var c = TestData.Countries;
        return LogIndex.Build(qsos.Select(q => new LogContact(q.Call, q.Band, "FT8", null, c.Lookup(q.Call)!.Entity.Key, null, false, T0.AddDays(-30))));
    }

    [Fact]
    public void Find_CountryNotInLog_NewCountry()
    {
        var spots = ClusterSpots.Empty.With([Spot("JA1XYZ", 21_074_000)], T0);

        var n = ClusterNeeds.Find(spots, LogWith(("DL2ZZZ", "20m")), TestData.Countries, Mode.Ft8, Table, T0);

        n.Should().ContainSingle().Which.Need.Should().Be(NeedTag.NewCountry);
        n[0].Band.Should().Be("15m");
    }

    [Fact]
    public void Find_CountryWorkedOnOtherBand_NewBand()
    {
        var spots = ClusterSpots.Empty.With([Spot("JA1XYZ", 21_074_000)], T0);

        var n = ClusterNeeds.Find(spots, LogWith(("JA2AAA", "20m")), TestData.Countries, Mode.Ft8, Table, T0);

        n.Should().ContainSingle().Which.Need.Should().Be(NeedTag.NewBand);
    }

    [Fact]
    public void Find_CountryWorkedOnThisBand_LeftOut()
    {
        var spots = ClusterSpots.Empty.With([Spot("JA1XYZ", 21_074_000)], T0);

        ClusterNeeds.Find(spots, LogWith(("JA2AAA", "15m")), TestData.Countries, Mode.Ft8, Table, T0).Should().BeEmpty();
    }

    [Fact]
    public void Find_OtherMode_LeftOut()
    {
        var spots = ClusterSpots.Empty.With([Spot("JA1XYZ", 21_025_000, comment: "CW 20 WPM")], T0);

        ClusterNeeds.Find(spots, LogWith(("DL2ZZZ", "20m")), TestData.Countries, Mode.Ft8, Table, T0).Should().BeEmpty();
    }

    [Fact]
    public void Find_Several_NewCountriesFirstThenNewest()
    {
        var spots = ClusterSpots.Empty.With([Spot("JA1XYZ", 21_074_000, -1), Spot("VK2ABC", 14_074_000, -8), Spot("ZL1ABC", 21_074_000, -3)], T0);

        var n = ClusterNeeds.Find(spots, LogWith(("JA2AAA", "20m")), TestData.Countries, Mode.Ft8, Table, T0);

        n.Select(x => x.Spot.DxCall).Should().Equal("ZL1ABC", "VK2ABC", "JA1XYZ");
    }
}
