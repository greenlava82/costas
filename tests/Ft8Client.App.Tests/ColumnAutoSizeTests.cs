// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Views;

namespace Ft8Client.App.Tests;

public class ColumnAutoSizeTests
{
    // # | Where | Why | Doing now (star) | Call button: the shape of the Operate tables, small.
    private static ColumnLayout Layout()
    {
        var layout = new ColumnLayout([30, 100, 80, double.NaN, 60]);
        layout.AutoSize(star: 3, grow: [1, 2]);
        return layout;
    }

    private static readonly object Row = new();

    [Fact]
    public void Fit_TextCutOffAndStarHasSpare_WidensToText()
    {
        var layout = Layout();
        layout.ReportNeeds(Row, [0, 170, 70, 120, 0]);

        // Fixed 270; Doing now keeps 120 for its text; 600 leaves 210 spare, Where needs 70 more.
        layout.Fit(600).Should().Equal(30, 170, 80, double.NaN, 60);
    }

    [Fact]
    public void Fit_BothCutOffLittleSpare_SharesInProportion()
    {
        var layout = Layout();
        layout.ReportNeeds(Row, [0, 160, 110, 100, 0]);

        // Spare 430 - 270 - 100 = 60; Where is 60 short and Why 30 short, so they get 40 and 20.
        layout.Fit(430).Should().Equal(30, 140, 100, double.NaN, 60);
    }

    [Fact]
    public void Fit_StarNeedsAllItsRoom_DoesNotWiden()
    {
        var layout = Layout();
        layout.ReportNeeds(Row, [0, 300, 200, 250, 0]);
        layout.Fit(500).Should().Equal(30, 100, 80, double.NaN, 60);
    }

    [Fact]
    public void Fit_StarTextUnknown_KeepsMinimumStarRoom()
    {
        var layout = Layout();
        layout.ReportNeeds(Row, [0, 300, 0, 0, 0]);
        layout.Fit(400).Should().Equal(30, 100 + 400 - 270 - ColumnLayout.MinStarRoom, 80, double.NaN, 60);
    }

    [Fact]
    public void Fit_TextFits_NeverNarrows()
    {
        var layout = Layout();
        layout.ReportNeeds(Row, [0, 40, 30, 50, 0]);
        layout.Fit(2000).Should().Equal(30, 100, 80, double.NaN, 60);
    }

    [Fact]
    public void Fit_NoNeedsReported_KeepsWidths()
    {
        Layout().Fit(2000).Should().Equal(30, 100, 80, double.NaN, 60);
    }

    [Fact]
    public void Fit_WithoutAutoSize_IgnoresNeeds()
    {
        var layout = new ColumnLayout([30, 100, 80, double.NaN, 60]);
        layout.ReportNeeds(Row, [0, 300, 300, 0, 0]);
        layout.Fit(2000).Should().Equal(30, 100, 80, double.NaN, 60);
        layout.MeasuredColumns.Should().BeEmpty();
    }

    [Fact]
    public void Fit_OperatorDraggedColumn_KeepsTheirWidth()
    {
        var layout = Layout();
        layout.ReportNeeds(Row, [0, 300, 200, 50, 0]);
        layout.MoveEdge(1, -20);

        var w = layout.Fit(2000);

        w[1].Should().Be(80, "the operator narrowed Where on purpose");
        w[2].Should().Be(100, "Why, the other side of the dragged edge, is the operator's now too");
        layout.IsAutoSized(1).Should().BeFalse();
    }

    [Fact]
    public void Fit_OperatorWidenedPastText_KeepsTheirWidth()
    {
        var layout = Layout();
        layout.ReportNeeds(Row, [0, 150, 0, 50, 0]);
        layout.SetWidth(1, 400);
        layout.Fit(2000)[1].Should().Be(400);
    }

