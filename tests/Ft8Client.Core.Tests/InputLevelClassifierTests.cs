// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Decoding;

namespace Ft8Client.Core.Tests;

public class InputLevelClassifierTests
{
    [Theory]
    [InlineData(-25, -22, InputLevelState.Good)]
    [InlineData(-40, -37, InputLevelState.Good)]
    [InlineData(-10, -7, InputLevelState.Good)]
    [InlineData(-41, -38, InputLevelState.TooQuiet)]
    [InlineData(-9, -6, InputLevelState.TooLoud)]
    [InlineData(-25, -1, InputLevelState.Clipping)]
    [InlineData(-120, -120, InputLevelState.Silence)]
    public void Classify_Levels_MatchTheSetupBands(double rms, double peak, InputLevelState expected) =>
        InputLevelClassifier.Classify(rms, peak).Should().Be(expected);

    [Fact]
    public void Classify_LoudPeakOnQuietAudio_ClippingWins() =>
        InputLevelClassifier.Classify(-60, 0).Should().Be(InputLevelState.Clipping);
}
