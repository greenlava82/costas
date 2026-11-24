// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Reflection;
using Ft8Client.App.Engine;
using Ft8Client.App.ViewModels;
using Ft8Client.Core;
using Ft8Client.Core.Logbook;
using Ft8Client.Core.Ranking;
using Ft8Client.Core.Stations;

namespace Ft8Client.App.Tests;

/// <summary>
/// The station details open in a strip under the clicked row of Next in line or Repeats, instead of a floating card
/// (owner request 2026-11-14). Clicking the row again or Esc closes them; a double-click never opens them.
/// </summary>
public class DetailsStripTests
{
    private static readonly DateTime Slot = new(2026, 11, 14, 14, 30, 0, DateTimeKind.Utc);

    private static RankedStation Cq(string call) =>
        new(new Station { Call = call, LastHeardUtc = Slot, State = StationState.CallingCq, LastSnr = -12 },
            new NeedResult(NeedTag.NewCall, 4), new HearsYou(HearsYouKind.None), Chance.Good, true, null);

    // A call written "r:K1ABC" is in Repeats (already worked); the rest are in Next in line.
    private static SessionSnapshot Snap(params string[] calls) =>
        new()
        {
            Mode = Mode.Ft8, LastDecodedSlot = Slot,
            Rank = new RankResult([.. calls.Where(c => !c.StartsWith("r:", StringComparison.Ordinal)).Select(Cq)], [],
                [.. calls.Where(c => c.StartsWith("r:", StringComparison.Ordinal)).Select(c => Cq(c[2..]))], []),
        };

    // Moved on by NextSlot, so the 300 ms hold after a click is over when the next list arrives.
    private DateTime _now = Slot.AddSeconds(5);

    private readonly IOperateCommands _commands = DispatchProxy.Create<IOperateCommands, MiniViewModelTests.Recorder>();

    private List<string> Called => ((MiniViewModelTests.Recorder)(object)_commands).Calls;

    private OperateViewModel Build(params string[] line)
    {
        var vm = new OperateViewModel(_commands, () => _now);
        vm.Apply(Snap(line), [], 0);
        return vm;
    }

    private void NextSlot(OperateViewModel vm, int n, params string[] line)
    {
        _now = Slot.AddSeconds(15 * n + 5);
        vm.Apply(Snap(line) with { LastDecodedSlot = Slot.AddSeconds(15 * n) }, [], 0);
    }

    private static StationRowViewModel Row(OperateViewModel vm, string call) => vm.Line.Single(r => r.Call == call);

    private static IEnumerable<string> Open(OperateViewModel vm) => vm.Line.Where(r => r.DetailsOpen).Select(r => r.Call);

    // A single click on the call: the press, then the double-click time passing with no second press.
    private static void Click(OperateViewModel vm, StationRowViewModel? row)
    {
        vm.RowClicked(row, showDetails: true);
        vm.CompleteClick();
    }

    // A single click on the row away from the call.
    private static void ClickOffCall(OperateViewModel vm, StationRowViewModel? row)
    {
        vm.RowClicked(row);
        vm.CompleteClick();
    }

    private static void DoubleClick(OperateViewModel vm, StationRowViewModel? first, StationRowViewModel? second)
    {
        vm.RowClicked(first, clickCount: 1);
        vm.RowClicked(second, clickCount: 2);
    }

    [Fact]
    public void RowClicked_NewRow_OpensDetailsUnderThatRowOnly()
    {
        var vm = Build("A", "B", "C");

        Click(vm, Row(vm, "B"));

        Open(vm).Should().Equal("B");
        vm.Details!.Call.Should().Be("B");
    }

    [Fact]
    public void RowClicked_OffTheCall_SelectsWithoutOpeningTheStrip()
    {
        var vm = Build("A", "B");

        ClickOffCall(vm, Row(vm, "B"));

        vm.Selected!.Call.Should().Be("B");
        Open(vm).Should().BeEmpty();
    }

    [Fact]
    public void RowClicked_OffTheCallWhileAStripIsOpen_StripFollowsTheSelection()
    {
        var vm = Build("A", "B");
        Click(vm, Row(vm, "A"));

        ClickOffCall(vm, Row(vm, "B"));

        Open(vm).Should().Equal("B");
    }

    [Fact]
    public void ToggleDetails_RowSelected_OpensItsStripAtOnce()
    {
        var vm = Build("A", "B");
        ClickOffCall(vm, Row(vm, "B"));

        vm.ToggleDetails();

        Open(vm).Should().Equal("B");
    }

    [Fact]
    public void RowClicked_BeforeTheDoubleClickTimePasses_RowSelectedStripNotOpenYet()
    {
        var vm = Build("A", "B");

        vm.RowClicked(Row(vm, "B"));

        vm.Selected!.Call.Should().Be("B");
        Open(vm).Should().BeEmpty();
    }