    [Fact]
    public void Reset_DraggedColumn_AutoSizesAgain()
    {
        var layout = Layout();
        layout.ReportNeeds(Row, [0, 170, 0, 50, 0]);
        layout.MoveEdge(1, -20);
        var raised = 0;
        layout.Changed += () => raised++;

        layout.Reset(1);

        layout.IsAutoSized(1).Should().BeTrue();
        layout.Fit(2000)[1].Should().Be(170);
        raised.Should().Be(1);
    }

    [Fact]
    public void Reset_DraggedBackToDefault_StillAutoSizesAgain()
    {
        var layout = Layout();
        layout.ReportNeeds(Row, [0, 170, 0, 50, 0]);
        layout.MoveEdge(1, -20);
        layout.MoveEdge(1, 20);
        layout.Widths[1].Should().Be(100);
        layout.IsAutoSized(1).Should().BeFalse("a drag makes the width the operator's, even back at the default");
        var raised = 0;
        layout.Changed += () => raised++;

        layout.Reset(1);

        layout.Fit(2000)[1].Should().Be(170);
        raised.Should().Be(1, "the shown width changes, so the tables must re-apply");
    }

    [Fact]
    public void Load_SavedOwnWidth_IsNotAutoSized()
    {
        var layout = Layout();
        layout.Load([30, 120, 80, 60]);
        layout.IsAutoSized(1).Should().BeFalse();
        layout.IsAutoSized(2).Should().BeTrue("Why was saved at its default width");
    }

    [Fact]
    public void Fit_TableTooNarrow_GivesWayWithoutWidening()
    {
        var layout = Layout();
        layout.GiveWay([(1, 60)], [0]);
        layout.ReportNeeds(Row, [0, 300, 0, 50, 0]);

        layout.Fit(250).Should().Equal(30, 80, 80, double.NaN, 60);
    }

    [Fact]
    public void Fit_HiddenColumn_StaysHidden()
    {
        var layout = new ColumnLayout([30, 100, 80, double.NaN, 60]);
        layout.GiveWay([(2, 70)], [1]);
        layout.AutoSize(star: 3, grow: [1, 2]);
        layout.ReportNeeds(Row, [0, 300, 0, 0, 0]);

        var w = layout.Fit(150);

        w[1].Should().Be(0);
    }

    [Fact]
    public void Fit_AutoColumnBesideStar_KeepsItsRoom()
    {
        // Watching: ... | Doing now (star) | Call when free (auto).
        var layout = new ColumnLayout([100, 80, double.NaN, double.NaN]);
        layout.AutoSize(star: 2, grow: [0, 1]);
        layout.ReportNeeds(Row, [200, 0, 60, 120]);

        // 500 - 180 fixed - 120 button - 60 Doing now leaves 140; Where is 100 short.
        layout.Fit(500)[0].Should().Be(200);
        layout.Fit(400)[0].Should().Be(140, "only 40 is spare once the button and Doing now have their room");
    }

    [Fact]
    public void MeasuredColumns_AutoSizeStarAndAutoColumns()
    {
        var layout = new ColumnLayout([100, 80, 50, double.NaN, double.NaN]);
        layout.AutoSize(star: 3, grow: [0, 1, 1, 3, 9]);
        layout.MeasuredColumns.Should().Equal(0, 1, 3, 4);
    }

    [Fact]
    public void AutoSize_StarNotAStarColumn_IsOff()
    {
        var layout = new ColumnLayout([30, 100, 80, double.NaN, 60]);
        layout.AutoSize(star: 1, grow: [2]);
        layout.ReportNeeds(Row, [0, 0, 300, 0, 0]);
        layout.Fit(2000)[2].Should().Be(80);
        layout.IsAutoSized(2).Should().BeFalse();
    }

    [Fact]
    public void ReportNeeds_SeveralRows_TakesWidest()
    {
        var layout = Layout();
        object a = new(), b = new();
        layout.ReportNeeds(a, [0, 120, 50, 0, 0]);
        layout.ReportNeeds(b, [0, 90, 70, 0, 0]);
        layout.Needs.Should().Equal(0, 120, 70, 0, 0);
    }

