// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using Ft8Client.App.Services;

namespace Ft8Client.App.Tests;

public class UsageThrottleTests
{
    [Fact]
    public void Allow_SameKeyInsideWindow_OnlyFirstPasses()
    {
        var t = new UsageThrottle(TimeSpan.FromHours(1));
        var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        t.Allow("tx:clock", now).Should().BeTrue();
        t.Allow("tx:clock", now.AddMinutes(30)).Should().BeFalse();
        t.Allow("tx:band_edge", now.AddMinutes(30)).Should().BeTrue();
        t.Allow("tx:clock", now.AddMinutes(61)).Should().BeTrue();
    }
}
