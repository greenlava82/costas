// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Wspr;

namespace Ft8Client.Core.Tests;

public class WsprSpotParserTests
{
    private static readonly DateTime Slot = new(2015, 4, 26, 9, 18, 0, DateTimeKind.Utc);
    private const long Dial = 14_095_600;

    [Fact]
    public void TryParse_Type1Line_ReadsEveryField()
    {
        WsprSpotParser.TryParse("0918  -9  1.1  14.097046  0  ND6P DM04 30 ", Slot, Dial, out var s).Should().BeTrue();
        s.SlotStartUtc.Should().Be(Slot);
        s.Snr.Should().Be(-9);
        s.DtSeconds.Should().Be(1.1);
        s.FrequencyHz.Should().Be(14_097_046);
        s.AudioHz.Should().Be(1446);
        s.DriftHz.Should().Be(0);
        s.Callsign.Should().Be("ND6P");
        s.Grid.Should().Be("DM04");
        s.PowerDbm.Should().Be(30);
        s.IsHashed.Should().BeFalse();
        s.DialHz.Should().Be(Dial);
    }

    [Fact]
    public void TryParse_Type2Line_HasNoGrid()
    {
        WsprSpotParser.TryParse("0918 -15  0.2  14.097100  1  PJ4/K1ABC 37", Slot, Dial, out var s).Should().BeTrue();
        s.Callsign.Should().Be("PJ4/K1ABC");
        s.Grid.Should().BeNull();
        s.PowerDbm.Should().Be(37);
        s.DriftHz.Should().Be(1);
        s.IsHashed.Should().BeFalse();
    }

    [Fact]
    public void TryParse_Type3Line_IsHashedWithSixCharacterGrid()
    {
        WsprSpotParser.TryParse("0918 -21  0.5  14.097117  0  <PJ4/K1ABC> FK52UD 37", Slot, Dial, out var s).Should().BeTrue();
        s.Callsign.Should().Be("PJ4/K1ABC");
        s.Grid.Should().Be("FK52UD");
        s.IsHashed.Should().BeTrue();
        s.HasCall.Should().BeTrue();
    }

    [Fact]
    public void TryParse_UnresolvedHash_KeepsPlaceholder()
    {
        WsprSpotParser.TryParse("0918 -21  0.5  14.097117  0  <...> FK52UD 37", Slot, Dial, out var s).Should().BeTrue();
        s.Callsign.Should().Be(WsprSpot.UnresolvedCall);
        s.HasCall.Should().BeFalse();
        s.IsHashed.Should().BeTrue();
    }

    [Fact]
    public void TryParse_NegativeDriftAndDt_Parse()
    {
        WsprSpotParser.TryParse("0918  -1 -0.8  14.097103 -2  NM7J DM26 30", Slot, Dial, out var s).Should().BeTrue();
        s.DtSeconds.Should().Be(-0.8);
        s.DriftHz.Should().Be(-2);
    }

    [Fact]
    public void TryParse_EdgeGrids_Accepted()
    {
        WsprSpotParser.TryParse("0918 -5 0.0 14.097000 0 K1ABC AA00 0", Slot, Dial, out var a).Should().BeTrue();
        a.Grid.Should().Be("AA00");
        a.PowerDbm.Should().Be(0);
        WsprSpotParser.TryParse("0918 -5 0.0 14.097000 0 K1ABC RR99 60", Slot, Dial, out var b).Should().BeTrue();
        b.Grid.Should().Be("RR99");
        WsprSpotParser.TryParse("0918 -5 0.0 14.097000 0 K1ABC RR73 30", Slot, Dial, out var c).Should().BeTrue("RR73 is a real square in WSPR");
        c.Grid.Should().Be("RR73");
    }

    [Theory]
    [InlineData("<DecodeFinished>")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("0918 -9 1.1 14.097046 0 ND6P")]
    [InlineData("0918 -9 1.1 14.097046 0 ND6P DM04")]
    [InlineData("0918 -9 1.1 14.097046 0 ND6P ZZ99 30")]
    [InlineData("0918 -9 1.1 14.097046 0 ND6P DM04 99")]
    [InlineData("0918 -9 1.1 14.097046 0 ND6P DM04 -3")]
    [InlineData("0918 -9 1.1 notafreq 0 ND6P DM04 30")]
    [InlineData("09 -9 1.1 14.097046 0 ND6P DM04 30")]
    [InlineData("0918 -9 1.1 14.097046 0 <ND6P> DM04 30")]
    [InlineData("0918 -9 1.1 14.097046 0 NODIGITS DM04 30")]
    [InlineData("0918 -9 1.1 14.097046 0 ND6P DM04 30 extra words here")]
    public void TryParse_NotADecode_ReturnsFalse(string line) => WsprSpotParser.TryParse(line, Slot, Dial, out _).Should().BeFalse();

    [Fact]
    public void TryParse_Null_ReturnsFalse() => WsprSpotParser.TryParse(null, Slot, Dial, out _).Should().BeFalse();

    [Fact]
    public void TryParse_GoldenLines_AllNineParse()
    {
        var golden = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "wspr", "150426_0918.golden.txt"));
        var lines = golden.SkipWhile(l => l != "# stdout").Skip(1).TakeWhile(l => !l.StartsWith('#')).ToList();
        var spots = lines.Select(l => WsprSpotParser.TryParse(l, Slot, Dial, out var s) ? s : null).Where(s => s is not null).ToList();
        spots.Should().HaveCount(9);
        spots.Select(s => s!.Callsign).Should().Equal("ND6P", "W5BIT", "G8VDQ", "WD4LHT", "NM7J", "KI7CI", "DJ6OL", "W3HH", "W3BI");
    }
}