    [Fact]
    public void ReportNeeds_RowGone_NeedDropsBack()
    {
        var layout = Layout();
        object a = new(), b = new();
        layout.ReportNeeds(a, [0, 120, 0, 0, 0]);
        layout.ReportNeeds(b, [0, 90, 0, 0, 0]);
        layout.ReportNeeds(a, null);
        layout.Needs[1].Should().Be(90);
        layout.ReportNeeds(b, null);
        layout.Needs[1].Should().Be(0);
    }

    [Fact]
    public void ReportNeeds_RaisesChangedOnlyWhenWidestChanges()
    {
        var layout = Layout();
        object a = new(), b = new();
        var raised = 0;
        layout.Changed += () => raised++;

        layout.ReportNeeds(a, [0, 120, 0, 0, 0]);
        layout.ReportNeeds(a, [0, 120, 0, 0, 0]);
        layout.ReportNeeds(b, [0, 100, 0, 0, 0]);
        layout.ReportNeeds(b, null);
        layout.ReportNeeds(b, null);

        raised.Should().Be(1, "only the first report moved the widest; the rest left it at 120");
    }

    [Fact]
    public void ReportNeeds_BadValues_CountAsNothing()
    {
        var layout = Layout();
        layout.ReportNeeds(Row, [double.NaN, double.PositiveInfinity, -5, 10.2]);
        layout.Needs.Should().Equal([0, 0, 0, 11, 0], "fractions round up and a short list leaves the rest at 0");
    }

    [Fact]
    public void Adopt_ShownWider_DragStartsFromIt()
    {
        var layout = Layout();
        layout.ReportNeeds(Row, [0, 170, 0, 50, 0]);
        var shown = layout.Fit(2000);
        var raised = false;
        layout.Changed += () => raised = true;

        layout.Adopt(shown, 1);
        raised.Should().BeFalse("the table looks the same");
        layout.MoveEdge(1, 10);

        layout.Widths[1].Should().Be(180, "the edge moves from where the operator sees it");
        layout.Widths[2].Should().Be(70);
    }

    [Fact]
    public void Adopt_ShownNarrower_KeepsStoredWidth()
    {
        var layout = Layout();
        layout.Adopt([30, 60, 80, double.NaN, 60], 1);
        layout.Adopt([30, 60, 80, double.NaN, 60], 3);
        layout.Adopt([30, 60], 4);
        layout.Widths.Should().Equal(30, 100, 80, double.NaN, 60);
    }

    [Theory]
    [InlineData(190, 160)]
    [InlineData(150, 150)]
    [InlineData(170, 160)]
    [InlineData(300, 224)]
    [InlineData(200, 160)]
    public void UpgradeSaved_CallsFromBeforeDistanceColumn_InsertsDistance(double where, double expected)
    {
        var upgraded = OperateView.UpgradeSaved("Calls", [36, 100, where, 160, 100, 46, 64, 230, 120, 74]);
        upgraded.Should().Equal(36, 100, expected, OperateView.DistanceWidth, 160, 100, 46, 64, 230, 120, 74);
    }

    [Fact]
    public void UpgradeSaved_WatchingFromBeforeDistanceColumn_InsertsDistance()
    {
        OperateView.UpgradeSaved("Watching", [100, 180, 160, 64]).Should().Equal(100, 150, OperateView.DistanceWidth, 160, 64);
    }

    [Fact]
    public void UpgradeSaved_CurrentOrUnknown_Unchanged()
    {
        double[] current = [36, 100, 160, 76, 160, 100, 46, 64, 230, 120, 74];
        OperateView.UpgradeSaved("Calls", current).Should().Equal(current);
        OperateView.UpgradeSaved("Watching", [100, 150, 76, 160, 64]).Should().Equal(100, 150, 76, 160, 64);
        OperateView.UpgradeSaved("Other", [1, 2, 3, 4]).Should().Equal(1, 2, 3, 4);
    }
}
