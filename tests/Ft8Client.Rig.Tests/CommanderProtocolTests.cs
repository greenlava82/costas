// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Globalization;
using Ft8Client.Rig.DxLab;

namespace Ft8Client.Rig.Tests;

public sealed class CommanderProtocolTests
{
    [Fact]
    public void Command_NoParameters_MatchesWsjtxFraming() =>
        CommanderProtocol.Command("CmdGetFreq").Should().Be("<command:10>CmdGetFreq<parameters:0>");

    [Fact]
    public void Command_WithParameters_CountsParameterLength() =>
        CommanderProtocol.Command("CmdSplit", ("1", "off")).Should().Be("<command:8>CmdSplit<parameters:8><1:3>off");

    // Built by hand: test machines may run without culture data (globalization-invariant mode).
    private static CultureInfo Numbers(string group, string point)
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        c.NumberFormat.NumberGroupSeparator = group;
        c.NumberFormat.NumberDecimalSeparator = point;
        return c;
    }

    [Theory]
    [InlineData(",", ".", 14_074_000L, "14,074.000")]
    [InlineData(".", ",", 14_074_000L, "14.074,000")]
    [InlineData(",", ".", 7_074_500L, "7,074.500")]
    public void FormatFrequency_Culture_KilohertzThreeDecimals(string group, string point, long hz, string expected) =>
        CommanderProtocol.FormatFrequency(hz, Numbers(group, point)).Should().Be(expected);

    [Theory]
    [InlineData("14,074.000", 14_074_000L)]
    [InlineData("14.074,000", 14_074_000L)]
    [InlineData("14 074,000", 14_074_000L)]
    [InlineData("14074.5", 14_074_500L)]
    [InlineData("14074.52", 14_074_520L)]
    [InlineData("14074", 14_074_000L)]
    public void ParseFrequency_Separators_Hertz(string text, long expected) =>
        CommanderProtocol.ParseFrequency(text).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0.000")]
    public void ParseFrequency_NotAFrequency_Null(string? text) =>
        CommanderProtocol.ParseFrequency(text).Should().BeNull();

    [Fact]
    public void ReadReply_Complete_NameValueAndLength()
    {
        var reply = CommanderProtocol.ReadReply("<CmdFreq:10>14,074.000<CmdTX:2>ON", out var used);
        reply.Should().Be(("CmdFreq", "14,074.000"));
        used.Should().Be("<CmdFreq:10>14,074.000".Length);
    }

    [Fact]
    public void ReadReply_StillArriving_Null() =>
        CommanderProtocol.ReadReply("<CmdFreq:10>14,07", out _).Should().BeNull();

    [Theory]
    [InlineData("DATA-U", "PKTUSB")]
    [InlineData("PKT-R", "PKTUSB")]
    [InlineData("DIGL", "PKTLSB")]
    [InlineData("usb", "USB")]
    [InlineData("RTTY-R", "RTTYR")]
    public void HamlibMode_CommanderName_HamlibName(string commander, string hamlib) =>
        CommanderProtocol.HamlibMode(commander).Should().Be(hamlib);

    [Theory]
    [InlineData("ON", true)]
    [InlineData("off", false)]
    [InlineData("?", null)]
    public void OnOff_Reply_State(string value, bool? expected) =>
        CommanderProtocol.OnOff(value).Should().Be(expected);

    [Fact]
    public void ModeName_DataMode_DataU() => CommanderProtocol.ModeName(RigMode.PktUsb).Should().Be("DATA-U");
}