    [Fact]
    public void RowClicked_OtherRow_OldStripClosesAtOnceNewOneOpensAfterTheWait()
    {
        var vm = Build("A", "B");
        Click(vm, Row(vm, "A"));

        vm.RowClicked(Row(vm, "B"));
        Open(vm).Should().BeEmpty();
        vm.CompleteClick();

        Open(vm).Should().Equal("B");
    }

    [Fact]
    public void RowClicked_SelectedRowAgain_DetailsCloseRowStaysSelected()
    {
        var vm = Build("A", "B");
        Click(vm, Row(vm, "A"));

        Click(vm, Row(vm, "A"));

        Open(vm).Should().BeEmpty();
        vm.Details.Should().BeNull();
        vm.Selected!.Call.Should().Be("A", "Enter still calls the row the operator picked");
    }

    [Fact]
    public void RowClicked_ClosedRowAgain_DetailsOpenAgain()
    {
        var vm = Build("A");
        Click(vm, Row(vm, "A"));
        Click(vm, Row(vm, "A"));

        Click(vm, Row(vm, "A"));

        Open(vm).Should().Equal("A");
    }

    [Fact]
    public void RowClicked_DoubleClick_CallsTheRowAndNeverOpensItsStrip()
    {
        var vm = Build("A", "B");

        DoubleClick(vm, Row(vm, "B"), Row(vm, "B"));
        vm.CompleteClick();

        Called.Should().Equal("B");
        Open(vm).Should().BeEmpty();
        vm.Selected!.Call.Should().Be("B");
    }

    [Fact]
    public void RowClicked_DoubleClickOnTheOpenRow_CallsAndTheStripStaysOpen()
    {
        var vm = Build("A");
        Click(vm, Row(vm, "A"));

        DoubleClick(vm, Row(vm, "A"), Row(vm, "A"));
        vm.CompleteClick();

        Called.Should().Equal("A");
        Open(vm).Should().Equal("A");
    }

    [Fact]
    public void RowClicked_SecondClickLandsOnAnotherRow_CallsTheRowTheFirstClickPicked()
    {
        var vm = Build("A", "B");
        Click(vm, Row(vm, "A"));

        DoubleClick(vm, Row(vm, "B"), Row(vm, "A"));

        Called.Should().Equal(new[] { "B" }, "A's strip closing moved A under a pointer that did not move");
        vm.Selected!.Call.Should().Be("B");
    }

    [Fact]
    public void RowClicked_SecondClickLandsInTheStrip_CallsTheRow()
    {
        var vm = Build("A");

        DoubleClick(vm, Row(vm, "A"), null);

        Called.Should().Equal("A");
    }

    [Fact]
    public void RowClicked_DoubleClickInTheStrip_CallsNobodyStripStaysOpen()
    {
        var vm = Build("A");
        Click(vm, Row(vm, "A"));

        DoubleClick(vm, null, null);
        vm.CompleteClick();

        Called.Should().BeEmpty();
        Open(vm).Should().Equal("A");
    }

    [Fact]
    public void CloseDetails_Open_ClosesRowStaysSelected()
    {
        var vm = Build("A");
        Click(vm, Row(vm, "A"));

        vm.CloseDetails();

        Open(vm).Should().BeEmpty();
        vm.Selected!.Call.Should().Be("A");
    }

    [Fact]
    public void Apply_NewSlotSelectedStationStillHere_DetailsStayOpenUnderItsNewRow()
    {
        var vm = Build("A", "B");
        Click(vm, Row(vm, "B"));

        NextSlot(vm, 1, "C", "B", "A");

        Open(vm).Should().Equal("B");
    }

    [Fact]
    public void Apply_SelectedStationLeavesTheLine_DetailsClose()
    {
        var vm = Build("A", "B");
        Click(vm, Row(vm, "B"));

        NextSlot(vm, 1, "A");
        NextSlot(vm, 2, "A", "B");

        Open(vm).Should().BeEmpty();
        vm.Details.Should().BeNull();
    }

    [Fact]
    public void MoveSelection_Down_DetailsFollowTheSelection()
    {
        var vm = Build("A", "B");
        Click(vm, Row(vm, "A"));

        vm.MoveSelection(1);

        Open(vm).Should().Equal("B");
    }

    private static RepeatRowViewModel Repeat(OperateViewModel vm, string call) => vm.Repeats.Single(r => r.Call == call);

    private static IEnumerable<string> OpenRepeats(OperateViewModel vm) => vm.Repeats.Where(r => r.DetailsOpen).Select(r => r.Call);

    private static void Click(OperateViewModel vm, RepeatRowViewModel row)
    {
        vm.RepeatClicked(row, showDetails: true);
        vm.CompleteClick();
    }

