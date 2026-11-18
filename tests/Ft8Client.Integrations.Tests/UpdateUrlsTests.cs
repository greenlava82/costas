// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Integrations.Updates;

namespace Ft8Client.Integrations.Tests;

public class UpdateUrlsTests
{
    [Theory]
    [InlineData("https://github.com/somedudenamedtony/costas/releases/download/v0.1.1/Costas-Setup-0.1.1.exe")]
    [InlineData("HTTPS://objects.githubusercontent.com/x")]
    public void RequireHttps_HttpsUrl_ReturnsUri(string url)
    {
        UpdateUrls.RequireHttps(url).Host.Should().NotBeEmpty();
        UpdateUrls.IsHttps(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://github.com/x/Costas-Setup.exe")]
    [InlineData("ftp://example.invalid/Costas-Setup.exe")]
    [InlineData("file:///C:/Temp/Costas-Setup.exe")]
    [InlineData("/tmp/Costas-Setup.exe")]
    [InlineData("Costas-Setup.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void RequireHttps_NotHttps_Throws(string? url)
    {
        var act = () => UpdateUrls.RequireHttps(url);
        act.Should().Throw<InvalidDataException>();
        UpdateUrls.IsHttps(url).Should().BeFalse();
    }
}
