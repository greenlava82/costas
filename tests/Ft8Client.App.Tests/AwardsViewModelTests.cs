// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.Core;
using Ft8Client.Core.Logbook;
using Ft8Client.Core.Stations;
using Ft8Client.Core.Time;
using Ft8Client.Data.Journal;
using Ft8Client.Data.Reference;

namespace Ft8Client.App.Tests;

public sealed class AwardsViewModelTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ft8aw-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private HeardHistoryService History(params (DateTime Utc, long Dial, string Message)[] lines)
    {
        var j = new DecodeJournal(_dir);
        foreach (var (utc, dial, msg) in lines) j.Append(utc, [DecodeJournal.Format(utc, dial, false, "FT8", -10, 0.1, 1200, msg)]);
        return new HeardHistoryService(_dir, ReferenceData.LoadCountries(), new ManualClock(Now));
    }

    private static DateTime At(int day, int hour) => new(2026, 10, day, hour, 15, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Refresh_WithHistory_ListsHeardCountriesFirstWithSentence()
    {
        var history = History((At(1, 13), 14_074_000, "CQ JT1AA ON47"), (At(2, 14), 14_074_000, "CQ JT1AA ON47"));
        await history.Refresh();
        var log = LogIndex.Build([new LogContact("JA1AAA", "20m", "FT8", "PM95", "JA", null, true, Now)]);
        var vm = new AwardsViewModel(() => log, ReferenceData.LoadCountries(), history, () => Now, a => a());

        vm.Refresh();

        vm.Cards.Select(c => c.Title).Should().Equal("DXCC", "WAS", "WAZ", "VUCC");
        vm.Cards[0].Headline.Should().Be("1 worked · 1 confirmed");
        vm.Needed[0].Name.Should().Be("Mongolia");
        vm.Needed[0].Heard.Should().BeTrue();
        vm.Needed[0].When.Should().Be("Heard 2 times, always on 20 m between 13:00 and 15:00 UTC, last heard 2 Oct.");
        vm.Needed.Should().NotContain(n => n.Name == "Japan");
        vm.Needed[1].When.Should().Be("Not heard in your decodes yet.");
    }

    [Fact]
    public async Task SelectedScope_Band_ListsCountriesMissingOnThatBand()
    {
        var history = History();
        await history.Refresh();
        var log = LogIndex.Build([new LogContact("JA1AAA", "20m", "FT8", "PM95", "JA", null, false, Now)]);
        var vm = new AwardsViewModel(() => log, ReferenceData.LoadCountries(), history, () => Now, a => a());
        vm.Refresh();

        vm.SelectedScope = "40 m";
        vm.Needed.Should().Contain(n => n.Name == "Japan");

        vm.SelectedScope = "20 m";
        vm.Needed.Should().NotContain(n => n.Name == "Japan");
        vm.IncludeUnconfirmed = true;
        vm.Needed.Should().Contain(n => n.Name == "Japan" && n.Detail.EndsWith("worked, not confirmed"));
    }

    [Fact]
    public async Task Describe_WithName_StartsWithCountry()
    {
        var history = History((At(3, 9), 7_074_000, "K1ABC JT1AA -05"));
        await history.Refresh();

        history.Describe("JT", withName: true).Should().Be("Mongolia: heard once, on 40 m at 09:15 UTC on 3 Oct.");
        history.Describe("JA").Should().BeNull();
    }

    [Fact]
    public async Task RefreshIfOlderThan_Fresh_DoesNotReadAgain()
    {
        var history = History();
        await history.Refresh();
        var first = history.Current;

        await history.RefreshIfOlderThan(HeardHistoryService.MaxAge);

        history.Current.Should().BeSameAs(first);
    }

    [Fact]
    public void StationDetails_HeardHistorySet_ShowsSentence()
    {
        var countries = ReferenceData.LoadCountries();
        var station = new Station { Call = "JT1AA", Entity = countries.Lookup("JT1AA") };
        try
        {
            StationDetailsViewModel.HeardHistory = (call, key) => key == "JT" ? "Mongolia: heard 6 times." : null;

            var d = StationDetailsViewModel.From("JT1AA", station, null, null, DistanceUnit.Miles, Now);

            d.Heard.Should().Be("Mongolia: heard 6 times.");
            d.HasHeard.Should().BeTrue();
        }
        finally
        {
            StationDetailsViewModel.HeardHistory = null;
        }
    }

    [Fact]
    public void FillClusterSpots_NeededSpotOnOtherBand_ListedWithGoTo_AndOnlyAClickChangesBand()
    {
        var log = LogIndex.Build([new LogContact("JA1AAA", "20m", "FT8", "PM95", "JA", null, false, Now)]);
        var goes = new List<(string Band, Mode Mode)>();
        var vm = new AwardsViewModel(() => log, ReferenceData.LoadCountries(), new HeardHistoryService(_dir, ReferenceData.LoadCountries(), new ManualClock(Now)),
            () => Now, a => a())
        {
            Working = () => ("20m", Mode.Ft8),
            Frequencies = ReferenceData.LoadFrequencies(),
            GoToBand = (b, m) => goes.Add((b, m)),
        };
        var spots = Core.Cluster.ClusterSpots.Empty.With(
        [
            new Core.Cluster.ClusterSpot("JA1XYZ", 21_074_000, "DL1ABC", "FT8 -12dB", Now.AddMinutes(-4)),
            new Core.Cluster.ClusterSpot("JA2XYZ", 14_074_000, "DL1ABC", "FT8", Now.AddMinutes(-2)),
            new Core.Cluster.ClusterSpot("VK2XYZ", 21_025_000, "DL1ABC", "CW", Now.AddMinutes(-1)),
        ], Now);

        vm.SetClusterSpots(spots, on: true);

        var row = vm.ClusterSpots.Should().ContainSingle("Japan is worked on 20 m and the VK spot is CW").Subject;
        row.Call.Should().Be("JA1XYZ");
        row.Need.Should().Be("New DXCC 15 m");
        row.Spotted.Should().Be("21074.0 · Spotted 4 min ago on 15 m by DL1ABC · FT8 -12dB");
        row.GoText.Should().Be("Go to 15 m");
        goes.Should().BeEmpty("spots never change band on their own");

        vm.GoToClusterCommand.Execute(row);

        goes.Should().Equal(("15m", Mode.Ft8));
    }

    [Fact]
    public void SetClusterSpots_Off_SaysHowToTurnOn()
    {
        var vm = new AwardsViewModel(() => LogIndex.Empty, ReferenceData.LoadCountries(),
            new HeardHistoryService(_dir, ReferenceData.LoadCountries(), new ManualClock(Now)), () => Now, a => a());

        vm.SetClusterSpots(Core.Cluster.ClusterSpots.Empty, on: false);

        vm.ClusterSpots.Should().BeEmpty();
        vm.ClusterText.Should().Contain("Settings > Who to call > DX cluster");
    }

    [Theory]
    [InlineData(ConfirmationSource.Lotw, false, "1 worked · 0 confirmed")]
    [InlineData(ConfirmationSource.Lotw, true, "1 worked · 1 confirmed")]
    [InlineData(ConfirmationSource.Any, false, "1 worked · 1 confirmed")]
    [InlineData(ConfirmationSource.Any, true, "1 worked · 1 confirmed")]
    public async Task Refresh_ConfirmedBySetting_DxccCountsOnlyThatSource(ConfirmationSource source, bool lotw, string headline)
    {
        var history = History();
        await history.Refresh();
        // Confirmed but not by LoTW: an eQSL or a paper card.
        var log = LogIndex.Build([new LogContact("JA1AAA", "20m", "FT8", "PM95", "JA", null, true, Now, LotwConfirmed: lotw)], source);
        var vm = new AwardsViewModel(() => log, ReferenceData.LoadCountries(), history, () => Now, a => a());

        vm.Refresh();

        vm.Cards[0].Headline.Should().Be(headline);
    }
}
