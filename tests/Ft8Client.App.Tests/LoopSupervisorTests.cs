// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.App.Services;

namespace Ft8Client.App.Tests;

/// <summary>Background loops restart after an unexpected error (evaluation B9).</summary>
public class LoopSupervisorTests
{
    [Fact]
    public async Task RunAsync_LoopThrows_RestartsWithGrowingBackoff()
    {
        var runs = 0;
        var waits = new List<TimeSpan>();
        var faults = new List<string>();

        await LoopSupervisor.RunAsync("Test", () =>
        {
            runs++;
            if (runs < 4) throw new IOException("settings.json is locked");
            return Task.CompletedTask;
        }, CancellationToken.None, (name, _) => faults.Add(name), (wait, _) => { waits.Add(wait); return Task.CompletedTask; });

        runs.Should().Be(4);
        faults.Should().Equal("Test", "Test", "Test");
        waits.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task RunAsync_Cancelled_EndsWithoutFault()
    {
        using var cts = new CancellationTokenSource();
        var faults = 0;
        var loop = LoopSupervisor.RunAsync("Test", async () => await Task.Delay(Timeout.Infinite, cts.Token), cts.Token, (_, _) => faults++);

        await cts.CancelAsync();
        await loop;

        faults.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_ManyFaults_BackoffCapsAtOneMinute()
    {
        var runs = 0;
        var waits = new List<TimeSpan>();
        await LoopSupervisor.RunAsync("Test", () => ++runs < 12 ? throw new InvalidOperationException() : Task.CompletedTask,
            CancellationToken.None, delay: (wait, _) => { waits.Add(wait); return Task.CompletedTask; });

        waits.Max().Should().Be(LoopSupervisor.MaxBackoff);
    }
}
