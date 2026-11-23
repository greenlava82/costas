// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Data.Journal;

namespace Ft8Client.Data.Tests;

public class AllTxtWriterTests
{
    [Fact]
    public void PathFor_Time_MonthFolderWithAllTxt()
    {
        var w = new AllTxtWriter(Path.Combine("x", "costas"));

        w.PathFor(new DateTime(2026, 11, 22, 19, 0, 0, DateTimeKind.Utc)).Should().Be(Path.Combine("x", "costas", "2026-11", "ALL.TXT"));
    }

    [Fact]
    public void Append_TwoMonths_EachInItsOwnFolderInWsjtxLayout()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ft8all-" + Guid.NewGuid().ToString("N"));
        try
        {
            var w = new AllTxtWriter(dir);
            var oct = new DateTime(2026, 10, 31, 23, 59, 45, DateTimeKind.Utc);
            var nov = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            var rx = DecodeJournal.Format(oct, 14_074_000, false, "FT8", -11, 0.2, 1290, "CQ DF2RG JO31");
            var tx = DecodeJournal.Format(nov, 14_074_000, true, "FT8", 0, 0, 1650, "DF2RG W7LIT DN40");

            w.Append(oct, [rx]);
            w.Append(nov, [tx]);
            w.Append(nov, []);

            File.ReadAllText(Path.Combine(dir, "2026-10", "ALL.TXT")).Should().Be("261031_235945    14.074 Rx FT8    -11  0.2 1290 CQ DF2RG JO31" + Environment.NewLine);
            File.ReadAllLines(Path.Combine(dir, "2026-11", "ALL.TXT")).Should().Equal("261101_000000    14.074 Tx FT8      0  0.0 1650 DF2RG W7LIT DN40");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
