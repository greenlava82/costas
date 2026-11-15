// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Contacts;

namespace Ft8Client.Core.Tests;

public class OutgoingMessagesTests
{
    private static readonly ContactSettings Defaults = new();

    [Fact]
    public void Defaults_MatchTheStandardMessages()
    {
        OutgoingMessages.Cq("W7LIT", "DN40", Defaults).Should().Be("CQ W7LIT DN40");
        OutgoingMessages.Reply("ZL2RPA", "W7LIT", "DN40", Defaults).Should().Be("ZL2RPA W7LIT DN40");
        OutgoingMessages.Final("ZL2RPA", "W7LIT", Defaults).Should().Be("ZL2RPA W7LIT 73");
        OutgoingMessages.FreeTextAfter(Defaults).Should().BeNull();
        OutgoingMessages.Problem(Defaults, "W7LIT", "DN40").Should().BeNull();
    }

    [Fact]
    public void Cq_Modifier_AddedAfterCq()
    {
        var s = new ContactSettings { CqModifier = " pota " };
        OutgoingMessages.Cq("W7LIT", "DN40", s).Should().Be("CQ POTA W7LIT DN40");
        OutgoingMessages.Problem(s, "W7LIT", "DN40").Should().BeNull();
    }

    [Fact]
    public void Cq_NoGridConfigured_LeavesItOut()
    {
        OutgoingMessages.Cq("W7LIT", string.Empty, Defaults).Should().Be("CQ W7LIT");
    }

    [Fact]
    public void Reply_GridOff_SendsBothCallsOnly()
    {
        OutgoingMessages.Reply("ZL2RPA", "W7LIT", "DN40", new ContactSettings { GridInReply = false }).Should().Be("ZL2RPA W7LIT");
    }

    [Fact]
    public void Final_Rr73_SendsRr73()
    {
        OutgoingMessages.Final("ZL2RPA", "W7LIT", new ContactSettings { AnswerSignoff = "RR73" }).Should().Be("ZL2RPA W7LIT RR73");
    }

    [Fact]
    public void FreeTextAfter_Tidied_UpperCaseSingleSpaced()
    {
        var s = new ContactSettings { FreeTextAfter = " tnx  73 gl " };
        OutgoingMessages.FreeTextAfter(s).Should().Be("TNX 73 GL");
        OutgoingMessages.Problem(s, "W7LIT", "DN40").Should().BeNull();
    }

    [Theory]
    [InlineData("DXCCX")]
    [InlineData("1234")]
    [InlineData("D1")]
    public void Problem_BadModifier_Refused(string mod)
    {
        OutgoingMessages.Problem(new ContactSettings { CqModifier = mod }, "W7LIT", "DN40").Should().Contain("modifier");
    }

    [Fact]
    public void Problem_ModifierWithNonstandardCall_Refused()
    {
        OutgoingMessages.Problem(new ContactSettings { CqModifier = "DX" }, "PJ4/K1ABC", "FN42").Should().Contain("no room for a CQ modifier");
    }

    [Fact]
    public void Problem_CompoundCallWithDefaults_NoProblem()
    {
        OutgoingMessages.Problem(new ContactSettings(), "VE3/W7LIT", "DN40").Should().BeNull();
        OutgoingMessages.Problem(new ContactSettings { AnswerSignoff = "RR73" }, "W7LIT/P", "DN40").Should().BeNull();
    }

    [Fact]
    public void CompoundOwnCall_FollowsWsjtxForms()
    {
        var s = new ContactSettings();
        OutgoingMessages.Cq("VE3/W7LIT", "DN40", s).Should().Be("CQ VE3/W7LIT");
        OutgoingMessages.Reply("K1ABC", "VE3/W7LIT", "DN40", s).Should().Be("<K1ABC> VE3/W7LIT");
        OutgoingMessages.Report("K1ABC", "VE3/W7LIT", "R-10").Should().Be("K1ABC <VE3/W7LIT> R-10");
        OutgoingMessages.Signoff("K1ABC", "VE3/W7LIT", "RR73").Should().Be("<K1ABC> VE3/W7LIT RR73");
        OutgoingMessages.FromDx("K1ABC", "VE3/W7LIT", "-05").Should().Be("<VE3/W7LIT> K1ABC -05");
    }

    [Fact]
    public void PortableAndRoverCalls_AreStandard()
    {
        OutgoingMessages.IsCompound("W7LIT/P").Should().BeFalse();
        OutgoingMessages.IsCompound("W7LIT/R").Should().BeFalse();
        OutgoingMessages.IsCompound("VE3/W7LIT").Should().BeTrue();
        OutgoingMessages.IsCompound("W7LIT/7").Should().BeTrue();
        OutgoingMessages.Cq("W7LIT/P", "DN40", new ContactSettings()).Should().Be("CQ W7LIT/P DN40");
    }

    [Theory]
    [InlineData("THANKS FOR QSO 73")]
    [InlineData("CQ W7LIT")]
    [InlineData("HI@HOME")]
    public void Problem_UnsendableFreeText_Refused(string text)
    {
        OutgoingMessages.Problem(new ContactSettings { FreeTextAfter = text }, "W7LIT", "DN40").Should().NotBeNull();
    }
}
