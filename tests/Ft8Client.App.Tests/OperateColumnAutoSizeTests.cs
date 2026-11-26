// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ft8Client.App.Engine;
using Ft8Client.App.ViewModels;
using Ft8Client.App.Views;
using Ft8Client.Core;
using Ft8Client.Core.Geo;
using Ft8Client.Core.Logbook;
using Ft8Client.Core.Ranking;
using Ft8Client.Core.Stations;

namespace Ft8Client.App.Tests;

public class OperateColumnAutoSizeTests
{
    private static readonly DateTime Slot = new(2026, 11, 26, 19, 16, 0, DateTimeKind.Utc);
    private const string LongPlace = "Saint Vincent and the Grenadines";

    public class NoCommands : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }

    private static RankedStation Cq(string call, string country, double km) =>
        new(new Station
        {
            Call = call, LastHeardUtc = Slot.AddSeconds(-3), State = StationState.CallingCq, CqStreak = 2, LastSnr = -10, DistanceKm = km,
            Entity = new EntityMatch(new Entity("J8", country, "NA", 8, 11, new LatLon(13, -61)), "NA", 8, new LatLon(13, -61)),
        }, new NeedResult(NeedTag.NewCall, 4), new HearsYou(HearsYouKind.None), Chance.Good, true, null);

    private static async Task WithOperate(int width, int height, Action<Window, OperateViewModel> check)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(OperateColumnAutoSizeTests).Assembly);
        await session.Dispatch(() =>
        {
            var vm = new OperateViewModel(DispatchProxy.Create<IOperateCommands, NoCommands>(), () => Slot.AddSeconds(8));
            vm.Apply(new SessionSnapshot
            {
                Mode = Mode.Ft8,
                LastDecodedSlot = Slot,
                Rank = new RankResult([Cq("J88AB", LongPlace, 5000), Cq("J88CD", "Peru", 7000)], [], [], []),
            }, [], 0);
            var window = new Window { Width = width, Height = height, Content = new OperateView { DataContext = vm } };
            window.Show();
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                }
                check(window, vm);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Grid Header(Window window) =>
        window.GetVisualDescendants().OfType<Grid>().First(g => ResizableColumns.GetLayout(g) == "Calls" && ResizableColumns.GetIsHeader(g));

    private static TextBlock Cell(Window window, string text) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text && t.FindAncestorOfType<ListBox>() is not null);

    // The headless test fonts are wider than Segoe UI (every glyph is a full em), so "wide" here means 2560 and up;
    // the before/after previews in the pull request show the real fonts at 1920.
    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(3440, 1440)]
    public async Task OperateView_WideWindowLongPlace_WhereShowsItInFull(int width, int height)
    {
        await WithOperate(width, height, (window, _) =>
        {
            var place = Cell(window, LongPlace);
            var header = Header(window);
            header.ColumnDefinitions[2].Width.Value.Should().BeGreaterThan(160, "Where widens past its default for the long place name");
            (place.Bounds.Width + place.Margin.Left + place.Margin.Right).Should().BeGreaterThanOrEqualTo(ResizableColumns.NaturalWidth(place) - 1, "the place is not cut off");

            // Distance sits in its own column, and Doing now keeps room for its text.
            Cell(window, "3,107 mi").Should().NotBeNull();
            var doing = Cell(window, "Calling CQ, 2 slots");
            doing.Bounds.Width.Should().BeGreaterThanOrEqualTo(ResizableColumns.NaturalWidth(doing) - 1);
        });
    }

    [Fact]
    public async Task OperateView_WideWindowShortPlaces_WhereKeepsDefault()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(OperateColumnAutoSizeTests).Assembly);
        await session.Dispatch(() =>
        {
            var slot = Slot;
            var vm = new OperateViewModel(DispatchProxy.Create<IOperateCommands, NoCommands>(), () => slot.AddSeconds(8));
            vm.Apply(new SessionSnapshot
            {
                Mode = Mode.Ft8, LastDecodedSlot = Slot,
                Rank = new RankResult([Cq("J88CD", "Peru", 7000)], [], [], []),
            }, [], 0);
            var window = new Window { Width = 2560, Height = 1440, Content = new OperateView { DataContext = vm } };
            window.Show();
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                }
                Header(window).ColumnDefinitions[2].Width.Value.Should().Be(160, "a short place needs no more room");

                // The long name arriving later (a re-rank) widens Where; it going away puts Where back.
                slot = slot.AddSeconds(15);
                vm.Apply(new SessionSnapshot
                {
                    Mode = Mode.Ft8, LastDecodedSlot = slot,
                    Rank = new RankResult([Cq("J88AB", LongPlace, 5000)], [], [], []),
                }, [], 0);
                for (var i = 0; i < 4; i++)
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                }
                Header(window).ColumnDefinitions[2].Width.Value.Should().BeGreaterThan(160);

                slot = slot.AddSeconds(15);
                vm.Apply(new SessionSnapshot
                {
                    Mode = Mode.Ft8, LastDecodedSlot = slot,
                    Rank = new RankResult([Cq("J88CD", "Peru", 7000)], [], [], []),
                }, [], 0);
                for (var i = 0; i < 4; i++)
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                }
                Header(window).ColumnDefinitions[2].Width.Value.Should().Be(160);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(1280)]
    [InlineData(1440)]
    public async Task OperateView_NarrowerWindow_WhereNeverWidens(int width)
    {
        await WithOperate(width, 860, (window, _) =>
        {
            Header(window).ColumnDefinitions[2].Width.Value.Should().BeLessThanOrEqualTo(160, "there is no spare room to give Where on a narrower window");
        });
    }

    [Fact]
    public async Task NaturalWidth_TrimmedText_IsFullTextWidth()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(OperateColumnAutoSizeTests).Assembly);
        await session.Dispatch(() =>
        {
            var full = new TextBlock { Text = LongPlace };
            var trimmed = new TextBlock { Text = LongPlace, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, Width = double.NaN };
            full.Measure(Avalonia.Size.Infinity);
            trimmed.Measure(new Avalonia.Size(40, 100));
            ResizableColumns.NaturalWidth(trimmed).Should().BeApproximately(full.DesiredSize.Width, 2);

            ResizableColumns.NaturalWidth(new TextBlock { Text = LongPlace, IsVisible = false }).Should().Be(0);
            ResizableColumns.NaturalWidth(new TextBlock { Text = "", Margin = new Avalonia.Thickness(0, 0, 12, 0) }).Should().Be(12);

            var a = new TextBlock { Text = "New grid" };
            var b = new TextBlock { Text = "EN92" };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { a, b, new TextBlock { Text = "hidden", IsVisible = false } } };
            ResizableColumns.NaturalWidth(row).Should().BeApproximately(ResizableColumns.NaturalWidth(a) + ResizableColumns.NaturalWidth(b) + 6, 0.01);
            var column = new StackPanel { Children = { new TextBlock { Text = "short" }, new TextBlock { Text = LongPlace } } };
            ResizableColumns.NaturalWidth(column).Should().BeApproximately(ResizableColumns.NaturalWidth(new TextBlock { Text = LongPlace }), 0.01);
            ResizableColumns.NaturalWidth(new StackPanel()).Should().Be(0);
            ResizableColumns.NaturalWidth(new Border { Padding = new Avalonia.Thickness(4), Child = new TextBlock { Text = "x", IsVisible = false } }).Should().Be(8);
        }, CancellationToken.None);
    }
}
