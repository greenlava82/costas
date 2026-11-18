// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Diagnostics;
using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.Core.Time;
using Ft8Client.Data.Adif;
using Ft8Client.Data.Log;

namespace Ft8Client.App.Tests;

/// <summary>Keeping the ADIF log copy current without holding up logging (C8).</summary>
public class LogCopyServiceTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(150);

    private static QsoRecord Contact(string call) => new()
    {
        Call = call, Band = "20m", Mode = "FT8", QsoDateOn = new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc),
        StationCallsign = "W7LIT", ProfileId = "home",
    };

    private static async Task Until(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition not met.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task RunAsync_BurstOfChanges_WritesOnceAfterQuiet()
    {
        var folder = Directory.CreateTempSubdirectory("ft8copy").FullName;
        var reads = 0;
        var log = new List<QsoRecord> { Contact("PA3A") };
        var copy = new LogCopyService(new AdifLogMirror(() => { Interlocked.Increment(ref reads); return log; }, folder), new SystemClock(), Quiet);
        using var cts = new CancellationTokenSource();
        var run = copy.RunAsync(cts.Token);
        await Until(() => copy.Attempts == 1);

        for (var i = 0; i < 20; i++) copy.Request();
        await Until(() => copy.Attempts == 2);
        await Task.Delay(Quiet * 3, TestContext.Current.CancellationToken);

        copy.Attempts.Should().Be(2, "the start-up write plus one for the whole burst");
        reads.Should().Be(2);
        copy.Status.Error.Should().BeNull();
        copy.Status.Files.Select(Path.GetFileName).Should().Equal("costas-log-W7LIT.adi");
        await cts.CancelAsync();
        await run.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Request_WhileWriteInProgress_ReturnsAtOnce()
    {
        var folder = Directory.CreateTempSubdirectory("ft8copy").FullName;
        using var writing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var copy = new LogCopyService(new AdifLogMirror(() =>
        {
            writing.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return [Contact("PA3A")];
        }, folder), new SystemClock(), Quiet);
        using var cts = new CancellationTokenSource();
        var run = copy.RunAsync(cts.Token);
        writing.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();

        var sw = Stopwatch.StartNew();
        copy.Request();
        sw.Stop();
        release.Set();

        sw.ElapsedMilliseconds.Should().BeLessThan(50, "logging and PTT are never held up by the copy");
        await Until(() => copy.Attempts == 2);
        await cts.CancelAsync();
        await run.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_FolderUnusable_ShowsErrorAndRetriesUntilItWorks()
    {
        var folder = Path.Combine(Directory.CreateTempSubdirectory("ft8copy").FullName, "backups");
        File.WriteAllText(folder, "a file where the folder should be");
        var statuses = new List<LogCopyStatus>();
        var copy = new LogCopyService(new AdifLogMirror(() => [Contact("PA3A")], folder), new SystemClock(), Quiet,
            retry: TimeSpan.FromMilliseconds(200));
        copy.StatusChanged += s => { lock (statuses) statuses.Add(s); };
        using var cts = new CancellationTokenSource();
        var run = copy.RunAsync(cts.Token);
        await Until(() => copy.Attempts >= 1);

        copy.Status.Error.Should().NotBeNull();
        copy.Status.NextTryUtc.Should().NotBeNull();
        LogCopyText.Written(copy.Status).Should().StartWith("Could not write the copy:");
        File.Delete(folder); // the folder comes back
        await Until(() => copy.Status.Error is null);

        copy.Status.LastWrittenUtc.Should().NotBeNull();
        File.Exists(Path.Combine(folder, "costas-log-W7LIT.adi")).Should().BeTrue();
        lock (statuses) statuses.First().Error.Should().NotBeNull();
        await cts.CancelAsync();
        await run.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Files_NoFileYet_SaysWhenItIsWritten()
    {
        var s = new LogCopyStatus("/backups", [], null, null, null);

        LogCopyText.Files(s).Should().Be("No file yet: it is written once the log has a contact.");
        LogCopyText.Written(s).Should().StartWith("Not written yet");
    }
}
