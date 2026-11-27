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
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ft8Client.App.Engine;
using Ft8Client.App.ViewModels;
using Ft8Client.App.Views;
using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Logbook;
using Ft8Client.Core.Messages;
using Grid = Avalonia.Controls.Grid;
using Ft8Client.Core.Ranking;
using Ft8Client.Core.Stations;

namespace Ft8Client.App.Tests;

// The PSK and RTTY Operate clean-up (owner pick 2026-11-27): FT8-only columns hide, the band summary folds to its strip,
// the readout shows only while sending, the conversation box takes spare room, Raw decodes drops DT and counts CQs.
public class PskRttyFocusTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 1, 0, 0, DateTimeKind.Utc);

    public class NoCommands : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }

    private static RankedStation Cq(string call) =>
        new(new Station { Call = call, LastHeardUtc = Now.AddSeconds(-3), State = StationState.CallingCq, LastSnr = -10, RecentText = "CQ CQ DE " + call + " K" },
            new NeedResult(NeedTag.NewCall, 4), new HearsYou(HearsYouKind.None), Chance.Good, true, null);

    private static SessionSnapshot Snap(Mode mode) => new()
    {
        Mode = mode,
        Band = "20m",
        LastDecodedSlot = Now,
        Summary = new BandSummary { Band = "20m", HeardFrom = new Dictionary<string, int>() },
        Rank = new RankResult([Cq("KP4JRS"), Cq("9A1AA")], [], [Cq("W1AW")], []),
    };

    private static OperateViewModel Vm(SessionSnapshot s)
    {
        var vm = new OperateViewModel(DispatchProxy.Create<IOperateCommands, NoCommands>(), () => Now);
        vm.Apply(s, [], 0);
        return vm;
    }

    // ---- Column layout ----

    [Fact]
    public void Fit_HiddenColumns_ZeroWidthAndStarKeepsTheRoom()
    {
        var layout = new ColumnLayout([36, 100, double.NaN, 64, 230, 74]);

        layout.Hide([3, 4]);

        layout.Fit(1200).Should().Equal(36, 100, double.NaN, 0, 0, 74);
        layout.Widths.Should().Equal(36, 100, double.NaN, 64, 230, 74); // the stored widths are kept for FT8
    }

    [Fact]
    public void Hide_EmptyList_ColumnsComeBackAsTheyWere()
    {
        var layout = new ColumnLayout([36, 100, double.NaN, 64, 230, 74]);
        layout.SetWidth(4, 180);
        layout.Hide([3, 4]);

        layout.Hide([]);

        layout.Fit(1200).Should().Equal(36, 100, double.NaN, 64, 180, 74);
    }

    [Fact]
    public void Hide_SameColumnsAgain_RaisesNothing()
    {
        var layout = new ColumnLayout([36, 100, double.NaN, 64, 230, 74]);
        layout.Hide([4, 3]);
        var raised = 0;
        layout.Changed += () => raised++;

        layout.Hide([3, 4]);
        layout.Hide([3, 3, 4, 99]);

        raised.Should().Be(0);
    }

    [Fact]
    public void Fit_NarrowWindowWithHiddenShrinkColumn_DoesNotBringItBack()
    {
        // Chance (4) is both hidden and the first to shrink; room freed by dropping a column goes to the others only.
        var layout = new ColumnLayout([36, 100, double.NaN, 120, 230, 74]);
        layout.GiveWay([(4, 120), (3, 60)], [0]);
        layout.Hide([4]);

        var w = layout.Fit(300);

        w[4].Should().Be(0);
        w.Where(x => !double.IsNaN(x)).Sum().Should().BeLessThanOrEqualTo(300);
        w[5].Should().Be(74, "the Call buttons keep their width");
    }

    [Fact]
    public void MoveEdge_BesideHiddenColumn_DoesNothing()
    {
        var layout = new ColumnLayout([36, 100, 64, 230, double.NaN]);
        layout.Hide([2]);

        layout.MoveEdge(1, 30);
        layout.MoveEdge(2, 30);

        layout.Widths.Should().Equal(36, 100, 64, 230, double.NaN);
    }

    // ---- Operate view model ----

    [Theory]
    [InlineData(Mode.Psk31)]
    [InlineData(Mode.Psk63)]
    [InlineData(Mode.Rtty)]
    public void Apply_KeyboardModeIdle_SummaryFoldsToStrip(Mode mode)
    {
        var vm = Vm(Snap(mode));

        vm.ShowSummaryCard.Should().BeFalse();
        vm.ShowStrip.Should().BeTrue();
        vm.DoingNowHeader.Should().Be("Sending now");
    }

    [Theory]
    [InlineData(Mode.Ft8)]
    [InlineData(Mode.Ft4)]
    public void Apply_SlottedModeIdle_SummaryCardAsBefore(Mode mode)
    {
        var vm = Vm(Snap(mode));

        vm.ShowSummaryCard.Should().BeTrue();
        vm.ShowStrip.Should().BeFalse();
        vm.DoingNowHeader.Should().Be("Doing now");
        vm.SlotReadoutShown.Should().BeTrue();
    }

    [Fact]
    public void Apply_KeyboardModeNoSummaryYet_NoStrip()
    {
        var vm = Vm(Snap(Mode.Psk31) with { Summary = null });

        vm.ShowStrip.Should().BeFalse();
        vm.ShowSummaryCard.Should().BeFalse();
    }

    [Fact]
    public void Apply_KeyboardModeCompactWithBandTab_StripHiddenAsBefore()
    {
        var vm = Vm(Snap(Mode.Psk31));
        vm.Compact = true;
        vm.SideCollapsed = false;
        vm.SideTab = OperateViewModel.BandTab;

        vm.ShowStrip.Should().BeFalse("the Band tab already shows the summary");
    }

    [Fact]
    public void Apply_PskThenFt8_SummaryCardComesBack()
    {
        var vm = Vm(Snap(Mode.Psk31));

        vm.Apply(Snap(Mode.Ft8), [], 0);

        vm.ShowSummaryCard.Should().BeTrue();
        vm.ShowStrip.Should().BeFalse();
        vm.DoingNowHeader.Should().Be("Doing now");
    }

    [Fact]
    public void Apply_KeyboardIdleNothingTyped_ReadoutHidden()
    {
        Vm(Snap(Mode.Rtty)).SlotReadoutShown.Should().BeFalse();
    }

    [Fact]
    public void Apply_KeyboardOverOnAir_ReadoutShown()
    {
        var vm = Vm(Snap(Mode.Psk31) with { KeyboardTx = new KeyboardTxView(true, "CQ CQ ", "DE W7LIT K", false) });

        vm.SlotReadoutShown.Should().BeTrue();
        vm.SlotLabel.Should().Be("Sending");
    }

    [Fact]
    public void Apply_KeyboardTextTypedAhead_ReadoutShown()
    {
        var vm = Vm(Snap(Mode.Psk31));
        vm.Keyboard.Typed = "hello";

        vm.Apply(Snap(Mode.Psk31), [], 0);

        vm.SlotReadoutShown.Should().BeTrue();
        vm.SlotLabel.Should().Be("Typed ahead");
    }

    // ---- Conversation box ----

    [Theory]
    [InlineData(500, 300, 200)]
    [InlineData(500, 450, OperateView.ConversationMin)]
    [InlineData(900, 300, OperateView.ConversationMax)]
    [InlineData(100, 300, OperateView.ConversationMin)]
    public void ConversationHeight_Room_GrowsIntoSpareRoomWithinLimits(double room, double rest, double expected) =>
        OperateView.ConversationHeight(room, rest).Should().Be(expected);

    // ---- The real view ----

    private static T Find<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static Grid CallsHeader(Window w) =>
        w.GetVisualDescendants().OfType<Grid>().First(g => ResizableColumns.GetLayout(g) == "Calls" && ResizableColumns.GetIsHeader(g));

    private static void Settle(Window w)
    {
        for (var i = 0; i < 4; i++)
        {
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public async Task OperateView_Psk_SignalAndChanceHiddenAndBackInFt8()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(PskRttyFocusTests).Assembly);
        await session.Dispatch(() =>
        {
            var vm = Vm(Snap(Mode.Psk31));
            var window = new Window { Width = 1600, Height = 1000, Content = new OperateView { DataContext = vm } };
            window.Show();
            try
            {
                Settle(window);
                var header = CallsHeader(window);
                foreach (var c in OperateView.KeyboardHiddenCalls) header.ColumnDefinitions[c].Width.Value.Should().Be(0);
                header.Children.OfType<TextBlock>().Where(t => t.Text is "Signal" or "Chance").Should().OnlyContain(t => !t.IsVisible);
                header.Children.OfType<TextBlock>().Should().Contain(t => t.Text == "Sending now" && t.IsVisible);
                window.GetVisualDescendants().OfType<SignalSparkline>().Should().OnlyContain(s => !s.IsEffectivelyVisible);

                vm.Apply(Snap(Mode.Ft8), [], 0);
                Settle(window);

                foreach (var c in OperateView.KeyboardHiddenCalls) header.ColumnDefinitions[c].Width.Value.Should().BeGreaterThan(0);
                header.Children.OfType<TextBlock>().Where(t => t.Text is "Signal" or "Chance").Should().HaveCount(2).And.OnlyContain(t => t.IsVisible);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OperateView_PskIdleTallWindow_ConversationTallerAndSummaryStrip()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(PskRttyFocusTests).Assembly);
        await session.Dispatch(() =>
        {
            var vm = Vm(Snap(Mode.Psk31));
            var window = new Window { Width = 1600, Height = 1100, Content = new OperateView { DataContext = vm } };
            window.Show();
            try
            {
                Settle(window);
                Find<Border>(window, "SummaryCard").IsVisible.Should().BeFalse();
                Find<Border>(window, "BandStrip").IsVisible.Should().BeTrue();
                var box = Find<ScrollViewer>(window, "ConversationScroll");
                box.Height.Should().BeGreaterThan(OperateView.ConversationMin).And.BeLessThanOrEqualTo(OperateView.ConversationMax);
                Find<StackPanel>(window, "CardSlot").Children.Should().OnlyContain(c => !c.IsVisible, "idle with nothing typed, the readout hides");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OperateView_PskShortWindow_ConversationKeepsItsOldHeight()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(PskRttyFocusTests).Assembly);
        await session.Dispatch(() =>
        {
            var vm = Vm(Snap(Mode.Psk31));
            var window = new Window { Width = 1300, Height = 480, Content = new OperateView { DataContext = vm } };
            window.Show();
            try
            {
                Settle(window);
                Find<ScrollViewer>(window, "ConversationScroll").Height.Should().Be(OperateView.ConversationMin);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    // ---- Raw decodes ----

    private static RawDecodeLine KeyboardLine(DateTime utc, string text, bool cq = false) =>
        new(new Decode(utc, -6, 0, 720, text, false), MessageParser.Parse(text), "Puerto Rico", false, Mode: Mode.Psk31, TextCq: cq);

    [Fact]
    public void RawLine_Keyboard_ClockTime()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test-7", TimeSpan.FromHours(-7), "Test-7", "Test-7");
        var line = new RawLineViewModel(KeyboardLine(new DateTime(2026, 11, 26, 18, 46, 5, DateTimeKind.Utc), "CQ CQ DE KP4JRS K"), zone, keyboard: true);

        line.Utc.Should().Be("18:46:05");
        line.Local.Should().Be("11:46:05");
        line.Dt.Should().BeEmpty();
    }

    [Fact]
    public void RawDecodes_KeyboardThenFt8_DtHeadingFollows()
    {
        var vm = new RawDecodesViewModel(_ => { });
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Apply([KeyboardLine(Now, "CQ CQ DE KP4JRS K", cq: true)], keyboard: true);
        vm.DtHeader.Should().BeEmpty();
        changed.Should().Contain(nameof(RawDecodesViewModel.DtHeader));

        vm.Apply([], keyboard: false);
        vm.DtHeader.Should().Be("DT");
    }

    [Fact]
    public void RawDecodes_KeyboardCqLines_CountedAndShownByCqFilter()
    {
        var vm = new RawDecodesViewModel(_ => { });
        vm.Apply(
        [
            KeyboardLine(Now, "CQ CQ CQ DE KP4JRS KP4JRS K", cq: true),
            KeyboardLine(Now.AddSeconds(5), "EA8TL DE JA7QVI GE OM UR 599"),
            KeyboardLine(Now.AddSeconds(9), "CQ CQ DE 9A1AA 9A1AA K", cq: true),
        ], keyboard: true);

        vm.ShowOptions.Single(o => o.Choice == RawDecodesViewModel.ShowCq).Count.Should().Be("2");
        vm.Show = RawDecodesViewModel.ShowCq;
        vm.Lines.Where(l => l.IsLine).Select(l => l.Message).Should().Equal("CQ CQ CQ DE KP4JRS KP4JRS K", "CQ CQ DE 9A1AA 9A1AA K");
    }

    [Fact]
    public void RawDecodeLine_Ft8Cq_StillCq()
    {
        new RawDecodeLine(new Decode(Now, -6, 0.1, 720, "CQ K1ABC FN42", false), MessageParser.Parse("CQ K1ABC FN42"), null, false).IsCq.Should().BeTrue();
        new RawDecodeLine(new Decode(Now, -6, 0.1, 720, "K1ABC W7LIT -10", false), MessageParser.Parse("K1ABC W7LIT -10"), null, false).IsCq.Should().BeFalse();
    }
}
