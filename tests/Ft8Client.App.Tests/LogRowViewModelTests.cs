// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.ViewModels;
using Ft8Client.Data.Log;

namespace Ft8Client.App.Tests;

public class LogRowViewModelTests
{
    [Theory]
    [InlineData(QsoSource.Wavelog, UploadState.Uploaded, null, "From Wavelog", false)]
    [InlineData(QsoSource.Local, UploadState.Uploaded, null, "In Wavelog", false)]
    [InlineData(QsoSource.Local, UploadState.Queued, null, "Waiting to upload", true)]
    [InlineData(QsoSource.Local, UploadState.Queued, "The logbook site is unreachable", "Retrying: The logbook site is unreachable", true)]
    [InlineData(QsoSource.Local, UploadState.None, null, "", false)]
    [InlineData(QsoSource.Qrz, UploadState.None, null, "", false)]
    public void Wavelog_DeliveryState_Text(string source, string state, string? error, string text, bool flag)
    {
        var row = new LogRowViewModel(new QsoRecord
        {
            Call = "K1ABC", Band = "20m", Mode = "FT8", Source = source, WavelogUploadState = state, WavelogUploadError = error,
            QsoDateOn = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc),
        });

        (row.Wavelog, row.WavelogFlag).Should().Be((text, flag));
    }

    [Fact]
    public void Qrz_DownloadedFromWavelog_ShowsItsOwnQrzState()
    {
        var row = new LogRowViewModel(new QsoRecord { Call = "K1ABC", Band = "20m", Mode = "FT8", Source = QsoSource.Wavelog, QsoDateOn = DateTime.UtcNow });

        row.Qrz.Should().Be("Not uploaded", "where a contact came from is the Wavelog column's business");
    }
}
