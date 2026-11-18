// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Bands;

namespace Ft8Client.Core.Tests;

public class UsLicenceClassTests
{
    // The expected segments, typed out here from 47 CFR 97.301, 97.303(h), 97.305(c) and 97.307(f)(9) (eCFR, up to date
    // as of 2026-11-16) rather than read from UsDataSegments, so a wrong edge in the code fails a test.
    private const string Sixty = "5330.6-5333.4 5346.6-5349.4 5351.5-5366.5 5371.6-5374.4 5403.6-5406.4";
    private const string Vhf = "50100-54000 144100-148000";

    private static readonly Dictionary<UsLicenceClass, string> Expected = new()
    {
        [UsLicenceClass.AmateurExtra] = $"1800-2000 3500-3600 {Sixty} 7000-7125 10100-10150 14000-14150 18068-18110 21000-21200 24890-24930 28000-28300 {Vhf}",
        [UsLicenceClass.Advanced] = $"1800-2000 3525-3600 {Sixty} 7025-7125 10100-10150 14025-14150 18068-18110 21025-21200 24890-24930 28000-28300 {Vhf}",
        [UsLicenceClass.General] = $"1800-2000 3525-3600 {Sixty} 7025-7125 10100-10150 14025-14150 18068-18110 21025-21200 24890-24930 28000-28300 {Vhf}",
        [UsLicenceClass.Technician] = $"28000-28300 {Vhf}",
    };

    private static IEnumerable<(long LowHz, long HighHz)> ExpectedFor(UsLicenceClass c) =>
        Expected[c].Split(' ').Select(r => r.Split('-')).Select(p => (Hz(p[0]), Hz(p[1])));

    private static long Hz(string khz) => (long)Math.Round(decimal.Parse(khz, System.Globalization.CultureInfo.InvariantCulture) * 1000);

    /// <summary>Every edge of every segment for every class: (class, low, high, inside).</summary>
    public static TheoryData<UsLicenceClass, long, long, bool> Edges()
    {
        var data = new TheoryData<UsLicenceClass, long, long, bool>();
        foreach (var c in Expected.Keys)
        {
            foreach (var (lo, hi) in ExpectedFor(c))
            {
                data.Add(c, lo, lo + 50, true);          // a 50 Hz signal starting on the lower edge
                data.Add(c, lo - 1, lo + 49, false);     // 1 Hz below it
                data.Add(c, hi - 50, hi, true);          // ending on the upper edge
                data.Add(c, hi - 49, hi + 1, false);     // 1 Hz above it
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Edges))]
    public void Contains_SegmentEdge_InsideOnlyWhenWholeSignalFits(UsLicenceClass licence, long low, long high, bool inside) =>
        UsDataSegments.Contains(licence, low, high).Should().Be(inside);

    [Theory]
    [InlineData(UsLicenceClass.AmateurExtra)]
    [InlineData(UsLicenceClass.Advanced)]
    [InlineData(UsLicenceClass.General)]
    [InlineData(UsLicenceClass.Technician)]
    public void For_EachClass_MatchesTheRegulations(UsLicenceClass licence) =>
        UsDataSegments.For(licence).Should().Equal(ExpectedFor(licence));

    [Theory]
    // The 25 kHz at the bottom of 80, 40, 20 and 15 m is Extra only.
    [InlineData(UsLicenceClass.AmateurExtra, 3_510_000, true)]
    [InlineData(UsLicenceClass.Advanced, 3_510_000, false)]
    [InlineData(UsLicenceClass.General, 3_510_000, false)]
    [InlineData(UsLicenceClass.AmateurExtra, 7_010_000, true)]
    [InlineData(UsLicenceClass.General, 7_010_000, false)]
    [InlineData(UsLicenceClass.AmateurExtra, 14_010_000, true)]
    [InlineData(UsLicenceClass.Advanced, 14_010_000, false)]
    [InlineData(UsLicenceClass.General, 14_010_000, false)]
    [InlineData(UsLicenceClass.AmateurExtra, 21_010_000, true)]
    [InlineData(UsLicenceClass.General, 21_010_000, false)]
    // The usual FT8 frequencies are inside General's segments.
    [InlineData(UsLicenceClass.General, 3_574_000, true)]
    [InlineData(UsLicenceClass.General, 7_075_000, true)]
    [InlineData(UsLicenceClass.General, 14_075_000, true)]
    [InlineData(UsLicenceClass.General, 21_075_000, true)]
    // 60 m is General and up.
    [InlineData(UsLicenceClass.General, 5_358_000, true)]
    [InlineData(UsLicenceClass.Advanced, 5_332_000, true)]
    [InlineData(UsLicenceClass.Technician, 5_358_000, false)]
    [InlineData(UsLicenceClass.Technician, 5_332_000, false)]
    // Technician: CW only in the 80, 40 and 15 m data segments, nothing else below 10 m.
    [InlineData(UsLicenceClass.Technician, 1_841_000, false)]
    [InlineData(UsLicenceClass.Technician, 3_574_000, false)]
    [InlineData(UsLicenceClass.Technician, 7_075_000, false)]
    [InlineData(UsLicenceClass.Technician, 10_137_000, false)]
    [InlineData(UsLicenceClass.Technician, 14_075_000, false)]
    [InlineData(UsLicenceClass.Technician, 18_101_000, false)]
    [InlineData(UsLicenceClass.Technician, 21_075_000, false)]
    [InlineData(UsLicenceClass.Technician, 24_916_000, false)]
    [InlineData(UsLicenceClass.Technician, 28_075_000, true)]
    [InlineData(UsLicenceClass.Technician, 28_350_000, false)]
    [InlineData(UsLicenceClass.Technician, 50_314_000, true)]
    [InlineData(UsLicenceClass.Technician, 144_175_000, true)]
    // Not US: never checked here.
    [InlineData(UsLicenceClass.NotUs, 14_250_000, true)]
    public void Contains_FrequencyByClass_FollowsPrivileges(UsLicenceClass licence, long hz, bool inside) =>
        UsDataSegments.Contains(licence, hz, hz + 50).Should().Be(inside);

    [Fact]
    public void Within_SixtyMetresForGeneral_AllFiveSegments() =>
        UsDataSegments.Within(UsLicenceClass.General, 5_330_500, 5_406_400).Should().HaveCount(5);

    [Fact]
    public void Describe_SeveralSegments_ListsThemInMhz() =>
        UsDataSegments.Describe([(5_330_600, 5_333_400), (5_351_500, 5_366_500), (5_371_600, 5_374_400)])
            .Should().Be("5.3306-5.3334, 5.3515-5.3665 and 5.3716-5.3744 MHz");

    [Theory]
    [InlineData(UsLicenceClass.AmateurExtra, "Amateur Extra")]
    [InlineData(UsLicenceClass.Advanced, "Advanced")]
    [InlineData(UsLicenceClass.General, "General")]
    [InlineData(UsLicenceClass.Technician, "Technician")]
    [InlineData(UsLicenceClass.NotUs, "Not US")]
    public void Name_EachClass_PlainName(UsLicenceClass licence, string name) => UsDataSegments.Name(licence).Should().Be(name);
}