    [Fact]
    public void RepeatClicked_OffTheCall_StripStaysClosed()
    {
        var vm = Build("r:K1ABC");

        vm.RepeatClicked(Repeat(vm, "K1ABC"));
        vm.CompleteClick();

        OpenRepeats(vm).Should().BeEmpty();
    }

    [Fact]
    public void RepeatClicked_Row_OpensDetailsUnderItOnly()
    {
        var vm = Build("A", "r:K1ABC", "r:K2DEF");

        Click(vm, Repeat(vm, "K2DEF"));

        OpenRepeats(vm).Should().Equal("K2DEF");
        Open(vm).Should().BeEmpty();
        vm.Details!.Call.Should().Be("K2DEF");
    }

    [Fact]
    public void ShowRepeats_TurnedOffWithARepeatsStripOpen_StripCloses()
    {
        var vm = Build("A", "r:K1ABC");
        Click(vm, Repeat(vm, "K1ABC"));

        vm.ShowRepeats = false;

        OpenRepeats(vm).Should().BeEmpty();
        vm.Details.Should().BeNull();
    }

    [Fact]
    public void RepeatClicked_LineRowSelected_LineRowDeselectedAndItsStripCloses()
    {
        var vm = Build("A", "r:K1ABC");
        Click(vm, Row(vm, "A"));

        Click(vm, Repeat(vm, "K1ABC"));

        Open(vm).Should().BeEmpty();
        vm.Selected.Should().BeNull("Enter must not call a line row the operator has moved away from");
        OpenRepeats(vm).Should().Equal("K1ABC");
    }

    [Fact]
    public void RowClicked_RepeatOpen_DetailsMoveToTheLine()
    {
        var vm = Build("A", "r:K1ABC");
        Click(vm, Repeat(vm, "K1ABC"));

        Click(vm, Row(vm, "A"));

        OpenRepeats(vm).Should().BeEmpty();
        Open(vm).Should().Equal("A");
    }

    [Fact]
    public void RepeatClicked_OpenRowAgain_DetailsClose()
    {
        var vm = Build("r:K1ABC");
        Click(vm, Repeat(vm, "K1ABC"));

        Click(vm, Repeat(vm, "K1ABC"));

        OpenRepeats(vm).Should().BeEmpty();
        vm.Details.Should().BeNull();
    }

    [Fact]
    public void RepeatClicked_DoubleClick_StripNeverOpens()
    {
        var vm = Build("r:K1ABC");

        vm.RepeatClicked(Repeat(vm, "K1ABC"), clickCount: 1);
        vm.RepeatClicked(Repeat(vm, "K1ABC"), clickCount: 2);
        vm.CompleteClick();

        OpenRepeats(vm).Should().BeEmpty();
    }

    [Fact]
    public void Apply_RepeatStillHere_DetailsStayOpenUnderItsNewRow()
    {
        var vm = Build("r:K1ABC", "r:K2DEF");
        Click(vm, Repeat(vm, "K2DEF"));

        NextSlot(vm, 1, "r:K2DEF", "r:K1ABC");

        OpenRepeats(vm).Should().Equal("K2DEF");
    }

    [Fact]
    public void Apply_RepeatLeaves_DetailsClose()
    {
        var vm = Build("A", "r:K1ABC");
        Click(vm, Repeat(vm, "K1ABC"));

        NextSlot(vm, 1, "A");
        NextSlot(vm, 2, "A", "r:K1ABC");

        OpenRepeats(vm).Should().BeEmpty();
        Open(vm).Should().BeEmpty();
        vm.Details.Should().BeNull();
    }

    [Fact]
    public void PastShown_FiveContacts_LatestThreeAndTwoMore()
    {
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var log = LogIndex.Build([.. Enumerable.Range(0, 5).Select(i => new LogContact("JA1AAA", "20m", "FT8", "PM95", "JA", null, false, day.AddDays(i)))]);

        var d = StationDetailsViewModel.From("JA1AAA", null, null, log, DistanceUnit.Miles, Slot);

        d.PastShown.Select(p => p.Date).Should().Equal("2026-01-05", "2026-01-04", "2026-01-03");
        d.PastMoreText.Should().Be("+2 more");
        d.HasPastMore.Should().BeTrue();
    }

    [Fact]
    public void PastShown_ThreeContacts_AllShownNoMore()
    {
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var log = LogIndex.Build([.. Enumerable.Range(0, 3).Select(i => new LogContact("JA1AAA", "20m", "FT8", "PM95", "JA", null, false, day.AddDays(i)))]);

        var d = StationDetailsViewModel.From("JA1AAA", null, null, log, DistanceUnit.Miles, Slot);

        d.PastShown.Should().HaveCount(3);
        d.HasPastMore.Should().BeFalse();
        d.PastMoreText.Should().BeEmpty();
    }
}
