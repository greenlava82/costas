// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ft8Client.App.Engine;
using Ft8Client.App.ViewModels;
using Ft8Client.App.Views;
using Ft8Client.Core;
using Ft8Client.Core.Contacts;
using Ft8Client.Core.Logbook;
using Ft8Client.Core.Messages;
using Grid = Avalonia.Controls.Grid;
using Ft8Client.Core.Geo;
using Ft8Client.Core.Ranking;
using Ft8Client.Core.Stations;
using Ft8Client.Core.Time;

namespace Ft8Client.App.Tests;

/// <summary>
/// Calling you rows must never draw over each other: a long place name pushed a row past its width cap, its
/// "Will answer after this contact" button spilled out, and the next caller was drawn on top of it (issue report
/// 2026-11-23, Linux).
/// </summary>
public class CallingYouLayoutTests
{
    private static readonly DateTime Slot = new(2026, 11, 23, 9, 55, 45, DateTimeKind.Utc);

    private static readonly ContactView InContact =
        new("DL3MIB", "JO31", ContactFlow.CalledCq, ContactOutcome.InProgress, -16, null, [], false, false, string.Empty);

    private static readonly Entity Germany = new("DL", "Fed. Rep. of Germany", "EU", 14, 28, new LatLon(51, 10));

    private static Station Sta(string call, string city) =>
        new() { Call = call, City = city, Entity = new EntityMatch(Germany, "EU", 14, new LatLon(51, 10)), LastHeardUtc = Slot };

    private static RankedStation Ranked(Station s) =>
        new(s, new NeedResult(NeedTag.NewCall, 1), new HearsYou(HearsYouKind.None), Chance.Good, true, null);

    internal static SessionSnapshot Callers()
    {
        var a = Sta("DK2ABC", "Mönchengladbach-Rheydt");
        var b = Sta("DO5GSH", "Düsseldorf");
        var c = Sta("DL1XYZ", "Garmisch-Partenkirchen");
        return new SessionSnapshot
        {
            Mode = Mode.Ft8,
            Contact = InContact,
            Stations = [a, b, c],
            Rank = new RankResult([], [], [], [Ranked(a), Ranked(b), Ranked(c)]),
            Waiting =
            [
                new WaitingCaller("DK2ABC", "JO31", -12, MessageKind.GridReply, null, SlotParity.Even, 900, Slot.AddSeconds(-15), Slot, AnswerAfter: true),
                new WaitingCaller("DO5GSH", "JO31", -8, MessageKind.GridReply, null, SlotParity.Even, 1400, Slot, Slot),
                new WaitingCaller("DL1XYZ", "JN57", -20, MessageKind.GridReply, null, SlotParity.Even, 2100, Slot, Slot),
            ],
        };
    }

    [Theory]
    [InlineData(1920)]
    [InlineData(1400)]
    [InlineData(1100)]
    [InlineData(800)]
    public async Task CallingYou_LongPlaceNames_RowsDoNotOverlap(int width)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(CallingYouLayoutTests).Assembly);
        await session.Dispatch(() =>
        {
            var vm = new OperateViewModel(DispatchProxy.Create<IOperateCommands, ResizableColumnsTests.NoCommands>(), () => Slot.AddSeconds(5));
            vm.Apply(Callers(), [], 0);
            var view = new OperateView { DataContext = vm };
            var window = new Window { Width = width, Height = 900, Content = view };
            window.Show();
            try
            {
                Settle(window);
                var card = view.FindControl<Border>("CallingYou")!;
                card.IsEffectivelyVisible.Should().BeTrue();
                var rows = card.GetVisualDescendants().OfType<Grid>().Where(g => g.DataContext is WaitingRowViewModel).ToList();
                rows.Should().HaveCount(3);

                var boxes = new List<(string What, Rect Box)>();
                foreach (var row in rows)
                {
                    var call = ((WaitingRowViewModel)row.DataContext!).Call;
                    var rowBox = Box(row, card);
                    rowBox.Right.Should().BeLessThanOrEqualTo(card.Bounds.Width + 0.5, $"{call}'s row stays inside the card at {width} px");
                    foreach (var part in row.Children.Where(c => c.IsVisible))
                    {
                        var box = Box(part, card);
                        box.Left.Should().BeGreaterThanOrEqualTo(rowBox.Left - 0.5, $"{call}: {Name(part)} starts inside its row at {width} px");
                        box.Right.Should().BeLessThanOrEqualTo(rowBox.Right + 0.5, $"{call}: {Name(part)} ends inside its row at {width} px");
                        boxes.Add(($"{call} {Name(part)}", box));
                    }
                    // The text block is measured to its words: nothing in it is cut by the column it sits in.
                    foreach (var text in row.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Parent is StackPanel))
                        text.DesiredSize.Width.Should().BeLessThanOrEqualTo(text.Bounds.Width + 0.5, $"{call}: '{text.Text}' fits at {width} px");
                }

                for (var i = 0; i < boxes.Count; i++)
                for (var j = i + 1; j < boxes.Count; j++)
                    boxes[i].Box.Intersects(boxes[j].Box).Should().BeFalse($"{boxes[i].What} and {boxes[j].What} do not overlap at {width} px");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Rect Box(Control c, Visual to) => new(c.TranslatePoint(default, to)!.Value, c.Bounds.Size);

    private static string Name(Control c) => c is Button b ? AutomationProperties.GetName(b) ?? "button" : "text";

    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
