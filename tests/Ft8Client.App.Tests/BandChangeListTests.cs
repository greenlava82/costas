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
/// After a band change Next in line and Repeats show the new band at once, also while the pointer rests over a list
/// (which otherwise holds it still), so the old band's stations are never left on screen (owner request 2026-11-14).
/// </summary>
public class BandChangeListTests
{
    private static readonly DateTime Slot = new(2026, 11, 14, 14, 30, 0, DateTimeKind.Utc);

    private static RankedStation Cq(string call) =>
        new(new Station { Call = call, LastHeardUtc = Slot, State = StationState.CallingCq, LastSnr = -12 },
            new NeedResult(NeedTag.NewCall, 4), new HearsYou(HearsYouKind.None), Chance.Good, true, null);

    private static SessionSnapshot Snap(string band, string[] line, string[] repeats) =>
        new()
        {
            Mode = Mode.Ft8, Band = band, LastDecodedSlot = Slot,
            Rank = new RankResult([.. line.Select(Cq)], [], [.. repeats.Select(Cq)], []),
        };

    private readonly IOperateCommands _commands = DispatchProxy.Create<IOperateCommands, MiniViewModelTests.Recorder>();

    [Fact]
    public void Apply_BandChangedWithPointerOverLists_ClearsBothListsAtOnce()
    {
        var vm = new OperateViewModel(_commands, () => Slot.AddSeconds(5));
        vm.Apply(Snap("20m", ["K1ABC"], ["W2XYZ"]), [], 0);
        vm.PointerEnteredLine();
        vm.PointerEnteredRepeats();

        vm.Apply(Snap("40m", [], []), [], 0);

        vm.Line.Should().BeEmpty("the 20 m stations are not on 40 m");
        vm.Repeats.Should().BeEmpty();
        vm.ListUpdated.Should().BeFalse("nothing is left waiting behind the pill");
    }

    [Fact]
    public void Apply_SameBandNewSlotWithPointerOverList_StillHoldsTheList()
    {
        var vm = new OperateViewModel(_commands, () => Slot.AddSeconds(5));
        vm.Apply(Snap("20m", ["K1ABC"], []), [], 0);
        vm.PointerEnteredLine();

        vm.Apply(Snap("20m", ["N8WCR"], []) with { LastDecodedSlot = Slot.AddSeconds(15) }, [], 0);

        vm.Line.Select(r => r.Call).Should().Equal("K1ABC");
        vm.ListUpdated.Should().BeTrue();
    }
}
