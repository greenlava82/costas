// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using FluentAssertions;
using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

public class SettingsMapperNewAgainTests
{
    [Fact]
    public void ToRanking_DefaultSettings_AnyContactMakesARepeat() =>
        SettingsMapper.ToRanking(new RankingOptions()).NewAgainAfter.Should().BeNull();

    [Theory]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(365, 365)]
    [InlineData(100_000, 36_500)]
    public void ToRanking_NewAgainAfterDays_MappedAndClamped(int stored, int? expectedDays) =>
        SettingsMapper.ToRanking(new RankingOptions { NewAgainAfterDays = stored }).NewAgainAfter
            .Should().Be(expectedDays is { } d ? TimeSpan.FromDays(d) : null);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    [InlineData(45, 90)]
    [InlineData(5000, 1826)]
    public void NewAgainChoiceFor_StoredDays_RoundTripsToTheNearestChoiceNotShorter(int stored, int expected) =>
        SettingsViewModel.NewAgainDaysFor(SettingsViewModel.NewAgainChoiceFor(stored)).Should().Be(expected);
}
