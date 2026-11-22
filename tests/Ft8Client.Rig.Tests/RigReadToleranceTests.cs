// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Processes;
using Ft8Client.Core.Time;
using Ft8Client.Core.Transmit;
using Ft8Client.Rig;
using Ft8Client.Tests.Shared;

namespace Ft8Client.Rig.Tests;

/// <summary>
/// A CAT link that RF disturbs at high power (IC-7300 at 100 W): odd status reads fail while rigctld and the radio are
/// fine. Those must not stop a transmission or kill rigctld; a run of them, or rigctld exiting, still does.
/// </summary>
public sealed class RigReadToleranceTests
{
    private sealed class FakeRigctld : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("costas-flaky-").FullName;

        public FakeRigctld()
        {
            Log = Path.Combine(_dir, "log");
            File.WriteAllText(Log, string.Empty);
            Exe = Path.Combine(_dir, "rigctld");
            ExecutableScript.Copy(Path.Combine(AppContext.BaseDirectory, "Emulators", "flaky_rigctld.py"), Exe);
        }

        public string Exe { get; }

        public string Log { get; }

        public string[] Lines() => File.ReadAllLines(Log);

        public RigctldOptions Options(string settings) => new(Exe, 3073, "/dev/ttyCostasFake", 19200, Config: $"log={Log},{settings}");

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task GetFrequency_TwoBadReadsWhileRigctldRuns_NoFaultNoRestartAndPttStaysOn()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in rigctld is a Python script run directly.");
        using var fake = new FakeRigctld();
        var ct = TestContext.Current.CancellationToken;
        await using var rig = await RigctldRig.StartAsync(fake.Options("badreads=2"), NullProcessGuard.Instance, SystemClock.Instance, ct);
        var faults = new List<string>();
        var quiet = new List<string>();
        rig.Faulted += (_, f) => { lock (faults) faults.Add(f.Message); };
        rig.ReadFailed += (_, why) => { lock (quiet) quiet.Add(why); };

        await rig.SetPttAsync(true, ct);
        await rig.Invoking(r => r.GetFrequencyAsync(ct)).Should().ThrowAsync<RigException>();
        await rig.Invoking(r => r.GetFrequencyAsync(ct)).Should().ThrowAsync<RigException>();
        (await rig.GetFrequencyAsync(ct)).Should().Be(14_074_000);

        lock (faults) faults.Should().BeEmpty("a transmission stops on a fault, and two odd reads are not a lost radio");
        lock (quiet) quiet.Should().HaveCount(2);
        rig.Restarts.Should().Be(0);
        fake.Lines().Count(l => l == "start").Should().Be(1);
        fake.Lines().Should().NotContain("T 0", "nothing unkeyed the radio");
        await rig.SetPttAsync(false, ct);
    }

    [Fact]
    public async Task GetFrequency_ReplyTakesAsLongAsHamlibRetrying_ReadsItWithoutFault()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in rigctld is a Python script run directly.");
        using var fake = new FakeRigctld();
        var ct = TestContext.Current.CancellationToken;
        await using var rig = await RigctldRig.StartAsync(fake.Options("slowreads=1"), NullProcessGuard.Instance, SystemClock.Instance, ct);
        var faults = new List<string>();
        var quiet = new List<string>();
        rig.Faulted += (_, f) => { lock (faults) faults.Add(f.Message); };
        rig.ReadFailed += (_, why) => { lock (quiet) quiet.Add(why); };

        await rig.SetPttAsync(true, ct);
        (await rig.GetFrequencyAsync(ct)).Should().Be(14_074_000, "a 3 s reply is Hamlib recovering a garbled frame, not a lost radio");

        lock (faults) faults.Should().BeEmpty();
        lock (quiet) quiet.Should().BeEmpty();
        await rig.SetPttAsync(false, ct);
    }

    [Fact]
    public async Task GetFrequency_BadReadsPastTheLimit_FaultsAndRestartsRigctld()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in rigctld is a Python script run directly.");
        using var fake = new FakeRigctld();
        var ct = TestContext.Current.CancellationToken;
        await using var rig = await RigctldRig.StartAsync(fake.Options($"badreads={RigctldRig.ReadFailuresTolerated + 1}"), NullProcessGuard.Instance,
            SystemClock.Instance, ct);
        var faults = new List<string>();
        rig.Faulted += (_, f) => { lock (faults) faults.Add(f.Message); };

        for (var i = 0; i < RigctldRig.ReadFailuresTolerated; i++)
            await rig.Invoking(r => r.GetFrequencyAsync(ct)).Should().ThrowAsync<RigException>();
        lock (faults) faults.Should().BeEmpty();
        (await rig.Invoking(r => r.GetFrequencyAsync(ct)).Should().ThrowAsync<RigException>()).Which.Message.Should().Contain("reads in a row");

        lock (faults) faults.Should().ContainSingle().Which.Should().Contain("reads in a row");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (fake.Lines().Count(l => l == "start") < 2 && DateTime.UtcNow < deadline) await Task.Delay(50, ct);
        fake.Lines().Count(l => l == "start").Should().Be(2, "a radio that keeps not answering is restarted");
    }

    [Fact]
    public async Task GetFrequency_FailureAfterASuccess_CountsFromOneAgain()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in rigctld is a Python script run directly.");
        using var fake = new FakeRigctld();
        var ct = TestContext.Current.CancellationToken;
        await using var rig = await RigctldRig.StartAsync(fake.Options("badreads=1"), NullProcessGuard.Instance, SystemClock.Instance, ct);
        var faults = new List<string>();
        rig.Faulted += (_, f) => { lock (faults) faults.Add(f.Message); };

        await rig.Invoking(r => r.GetFrequencyAsync(ct)).Should().ThrowAsync<RigException>();
        await rig.SetFrequencyAsync(14_074_000, ct); // a command that worked
        (await rig.GetStateAsync(ct)).FrequencyHz.Should().Be(14_074_000);

        lock (faults) faults.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadTxMeters_MeterReadTimesOut_PausesMetersWithoutFault()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in rigctld is a Python script run directly.");
        using var fake = new FakeRigctld();
        var ct = TestContext.Current.CancellationToken;
        var clock = new ManualClock(new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc));
        await using var rig = await RigctldRig.StartAsync(fake.Options("slowalc=1"), NullProcessGuard.Instance, clock, ct);
        var faults = new List<string>();
        rig.Faulted += (_, f) => { lock (faults) faults.Add(f.Message); };

        (await rig.ReadTxMetersAsync(ct)).Should().Be(TxMeterReading.None, "the ALC read did not answer in time");
        (await rig.ReadTxMetersAsync(ct)).Should().Be(TxMeterReading.None);
        (await rig.ReadPowerWattsAsync(ct)).Should().BeNull();
        fake.Lines().Count(l => l.StartsWith("l ", StringComparison.Ordinal)).Should().Be(1, "meters pause after a failed read");

        clock.Advance(RigctldRig.MeterPause + TimeSpan.FromSeconds(1));
        await rig.ReadTxMetersAsync(ct);
        fake.Lines().Count(l => l == "l ALC").Should().Be(2, "meters are read again after the pause");
        lock (faults) faults.Should().BeEmpty();
    }
}
