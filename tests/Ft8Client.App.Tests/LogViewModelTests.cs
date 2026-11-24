// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.App.ViewModels;
using Ft8Client.Core.Geo;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;

namespace Ft8Client.App.Tests;

public class LogViewModelTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 23, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void FromFor_Choices_StartAtUtcMidnight()
    {
        LogViewModel.FromFor(LogViewModel.Today, Now).Should().Be(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc));
        LogViewModel.FromFor(LogViewModel.Last7Days, Now).Should().Be(new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc));
        LogViewModel.FromFor(LogViewModel.Last30Days, Now).Should().Be(new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc));
        LogViewModel.FromFor(LogViewModel.ThisYear, Now).Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        LogViewModel.FromFor(LogViewModel.AllTime, Now).Should().BeNull();
    }

    [Fact]
    public void BuildFilter_AllModes_IsAll()
    {
        LogViewModel.BuildFilter("", LogViewModel.AllBands, LogViewModel.AllModes, LogViewModel.AllTime, LogViewModel.AnyConfirmation, Now)
            .Should().Be(LogFilter.All);
    }

    [Fact]
    public void BuildFilter_AllDigital_FiltersOnDigitalModes()
    {
        LogViewModel.BuildFilter("", LogViewModel.AllBands, LogViewModel.AllDigital, LogViewModel.AllTime, LogViewModel.AnyConfirmation, Now)
            .Should().Be(new LogFilter(ModeKey: LogViewModel.AllDigital));
    }

    [Fact]
    public void BuildFilter_Choices_MapToFilter()
    {
        LogViewModel.BuildFilter("ja", "20m", "FT4", LogViewModel.AllTime, LogViewModel.NotConfirmed, Now)
            .Should().Be(new LogFilter("ja", "20m", "FT4", null, false));
    }

    [Fact]
    public void StatusText_Unfiltered_CountsEverything()
    {
        LogViewModel.StatusText(new LogTotals(3400, 212, 1920), 3400, 3400)
            .Should().Be("3,400 contacts · 212 countries · 1,920 confirmed");
    }

    [Fact]
    public void StatusText_Filtered_SaysShowingOfTotal()
    {
        LogViewModel.StatusText(new LogTotals(120, 1, 60), 3400, 120)
            .Should().Be("Showing 120 of 3,400 contacts · 1 country · 60 confirmed");
    }

    [Fact]
    public void StatusText_MoreThanListed_SaysNewestListed()
    {
        LogViewModel.StatusText(new LogTotals(6000, 200, 10), 6000, 5000)
            .Should().Be("6,000 contacts · 200 countries · 10 confirmed · newest 5,000 listed");
    }

    private static LogRowViewModel Row(DateTime on, string call, string band, string country, bool confirmed = false) =>
        new(new QsoRecord { Call = call, QsoDateOn = on, Band = band, Mode = "FT8", Country = country, Confirmed = confirmed });

    [Fact]
    public void LogDay_Today_SaysTodayAndSummarisesTheDay()
    {
        var day = new LogDayViewModel(Now.Date, [Row(Now, "K1ABC", "20m", "United States", true), Row(Now, "JA1XYZ", "20m", "Japan"), Row(Now, "W9XYZ", "40m", "United States")], Now.Date);
        day.Heading.Should().Be("Today · Mon 12 Oct");
        day.Summary.Should().Be("3 contacts  ·  2 countries  ·  1 confirmed  ·  20m 40m");
    }

    [Fact]
    public void LogDay_EarlierYear_ShowsTheYear()
    {
        var day = new LogDayViewModel(new DateTime(2025, 12, 2, 0, 0, 0, DateTimeKind.Utc), [Row(Now, "K1ABC", "20m", "")], Now.Date);
        day.Heading.Should().Be("Tue 2 Dec 2025");
        day.Summary.Should().Be("1 contact  ·  20m");
    }

    [Fact]
    public void Refresh_TwoDays_PutsADayHeadingAboveEachDay()
    {
        var db = SqliteDatabase.InMemory(out var keep);
        using var _ = keep;
        var repo = new QsoRepository(db);
        var today = DateTime.UtcNow.Date;
        foreach (var (call, on) in new[] { ("K1ABC", today.AddHours(1)), ("W9XYZ", today.AddDays(-3).AddHours(5)), ("JA1XYZ", today.AddDays(-3).AddHours(4)) })
            repo.Insert(new QsoRecord { ProfileId = "p", Call = call, QsoDateOn = on, Band = "20m", Mode = "FT8", StationCallsign = "W7LIT", CreatedUtc = on, ModifiedUtc = on });
        var vm = new LogViewModel(repo, CountryFile.Parse("United States:  05:  08:  NA:   37.53:    91.67:     5.0:  K:\n    K,W;\n"), () => ("p", "W7LIT"),
            _ => Task.FromResult(""), () => null, () => { });
        vm.Refresh();
        vm.Items.Select(i => i is LogDayViewModel d ? "day" : ((LogRowViewModel)i).Call).Should().Equal("day", "K1ABC", "day", "W9XYZ", "JA1XYZ");
        vm.ContactsTotal.Should().Be("3");
        vm.SyncText.Should().Be("Not synced with QRZ");
    }

    private static LogViewModel NewVm(QsoRepository repo, Action? changed = null) =>
        new(repo, CountryFile.Parse("United States:  05:  08:  NA:   37.53:    91.67:     5.0:  K:\n    K,W;\n"), () => ("p", "W7LIT"),
            _ => Task.FromResult(""), () => null, changed ?? (() => { }));

    [Fact]
    public async Task ImportAsync_MissingFile_ReportsTheErrorWithoutThrowing()
    {
        var db = SqliteDatabase.InMemory(out var keep);
        using var _ = keep;
        var changed = 0;
        var vm = NewVm(new QsoRepository(db), () => changed++);
        var path = Path.Combine(Path.GetTempPath(), $"costas-missing-{Guid.NewGuid():N}.adi");

        await vm.Invoking(v => v.ImportAsync(path)).Should().NotThrowAsync();

        vm.Message.Should().StartWith("Could not import");
        vm.Syncing.Should().BeFalse();
        changed.Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_ValidFile_ImportsAndRefreshes()
    {
        var db = SqliteDatabase.InMemory(out var keep);
        using var _ = keep;
        var vm = NewVm(new QsoRepository(db));
        var path = Path.Combine(Path.GetTempPath(), $"costas-import-{Guid.NewGuid():N}.adi");
        File.WriteAllText(path, "<EOH>\n<CALL:5>K1ABC <QSO_DATE:8>20261012 <TIME_ON:4>1200 <BAND:3>20m <MODE:3>FT8 <EOR>\n");
        try
        {
            await vm.ImportAsync(path);
        }
        finally
        {
            File.Delete(path);
        }

        vm.Message.Should().Be("Imported 1, merged 0 duplicates, skipped 0.");
        vm.ContactsTotal.Should().Be("1");
        vm.Syncing.Should().BeFalse();
    }

    [Fact]
    public async Task ExportAsync_UnwritablePath_ReportsTheErrorWithoutThrowing()
    {
        var db = SqliteDatabase.InMemory(out var keep);
        using var _ = keep;
        var vm = NewVm(new QsoRepository(db));
        var path = Path.Combine(Path.GetTempPath(), $"costas-no-such-dir-{Guid.NewGuid():N}", "log.adi");

        await vm.Invoking(v => v.ExportAsync(path)).Should().NotThrowAsync();

        vm.Message.Should().StartWith("Could not export");
        vm.Syncing.Should().BeFalse();
    }

    [Fact]
    public void IsFileError_ProgrammingError_IsFalse()
    {
        LogViewModel.IsFileError(new InvalidOperationException()).Should().BeFalse();
        LogViewModel.IsFileError(new NullReferenceException()).Should().BeFalse();
        LogViewModel.IsFileError(new FileNotFoundException()).Should().BeTrue();
    }

    [Fact]
    public void SelectedItem_DayHeading_KeepsTheSelectedRow()
    {
        var db = SqliteDatabase.InMemory(out var keep);
        using var _ = keep;
        var repo = new QsoRepository(db);
        var on = DateTime.UtcNow;
        repo.Insert(new QsoRecord { ProfileId = "p", Call = "K1ABC", QsoDateOn = on, Band = "20m", Mode = "FT8", StationCallsign = "W7LIT", CreatedUtc = on, ModifiedUtc = on });
        var vm = new LogViewModel(repo, CountryFile.Parse("United States:  05:  08:  NA:   37.53:    91.67:     5.0:  K:\n    K,W;\n"), () => ("p", "W7LIT"),
            _ => Task.FromResult(""), () => null, () => { });
        vm.Refresh();
        vm.SelectedItem = vm.Items[1];
        vm.SelectedItem = vm.Items[0];
        vm.Selected!.Call.Should().Be("K1ABC");
        vm.SelectedItem.Should().BeSameAs(vm.Selected);
    }

    [Fact]
    public void Refresh_TwoStations_ShowsOnlyTheProfilesCallsignUntilAllStations()
    {
        var db = SqliteDatabase.InMemory(out var keep);
        using var _ = keep;
        var repo = new QsoRepository(db);
        var on = DateTime.UtcNow.Date.AddHours(1);
        foreach (var (call, station) in new[] { ("K1ABC", "W7LIT"), ("JA1XYZ", "W7LIT"), ("VK3ABC", "K0GST") })
            repo.Insert(new QsoRecord { ProfileId = "p", Call = call, QsoDateOn = on, Band = "20m", Mode = "FT8", StationCallsign = station, CreatedUtc = on, ModifiedUtc = on });
        var profile = ("p", "W7LIT");
        var vm = new LogViewModel(repo, CountryFile.Parse("United States:  05:  08:  NA:   37.53:    91.67:     5.0:  K:\n    K,W;\n"), () => profile,
            _ => Task.FromResult(""), () => null, () => { });

        vm.Refresh();
        vm.StationChoices.Should().Equal("W7LIT", LogViewModel.AllStations);
        vm.StationFilter.Should().Be("W7LIT");
        vm.Rows.Select(r => r.Call).Should().BeEquivalentTo(["K1ABC", "JA1XYZ"]);
        vm.ContactsTotal.Should().Be("2");
        vm.ContactsOf.Should().BeEmpty("the other station's contacts are not part of this log's total");

        // Another operator's profile: their log, under their callsign.
        profile = ("p2", "k0gst");
        vm.Refresh();
        vm.StationChoices[0].Should().Be("K0GST");
        vm.StationFilter.Should().Be("K0GST");
        vm.Rows.Select(r => r.Call).Should().Equal("VK3ABC");

        vm.StationFilter = LogViewModel.AllStations;
        vm.Rows.Should().HaveCount(3);
        profile = ("p", "W7LIT");
        vm.Refresh();
        vm.StationFilter.Should().Be(LogViewModel.AllStations, "a choice of every station survives a profile switch");
        vm.Rows.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExportAsync_ThisStation_WritesOnlyItsContacts()
    {
        var db = SqliteDatabase.InMemory(out var keep);
        using var _ = keep;
        var repo = new QsoRepository(db);
        var on = DateTime.UtcNow.Date.AddHours(1);
        foreach (var (call, station) in new[] { ("K1ABC", "W7LIT"), ("VK3ABC", "K0GST") })
            repo.Insert(new QsoRecord { ProfileId = "p", Call = call, QsoDateOn = on, Band = "20m", Mode = "FT8", StationCallsign = station, CreatedUtc = on, ModifiedUtc = on });
        var vm = new LogViewModel(repo, CountryFile.Parse("United States:  05:  08:  NA:   37.53:    91.67:     5.0:  K:\n    K,W;\n"), () => ("p2", "K0GST"),
            _ => Task.FromResult(""), () => null, () => { });
        var path = Path.Combine(Directory.CreateTempSubdirectory("costas-export").FullName, "log.adi");

        await vm.ExportAsync(path);

        var text = File.ReadAllText(path);
        text.Should().Contain("VK3ABC").And.NotContain("K1ABC");
        vm.Message.Should().Be("Exported 1 contacts made as K0GST.");
        vm.StationFilter = LogViewModel.AllStations;
        await vm.ExportAsync(path);
        var folder = Path.GetDirectoryName(path)!;
        File.ReadAllText(Path.Combine(folder, "log-W7LIT.adi")).Should().Contain("K1ABC").And.NotContain("VK3ABC");
        File.ReadAllText(Path.Combine(folder, "log-K0GST.adi")).Should().Contain("VK3ABC");
    }

    [Fact]
    public void ParseRangeTime_DatesAndTimes_EndIsExclusive()
    {
        var day = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        LogViewModel.ParseRangeTime("2026-11-20", end: false).Should().Be(day);
        LogViewModel.ParseRangeTime("2026-11-20", end: true).Should().Be(day.AddDays(1), "a date alone includes the whole day");
        LogViewModel.ParseRangeTime("2026-11-20 14:30", end: false).Should().Be(day.AddHours(14).AddMinutes(30));
        LogViewModel.ParseRangeTime("20261120 1430", end: true).Should().Be(day.AddHours(14).AddMinutes(31), "a time includes that minute");
        LogViewModel.ParseRangeTime("20.11.2026 14:30:15", end: true).Should().Be(day.AddHours(14).AddMinutes(30).AddSeconds(16));
        LogViewModel.ParseRangeTime("2026-11-20T14:30Z", end: false).Should().Be(day.AddHours(14).AddMinutes(30));
        LogViewModel.ParseRangeTime("", end: false).Should().BeNull();
        LogViewModel.ParseRangeTime("next tuesday", end: false).Should().BeNull();
        LogViewModel.ParseRangeTime("2026-11-20 25:00", end: false).Should().BeNull();
    }

    [Fact]
    public void BuildFilter_CustomRange_UsesFromAndTo()
    {
        var from = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(2);

        var f = LogViewModel.BuildFilter("", LogViewModel.AllBands, LogViewModel.AllModes, LogViewModel.CustomRange, LogViewModel.AnyConfirmation, Now, null, from, to);

        f.FromUtc.Should().Be(from);
        f.ToUtc.Should().Be(to);
        LogViewModel.BuildFilter("", LogViewModel.AllBands, LogViewModel.AllModes, LogViewModel.Today, LogViewModel.AnyConfirmation, Now, null, from, to)
            .ToUtc.Should().BeNull("the typed range applies only to the custom range choice");
    }

    [Fact]
    public async Task ExportAsync_CustomRange_WritesOnlyContactsInTheRange()
    {
        var db = SqliteDatabase.InMemory(out var keep);
        using var _ = keep;
        var repo = new QsoRepository(db);
        var day = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        foreach (var (call, on) in new[] { ("DL1OLD", day.AddHours(-1)), ("DL2IN", day.AddHours(10)), ("DL3IN", day.AddDays(1).AddHours(23).AddMinutes(59)), ("DL4NEW", day.AddDays(2)) })
            repo.Insert(new QsoRecord { ProfileId = "p", Call = call, QsoDateOn = on, Band = "20m", Mode = "FT8", StationCallsign = "W7LIT", CreatedUtc = on, ModifiedUtc = on });
        var vm = NewVm(repo);
        vm.DateFilter = LogViewModel.CustomRange;
        vm.RangeFrom = "2026-11-20";
        vm.RangeTo = "2026-11-21";
        vm.Rows.Select(r => r.Call).Should().Equal("DL3IN", "DL2IN");
        var path = Path.Combine(Directory.CreateTempSubdirectory("costas-export").FullName, "log.adi");

        await vm.ExportAsync(path);

        var text = File.ReadAllText(path);
        text.Should().Contain("DL2IN").And.Contain("DL3IN").And.NotContain("DL1OLD").And.NotContain("DL4NEW");
        text.Should().Contain("Filtered: digital modes, from 2026-11-20 00:00 to before 2026-11-22 00:00 UTC.", "the header says which contacts the file holds");
        vm.Message.Should().Be("Exported 2 contacts made as W7LIT. The filters left out 2 contacts.");
    }

    [Fact]
    public async Task ExportAsync_BandFilter_WritesOnlyThatBand()
    {
        var db = SqliteDatabase.InMemory(out var keep);
        using var _ = keep;
        var repo = new QsoRepository(db);
        var on = DateTime.UtcNow.Date.AddHours(1);
        foreach (var (call, band) in new[] { ("K1TWENTY", "20m"), ("K1FORTY", "40m") })
            repo.Insert(new QsoRecord { ProfileId = "p", Call = call, QsoDateOn = on, Band = band, Mode = "FT8", StationCallsign = "W7LIT", CreatedUtc = on, ModifiedUtc = on });
        var vm = NewVm(repo);
        vm.Refresh();
        vm.BandFilter = "40m";
        var path = Path.Combine(Directory.CreateTempSubdirectory("costas-export").FullName, "log.adi");

        await vm.ExportAsync(path);

        File.ReadAllText(path).Should().Contain("K1FORTY").And.NotContain("K1TWENTY");
    }

    [Fact]
    public async Task ExportAsync_UnreadableRange_RefusesWithReason()
    {
        var db = SqliteDatabase.InMemory(out var keep);
        using var _ = keep;
        var vm = NewVm(new QsoRepository(db));
        vm.DateFilter = LogViewModel.CustomRange;
        vm.RangeFrom = "yesterday";
        var path = Path.Combine(Directory.CreateTempSubdirectory("costas-export").FullName, "log.adi");

        await vm.ExportAsync(path);

        File.Exists(path).Should().BeFalse();
        vm.Message.Should().StartWith("Not exported: Cannot read the From date \"yesterday\"");
    }

    [Fact]
    public async Task ExportAsync_DxKeeperChoices_ReportedAndKeptBySetting()
    {
        var db = SqliteDatabase.InMemory(out var keep);
        using var _ = keep;
        var repo = new QsoRepository(db);
        var on = DateTime.UtcNow.Date.AddHours(1);
        repo.Insert(new QsoRecord { ProfileId = "p", Call = "DL1ABC", QsoDateOn = on, Band = "20m", Mode = "FT8", StationCallsign = "W7LIT", Name = "Jürgen", Qth = "Köln", CreatedUtc = on, ModifiedUtc = on });
        (bool, bool)? saved = null;
        var vm = new LogViewModel(repo, CountryFile.Parse("United States:  05:  08:  NA:   37.53:    91.67:     5.0:  K:\n    K,W;\n"), () => ("p", "W7LIT"),
            _ => Task.FromResult(""), () => null, () => { }) { ExportOptionsChanged = (a, l) => saved = (a, l) };
        vm.ExportPlainAscii = true;
        saved.Should().Be((true, false));
        var path = Path.Combine(Directory.CreateTempSubdirectory("costas-export").FullName, "log.adi");

        await vm.ExportAsync(path);

        File.ReadAllText(path).Should().Contain("<name:7>Juergen").And.Contain("<qth:5>Koeln");
        vm.Message.Should().EndWith(" Plain ASCII text.");
        vm.ExportWithoutNameQthComment = true;
        await vm.ExportAsync(path);
        File.ReadAllText(path).Should().NotContain("<name:").And.NotContain("<qth:");
        vm.Message.Should().EndWith(" Plain ASCII text; name, QTH and comment left out.");
    }
}
