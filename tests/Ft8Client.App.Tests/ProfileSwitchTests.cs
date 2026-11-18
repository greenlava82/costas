// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.Core.Contacts;

namespace Ft8Client.App.Tests;

public class ProfileSwitchTests
{
    private static readonly SessionSnapshot Idle = new() { MyCall = "K1ABC" };

    private static ContactView Contact(ContactOutcome outcome) =>
        new("JA1AAA", "PM95", ContactFlow.AnswerCq, outcome, null, null, [], false, false, string.Empty);

    [Fact]
    public void Refusal_Idle_Null() => ProfileSwitch.Refusal(Idle).Should().BeNull();

    [Theory]
    [InlineData(ContactOutcome.Logged)]
    [InlineData(ContactOutcome.NoReply)]
    [InlineData(ContactOutcome.Abandoned)]
    [InlineData(ContactOutcome.Watchdog)]
    public void Refusal_ContactFinished_Null(ContactOutcome outcome) =>
        ProfileSwitch.Refusal(Idle with { Contact = Contact(outcome) }).Should().BeNull();

    [Fact]
    public void Refusal_ContactInProgress_NamesTheStationAndCallsign() =>
        ProfileSwitch.Refusal(Idle with { Contact = Contact(ContactOutcome.InProgress) })
            .Should().Be("Nothing was saved: the station profile cannot change during the contact with JA1AAA as K1ABC. Try again when the station is idle.");

    [Fact]
    public void Refusal_Transmitting_Refused() =>
        ProfileSwitch.Refusal(Idle with { Transmitting = true }).Should().Contain("while transmitting as K1ABC");

    [Fact]
    public void Refusal_KeyboardTransmission_Refused() =>
        ProfileSwitch.Refusal(Idle with { KeyboardTx = new KeyboardTxView(true, "CQ", "", false) }).Should().Contain("while transmitting");

    [Fact]
    public void Refusal_CallingCq_Refused() =>
        ProfileSwitch.Refusal(Idle with { CallingCq = true }).Should().Contain("while calling CQ");

    [Fact]
    public void Refusal_AutoCall_Refused() =>
        ProfileSwitch.Refusal(Idle with { AutoCall = true }).Should().Contain("while Auto-call is on");

    [Fact]
    public void Refusal_CallsignEdit_SaysYourCallsign() =>
        ProfileSwitch.Refusal(Idle with { CallingCq = true }, "your callsign")
            .Should().Be("Nothing was saved: your callsign cannot change while calling CQ as K1ABC. Try again when the station is idle.");
}
