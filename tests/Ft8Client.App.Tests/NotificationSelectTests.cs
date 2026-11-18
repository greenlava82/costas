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

/// <summary>A clicked desktop notification selects its station in Next in line, and never calls it.</summary>
public class NotificationSelectTests
{
    private static readonly DateTime Slot = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);

    private static RankedStation Cq(string call) =>
        new(new Station { Call = call, LastHeardUtc = Slot, State = StationState.CallingCq, LastSnr = -12 },
            new NeedResult(NeedTag.NewCall, 4), new HearsYou(HearsYouKind.None), Chance.Good, true, null);

    private static SessionSnapshot Snap(params string[] line) =>
        new() { Mode = Mode.Ft8, Band = "20m", LastDecodedSlot = Slot, Rank = new RankResult([.. line.Select(Cq)], [], [], []) };

    private static (OperateViewModel Vm, MiniViewModelTests.Recorder Commands) Make()
    {
        var commands = DispatchProxy.Create<IOperateCommands, MiniViewModelTests.Recorder>();
        var vm = new OperateViewModel(commands, () => Slot.AddSeconds(5));
        vm.Apply(Snap("K1ABC", "VP8PJ", "W2XYZ"), [], 0);
        return (vm, (MiniViewModelTests.Recorder)(object)commands);
    }

    [Fact]
    public void SelectCall_StationInLine_SelectsItWithoutCalling()
    {
        var (vm, commands) = Make();

        vm.SelectCall("vp8pj").Should().BeTrue();

        vm.Selected!.Call.Should().Be("VP8PJ");
        vm.Line.Where(r => r.IsSelected).Select(r => r.Call).Should().Equal("VP8PJ");
        commands.Calls.Should().BeEmpty();
    }

    [Fact]
    public void SelectCall_StationNotInLine_LeavesSelectionAlone()
    {
        var (vm, commands) = Make();
        vm.SelectCall("K1ABC");

        vm.SelectCall("F6DKQ").Should().BeFalse();

        vm.Selected!.Call.Should().Be("K1ABC");
        commands.Calls.Should().BeEmpty();
    }
}
