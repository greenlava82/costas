// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Services;
using Ft8Client.Core.Services;

namespace Ft8Client.App.Tests;

public sealed class CombinedServiceStatusTests
{
    private static readonly DateTime T = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    private static ServiceStatus S(ServiceHealth h, string m) => new(h, m, T);

    [Fact]
    public void Set_NothingFailing_ShowsLatest()
    {
        var c = new CombinedServiceStatus();
        c.Set("sync", S(ServiceHealth.Ok, "Synced 3 records"));

        c.Set("upload", S(ServiceHealth.Ok, "Uploading to QRZ")).Message.Should().Be("Uploading to QRZ");
    }

    [Fact]
    public void Set_OkWhileAnotherPartFails_KeepsShowingTheFailure()
    {
        var c = new CombinedServiceStatus();
        c.Set("lookup", S(ServiceHealth.Down, "Login refused"));

        c.Set("upload", S(ServiceHealth.Ok, "Uploading to QRZ")).Message.Should().Be("Login refused");
    }

    [Fact]
    public void Set_DownAndDegraded_ShowsDown()
    {
        var c = new CombinedServiceStatus();
        c.Set("lookup", S(ServiceHealth.Down, "Login refused"));

        c.Set("upload", S(ServiceHealth.Degraded, "retrying")).Health.Should().Be(ServiceHealth.Down);
    }

    [Fact]
    public void Set_FailingPartRecovers_ShowsItsOk()
    {
        var c = new CombinedServiceStatus();
        c.Set("sync", S(ServiceHealth.Ok, "Synced 3 records"));
        c.Set("upload", S(ServiceHealth.Degraded, "retrying"));

        c.Set("upload", S(ServiceHealth.Ok, "Uploading to QRZ")).Should().Be(S(ServiceHealth.Ok, "Uploading to QRZ"));
    }

    [Fact]
    public void Clear_FailingPart_FallsBackToTheOthers()
    {
        var c = new CombinedServiceStatus();
        c.Set("sync", S(ServiceHealth.Ok, "Synced 3 records"));
        c.Set("upload", S(ServiceHealth.Degraded, "retrying"));

        c.Clear("upload")!.Message.Should().Be("Synced 3 records");
        c.Clear("upload").Should().BeNull("nothing changed");
    }
}
