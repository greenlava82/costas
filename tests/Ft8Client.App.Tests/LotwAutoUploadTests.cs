// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Services;

namespace Ft8Client.App.Tests;

public class LotwAutoUploadTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(5, 32)]
    [InlineData(6, 60)]
    [InlineData(20, 60)]
    public void RetryDelay_FailuresInARow_DoublesUpToAnHour(int failures, int minutes) =>
        LotwService.RetryDelay(failures).Should().Be(TimeSpan.FromMinutes(minutes));

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(5, 480)]
    [InlineData(6, 720)]
    [InlineData(50, 720)]
    public void DownloadRetryDelay_FailuresInARow_DoublesFromHalfAnHourUpToTheInterval(int failures, int minutes) =>
        LotwService.DownloadRetryDelay(failures, TimeSpan.FromHours(12)).Should().Be(TimeSpan.FromMinutes(minutes));

    [Fact]
    public void DownloadRetryDelay_ShortInterval_NeverWaitsLongerThanIt() =>
        LotwService.DownloadRetryDelay(4, TimeSpan.FromHours(1)).Should().Be(TimeSpan.FromHours(1));
}
