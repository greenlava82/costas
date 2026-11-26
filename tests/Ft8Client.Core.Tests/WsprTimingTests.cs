// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Wspr;

namespace Ft8Client.Core.Tests;

public class WsprTimingTests
{
    [Fact]
    public void TxLength_Default_Is110Point592Seconds() => WsprTiming.TxLength.Should().Be(TimeSpan.FromMilliseconds(110_592));

    [Fact]
    public void TxStart_Default_IsOneSecond() => WsprTiming.TxStart.Should().Be(TimeSpan.FromSeconds(1));

    [Fact]
    public void CaptureCutoff_Default_IsHalfSymbol396() => WsprTiming.CaptureCutoff.Should().Be(TimeSpan.FromMilliseconds(114_048));

    [Fact]
    public void CaptureCutoff_Default_FallsAfterTransmissionEnds() =>
        WsprTiming.CaptureCutoff.Should().BeGreaterThan(WsprTiming.TxStart + WsprTiming.TxLength);

    [Fact]
    public void RecordSamples_Default_IsWholeSlotAt12kHz() => WsprTiming.RecordSamples.Should().Be(1_440_000);

    [Fact]
    public void ToneSpacingHz_Default_Is12000Over8192() => WsprTiming.ToneSpacingHz.Should().BeApproximately(1.4648, 0.0001);

    [Fact]
    public void Window_Default_Is1400To1600Hz()
    {
        WsprTiming.WindowLowHz.Should().Be(1400);
        WsprTiming.WindowHighHz.Should().Be(1600);
    }
}
