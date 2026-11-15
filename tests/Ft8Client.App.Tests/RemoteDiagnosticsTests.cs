// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using FluentAssertions;
using Ft8Client.App.Remote;
using Xunit;

namespace Ft8Client.App.Tests;

public class RemoteDiagnosticsTests
{
    private static RemoteCheckFacts Facts(params FirewallRule[] rules) => new()
    {
        Enabled = true, Port = 8073, Urls = ["http://192.168.1.20:8073/"], AnsweredHere = ["http://192.168.1.20:8073/"],
        NetworkCategories = ["Private"], FirewallRules = rules,
    };

    [Fact]
    public void Explain_Off_SaysToTurnItOn() =>
        RemoteDiagnostics.Explain(new RemoteCheckFacts { Enabled = false }).Should().ContainSingle().Which.Should().Contain("is off");

    [Fact]
    public void Explain_AllowedOnPrivate_ListeningAndAllowed()
    {
        var lines = RemoteDiagnostics.Explain(Facts(new FirewallRule("Costas", true, "Private")));

        lines[0].Should().StartWith("✓ Costas is listening");
        lines.Should().Contain("✓ Windows Firewall lets Costas in on Private networks.");
        lines.Should().NotContain(l => l.StartsWith('✗'));
    }

    [Fact]
    public void Explain_PublicNetworkAllowedOnPrivateOnly_SaysPublicAndNoRule()
    {
        var lines = RemoteDiagnostics.Explain(Facts(new FirewallRule("Costas", true, "Private")) with { NetworkCategories = ["Public"] });

        lines.Should().Contain(l => l.Contains("as Public"));
        lines.Should().Contain(l => l.StartsWith("✗ No Windows Firewall rule lets Costas in on Public"));
    }

    [Fact]
    public void Explain_BlockRuleWinsOverAllow_NamesTheBlockRule()
    {
        var lines = RemoteDiagnostics.Explain(Facts(new FirewallRule("costas.exe", false, "Private, Public"), new FirewallRule("Costas", true, "Private")));

        lines.Should().Contain(l => l.StartsWith("✗ Windows Firewall blocks Costas on Private networks (rule \"costas.exe\")"));
    }

    [Fact]
    public void Explain_NoAnswerHere_SaysNotListening()
    {
        var lines = RemoteDiagnostics.Explain(Facts() with { AnsweredHere = [], FirewallRules = null, NetworkCategories = [] });

        lines[0].Should().StartWith("✗ Costas does not answer at http://192.168.1.20:8073/");
    }

    [Fact]
    public void ParseWindowsFacts_SingleRuleAsObject_ReadsIt()
    {
        var (cats, rules) = RemoteDiagnostics.ParseWindowsFacts("""{"Categories":"Public","Rules":{"Name":"Costas","Action":"Block","Profile":"Public"}}""");

        cats.Should().Equal("Public");
        rules.Should().Equal(new FirewallRule("Costas", false, "Public"));
    }

    [Fact]
    public void ParseWindowsFacts_NotJson_Unknown()
    {
        var (cats, rules) = RemoteDiagnostics.ParseWindowsFacts("oops");

        cats.Should().BeEmpty();
        rules.Should().BeNull();
    }

    [Theory]
    [InlineData("Any", "Public", true)]
    [InlineData("Domain, Private", "DomainAuthenticated", true)]
    [InlineData("Private", "Public", false)]
    public void Covers_ProfileAndCategory_Matches(string profiles, string category, bool covers) =>
        new FirewallRule("r", true, profiles).Covers(category).Should().Be(covers);
}
