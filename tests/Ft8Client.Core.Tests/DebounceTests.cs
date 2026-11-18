// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Time;

namespace Ft8Client.Core.Tests;

/// <summary>When the ADIF log copy is rewritten after changes (C8).</summary>
public class DebounceTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);

    private static Debounce New() => new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(8));

    [Fact]
    public void DueUtc_NoRequest_Null()
    {
        var d = New();

        d.DueUtc.Should().BeNull();
        d.Pending.Should().BeFalse();
        d.IsDue(T0.AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void IsDue_OneRequest_DueAfterQuiet()
    {
        var d = New();
        d.Request(T0);

        d.IsDue(T0.AddSeconds(4.9)).Should().BeFalse();
        d.IsDue(T0.AddSeconds(5)).Should().BeTrue();
    }

    [Fact]
    public void DueUtc_BurstOfRequests_WaitsForQuietAfterTheLast()
    {
        var d = New();
        for (var i = 0; i < 10; i++) d.Request(T0.AddSeconds(i));

        d.DueUtc.Should().Be(T0.AddSeconds(9 + 5), "ten changes a second apart are one write, 5 s after the last");
    }

    [Fact]
    public void DueUtc_ChangesNeverStop_DueAtMaxWaitAfterTheFirst()
    {
        var d = New();
        for (var i = 0; i < 120; i++) d.Request(T0.AddSeconds(i));

        d.DueUtc.Should().Be(T0.AddSeconds(60), "a long sync still gets written within a minute");
    }

    [Fact]
    public void Begin_RequestDuringWrite_StartsNewBurst()
    {
        var d = New();
        d.Request(T0);
        d.Begin();
        d.Pending.Should().BeFalse();

        d.Request(T0.AddSeconds(30));

        d.DueUtc.Should().Be(T0.AddSeconds(35));
    }

    [Fact]
    public void Failed_Repeatedly_RetryDoublesUpToMax()
    {
        var d = New();
        var now = T0;
        var waits = new List<TimeSpan>();
        for (var i = 0; i < 6; i++)
        {
            d.Begin();
            d.Failed(now);
            waits.Add(d.DueUtc!.Value - now);
            now = d.DueUtc.Value;
        }

        waits.Select(w => w.TotalMinutes).Should().Equal(1, 2, 4, 8, 8, 8);
    }

    [Fact]
    public void Succeeded_AfterFailures_RetryStartsAgainAtFirstWait()
    {
        var d = New();
        d.Failed(T0);
        d.Begin();
        d.Failed(T0);
        d.Begin();
        d.Succeeded();

        d.Failed(T0);

        d.DueUtc.Should().Be(T0.AddMinutes(1));
    }

    [Fact]
    public void DueUtc_ChangeWhileWaitingToRetry_DueAtTheSooner()
    {
        var d = New();
        d.Failed(T0);
        d.Request(T0.AddSeconds(10));

        d.DueUtc.Should().Be(T0.AddSeconds(15), "a new contact is tried after the quiet period, not after the retry wait");
    }
}
