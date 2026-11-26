// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core;
using Ft8Client.Core.Encoding;

namespace Ft8Client.Core.Tests;

public class ModeInfoWsprTests
{
    [Theory]
    [InlineData(Mode.Ft8, true, false, true, false)]
    [InlineData(Mode.Ft4, true, false, true, false)]
    [InlineData(Mode.Psk31, false, true, false, false)]
    [InlineData(Mode.Psk63, false, true, false, false)]
    [InlineData(Mode.Rtty, false, true, false, false)]
    [InlineData(Mode.Wspr, false, false, true, true)]
    public void Predicates_EachMode_MatchFamilies(Mode mode, bool slotted, bool keyboard, bool hasSlots, bool wspr)
    {
        ModeInfo.IsSlotted(mode).Should().Be(slotted);
        ModeInfo.IsKeyboard(mode).Should().Be(keyboard);
        ModeInfo.HasSlots(mode).Should().Be(hasSlots);
        ModeInfo.IsWspr(mode).Should().Be(wspr);
    }

    [Fact]
    public void SlotMilliseconds_Wspr_IsTwoMinutes() => ModeInfo.SlotMilliseconds(Mode.Wspr).Should().Be(120_000);

    [Fact]
    public void SlotLength_Wspr_IsTwoMinutes() => ModeInfo.SlotLength(Mode.Wspr).Should().Be(TimeSpan.FromMinutes(2));

    [Fact]
    public void TxStartMilliseconds_Wspr_IsOneSecond() => ModeInfo.TxStartMilliseconds(Mode.Wspr).Should().Be(1_000);

    [Fact]
    public void TxMilliseconds_Wspr_Is162SymbolsOf8192Samples() => ModeInfo.TxMilliseconds(Mode.Wspr).Should().Be(110_592);

    [Fact]
    public void TxEnd_Wspr_IsOneSecondPlusTransmission() => ModeInfo.TxEnd(Mode.Wspr).Should().Be(TimeSpan.FromMilliseconds(111_592));

    [Fact]
    public void OccupiedBandwidthHz_Wspr_IsWholeWindow() => ModeInfo.OccupiedBandwidthHz(Mode.Wspr).Should().Be(200);

    [Fact]
    public void Adif_Wspr_IsWsprWithNoSubmode() => ModeInfo.Adif(Mode.Wspr).Should().Be(("WSPR", (string?)null));

    [Fact]
    public void Name_Wspr_IsWspr() => ModeInfo.Name(Mode.Wspr).Should().Be("WSPR");

    [Fact]
    public void TryParse_Wspr_ReturnsFalse() => ModeInfo.TryParse("WSPR", out _).Should().BeFalse();

    [Fact]
    public void Operable_NotYetBuilt_LeavesOutWspr() => ModeInfo.Operable.Should().NotContain(Mode.Wspr);

    [Fact]
    public void JtNineHelpers_Wspr_Throw()
    {
        FluentActions.Invoking(() => ModeInfo.DecoderSamples(Mode.Wspr)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ModeInfo.Separator(Mode.Wspr)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ModeInfo.Slotted(Mode.Wspr)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(Mode.Psk31)]
    [InlineData(Mode.Psk63)]
    [InlineData(Mode.Rtty)]
    public void TimingHelpers_KeyboardMode_StillThrow(Mode mode)
    {
        FluentActions.Invoking(() => ModeInfo.SlotMilliseconds(mode)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ModeInfo.TxStartMilliseconds(mode)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ModeInfo.TxMilliseconds(mode)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FtxEncoderEncode_Wspr_Throws() =>
        FluentActions.Invoking(() => FtxEncoder.Encode("CQ K1ABC FN42", Mode.Wspr)).Should().Throw<ArgumentOutOfRangeException>();
}
