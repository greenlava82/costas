// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Bands;

namespace Ft8Client.Core.Tests;

public class LicenceGuardTests
{
    private static FrequencyTable Table(UsLicenceClass licence) => new(
    [
        new FrequencyEntry("80m", Mode.Ft8, 3_573_000, 3_500_000, 4_000_000),
        new FrequencyEntry("60m", Mode.Ft8, 5_357_000, 5_351_500, 5_366_500),
        new FrequencyEntry("40m", Mode.Ft8, 7_074_000, 7_000_000, 7_300_000),
        new FrequencyEntry("20m", Mode.Ft8, 14_074_000, 14_000_000, 14_350_000),
        new FrequencyEntry("20m", Mode.Rtty, 14_080_000, 14_000_000, 14_350_000),
        new FrequencyEntry("15m", Mode.Ft8, 21_074_000, 21_000_000, 21_450_000),
        new FrequencyEntry("10m", Mode.Ft8, 28_074_000, 28_000_000, 29_700_000),
        new FrequencyEntry("6m", Mode.Ft8, 50_313_000, 50_000_000, 54_000_000),
    ])
    { Licence = licence };

    [Theory]
    // FT8 tones run from dial + offset to 50 Hz above it.
    [InlineData(UsLicenceClass.General, "20m", 14_024_000, 1_000, true)]   // 14.025000-14.025050
    [InlineData(UsLicenceClass.General, "20m", 14_024_000, 999, false)]    // starts 1 Hz low
    [InlineData(UsLicenceClass.Advanced, "20m", 14_024_000, 999, false)]
    [InlineData(UsLicenceClass.AmateurExtra, "20m", 14_000_000, 0, true)]
    [InlineData(UsLicenceClass.General, "20m", 14_148_000, 1_950, true)]   // ends on 14.150000
    [InlineData(UsLicenceClass.General, "20m", 14_148_000, 1_951, false)]
    [InlineData(UsLicenceClass.General, "40m", 7_024_000, 1_000, true)]
    [InlineData(UsLicenceClass.General, "40m", 7_024_000, 999, false)]
    [InlineData(UsLicenceClass.General, "80m", 3_524_000, 999, false)]
    [InlineData(UsLicenceClass.General, "15m", 21_024_000, 999, false)]
    [InlineData(UsLicenceClass.General, "60m", 5_357_000, 1_500, true)]
    [InlineData(UsLicenceClass.Technician, "60m", 5_357_000, 1_500, false)]
    [InlineData(UsLicenceClass.Technician, "20m", 14_074_000, 1_500, false)]
    [InlineData(UsLicenceClass.Technician, "10m", 28_074_000, 1_500, true)]
    [InlineData(UsLicenceClass.Technician, "10m", 28_298_000, 1_950, true)]  // ends on 28.300000
    [InlineData(UsLicenceClass.Technician, "10m", 28_298_000, 1_951, false)]
    [InlineData(UsLicenceClass.Technician, "6m", 50_313_000, 1_500, true)]
    [InlineData(UsLicenceClass.NotUs, "20m", 14_250_000, 1_500, true)]
    public void TransmitAllowed_Ft8ByClass_WholeSignalMustFit(UsLicenceClass licence, string band, long dial, int offset, bool allowed) =>
        Table(licence).TransmitAllowed(band, Mode.Ft8, dial, offset, out _).Should().Be(allowed);

    [Fact]
    public void TransmitAllowed_RttyCentredNearGeneralLowerEdge_UsesSignalWidth()
    {
        // An RTTY signal is centred on the offset: 250 Hz wide, it needs its centre 125 Hz above 14.025 MHz.
        var t = Table(UsLicenceClass.General);
        t.TransmitAllowed("20m", Mode.Rtty, 14_024_000, 1_125, out _).Should().BeTrue();
        t.TransmitAllowed("20m", Mode.Rtty, 14_024_000, 1_124, out _).Should().BeFalse();
    }

    [Fact]
    public void TransmitAllowed_GeneralBelowSegment_ReasonGivesTheLimits()
    {
        Table(UsLicenceClass.General).TransmitAllowed("20m", Mode.Ft8, 14_020_000, 1_500, out var why).Should().BeFalse();
        why.Should().Be("Not sent: your General licence allows data on 20 m only on 14.025-14.150 MHz, and this signal would take " +
            "14.0215-14.02155 MHz. Wrong licence class, or licensed outside the US? Change it in Settings › Station.");
    }

    [Fact]
    public void TransmitAllowed_TechnicianOnTwentyMetres_ReasonSaysReceiveOnly()
    {
        Table(UsLicenceClass.Technician).TransmitAllowed("20m", Mode.Ft8, 14_074_000, 1_500, out var why).Should().BeFalse();
        why.Should().Contain("Technician licence doesn't allow data on 20 m").And.Contain("receive only")
            .And.Contain("28.000-28.300 MHz").And.Contain("Settings › Station");
    }

    [Fact]
    public void TransmitAllowed_TechnicianOnTenMetresAboveData_ReasonGivesTheSegment()
    {
        Table(UsLicenceClass.Technician).TransmitAllowed("10m", Mode.Ft8, 28_400_000, 1_500, out var why).Should().BeFalse();
        why.Should().Contain("only on 28.000-28.300 MHz");
    }

    [Fact]
    public void Licence_NewTable_NotUs() => new FrequencyTable([]).Licence.Should().Be(UsLicenceClass.NotUs);

    [Fact]
    public void TransmitAllowed_NotUsOutsideBand_StillRefused() =>
        Table(UsLicenceClass.NotUs).TransmitAllowed("20m", Mode.Ft8, 14_349_000, 1_500, out _).Should().BeFalse();

    [Theory]
    [InlineData(UsLicenceClass.Technician, "20m", true)]
    [InlineData(UsLicenceClass.Technician, "80m", true)]
    [InlineData(UsLicenceClass.Technician, "60m", true)]
    [InlineData(UsLicenceClass.Technician, "10m", false)]
    [InlineData(UsLicenceClass.Technician, "6m", false)]
    [InlineData(UsLicenceClass.General, "20m", false)]
    [InlineData(UsLicenceClass.General, "60m", false)]
    [InlineData(UsLicenceClass.AmateurExtra, "80m", false)]
    [InlineData(UsLicenceClass.NotUs, "20m", false)]
    [InlineData(UsLicenceClass.Technician, "2m", false)]  // no row: nothing to say
    public void ReceiveOnly_ByClass_TrueWhenNoDataSegmentOnTheBand(UsLicenceClass licence, string band, bool receiveOnly) =>
        Table(licence).ReceiveOnly(band, Mode.Ft8).Should().Be(receiveOnly);
}
