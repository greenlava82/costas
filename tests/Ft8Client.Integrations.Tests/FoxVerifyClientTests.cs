// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using FluentAssertions;
using Ft8Client.Core.Stations;
using Ft8Client.Integrations.Net;
using Ft8Client.Integrations.SuperFox;
using Xunit;

namespace Ft8Client.Integrations.Tests;

/// <summary>Checking SuperFox signatures with WSJT-X's service (format confirmed against 9dx.cc on 2026-11-15).</summary>
public class FoxVerifyClientTests
{
    private static readonly DateTime Slot = new(2026, 11, 15, 20, 36, 30, DateTimeKind.Utc);

    private sealed class Service(string body, HttpStatusCode status = HttpStatusCode.OK, bool fail = false) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (fail) throw new HttpRequestException("offline");
            Asked.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static (FoxVerifyClient Client, Service Service) Build(string body, HttpStatusCode status = HttpStatusCode.OK, bool fail = false)
    {
        var service = new Service(body, status, fail);
        return (new FoxVerifyClient(HttpFactory.Create("W7LIT", service)), service);
    }

    [Theory]
    [InlineData("2026-11-15T20:36:30Z K8R 920749 VERIFIED", FoxCheck.Verified)]
    [InlineData("2026-11-15T20:36:30Z K8R 920749 INVALID\n", FoxCheck.Invalid)]
    [InlineData("<html>busy</html>", FoxCheck.None)]
    public async Task CheckAsync_Answer_ReadsResult(string body, FoxCheck expected)
    {
        var (client, service) = Build(body);

        var check = await client.CheckAsync(new SuperFoxSignature("K8R", "920749"), Slot, TestContext.Current.CancellationToken);

        check.Should().Be(expected);
        service.Asked.Should().ContainSingle().Which.ToString().Should().Be("https://www.9dx.cc/check/K8R/2026-11-15T20:36:30Z/920749.text");
    }

    [Fact]
    public void UrlFor_PortableCall_EscapesSlash()
    {
        var (client, _) = Build(string.Empty);
        client.UrlFor(new SuperFoxSignature("E51D/MM", "123456"), Slot).AbsoluteUri.Should().Contain("/check/E51D%2FMM/");
    }

    [Fact]
    public async Task CheckAsync_Offline_None()
    {
        var (client, _) = Build(string.Empty, fail: true);
        (await client.CheckAsync(new SuperFoxSignature("K8R", "920749"), Slot, TestContext.Current.CancellationToken)).Should().Be(FoxCheck.None);
    }

    [Fact]
    public async Task CheckAsync_NoCode_NotSent()
    {
        var (client, service) = Build("x VERIFIED");
        (await client.CheckAsync(new SuperFoxSignature("K1JT", SuperFoxSignature.NoCode), Slot, TestContext.Current.CancellationToken)).Should().Be(FoxCheck.None);
        service.Asked.Should().BeEmpty();
    }
}
