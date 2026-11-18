// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Xml.Linq;
using Ft8Client.App.Services.Notifications;
using Ft8Client.Core.Notifications;

namespace Ft8Client.App.Tests;

public class ToastXmlTests
{
    [Fact]
    public void For_Notification_TitleTextSilentNoButtons()
    {
        var xml = XElement.Parse(ToastXml.For(new DesktopNotification(AttentionKind.Hunt, "VP8PJ on 20 m FT8", "New country, heard here at -12 dB.", "VP8PJ", true)));

        xml.Attribute("launch")!.Value.Should().Be("select=VP8PJ");
        xml.Descendants("text").Select(t => t.Value).Should().Equal("VP8PJ on 20 m FT8", "New country, heard here at -12 dB.");
        xml.Element("audio")!.Attribute("silent")!.Value.Should().Be("true");
        xml.Descendants("action").Should().BeEmpty();
        xml.Descendants("actions").Should().BeEmpty();
        xml.Attribute("scenario").Should().BeNull();
    }

    [Fact]
    public void For_MarkupInText_Escaped()
    {
        var xml = XElement.Parse(ToastXml.For(new DesktopNotification(AttentionKind.Caller, "K1ABC is calling you", "20 m FT8 · <TU> & 73", "K1ABC", true)));

        xml.Descendants("text").Last().Value.Should().Be("20 m FT8 · <TU> & 73");
    }

    [Theory]
    [InlineData("select=VP8PJ", "VP8PJ")]
    [InlineData("select=vp8pj ", "VP8PJ")]
    [InlineData("select=", null)]
    [InlineData("call=VP8PJ", null)]
    [InlineData("", null)]
    public void CallOf_LaunchString_TheStationOrNull(string arguments, string? expected) =>
        ToastXml.CallOf(arguments).Should().Be(expected);
}
