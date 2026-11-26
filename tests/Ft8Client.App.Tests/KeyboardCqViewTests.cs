// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Reflection;
using Ft8Client.App.Engine;
using Ft8Client.App.Remote;
using Ft8Client.App.ViewModels;
using Ft8Client.Core;

namespace Ft8Client.App.Tests;

// PSK and RTTY: a CQ is one over. The button says Stop CQ only while it is going out; afterwards the card says the CQ
// went and the radio is listening, and the button offers another CQ.
public class KeyboardCqViewTests
{
    public class NoCommands : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }

    private static readonly DateTime Now = new(2026, 11, 26, 18, 0, 0, DateTimeKind.Utc);

    private static OperateViewModel Vm(SessionSnapshot s)
    {
        var vm = new OperateViewModel(DispatchProxy.Create<IOperateCommands, NoCommands>(), () => Now);
        vm.Apply(s, [], 0);
        return vm;
    }

    [Fact]
    public void Apply_PskCqGoingOut_ButtonSaysStopCq()
    {
        var vm = Vm(new SessionSnapshot { Mode = Mode.Psk31, CallingCq = true, Transmitting = true });

        vm.CqText.Should().Be("Stop CQ");
        vm.Panel.Caption.Should().Be("Calling CQ");
    }

    [Fact]
    public void Apply_PskCqSentAndListening_ButtonSaysCallCqAndCardSaysListening()
    {
        var vm = Vm(new SessionSnapshot { Mode = Mode.Psk31, CqListening = true });

        vm.CqText.Should().Be("Call CQ");
        vm.Panel.Should().BeSameAs(ContactPanelViewModel.CqListening);
        vm.Panel.Caption.Should().Be("CQ sent");
        vm.Panel.Subline.Should().Contain("Back on receive");
        vm.Panel.IsIdle.Should().BeTrue();
    }

    [Fact]
    public void Apply_RttyNothingOut_IdleCard()
    {
        var vm = Vm(new SessionSnapshot { Mode = Mode.Rtty });

        vm.CqText.Should().Be("Call CQ");
        vm.Panel.Caption.Should().Be("Not in a contact");
    }

    [Fact]
    public void Build_PskCqListening_PhoneGetsIt()
    {
        var s = RemoteStateBuilder.Build(new SessionSnapshot { MyCall = "W7LIT", Band = "20m", Mode = Mode.Psk31, CqListening = true }, Now, new RemoteContext(), "you", null);

        s.CqListening.Should().BeTrue();
        s.CallingCq.Should().BeFalse("the phone's button says Call CQ again");
    }
}
