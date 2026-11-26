// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Wspr;
using Ft8Client.Tests.Shared;

namespace Ft8Client.Decoding.Tests;

public class WsprdDecoderTests
{
    private static readonly DateTime Slot = new(2015, 4, 26, 9, 18, 0, DateTimeKind.Utc);
    private const long Dial = 14_095_600;
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // A stand-in wsprd: prints the given lines and exits with the given code.
    private static string Script(string dir, string name, string[] lines, int exitCode = 0, bool hang = false)
    {
        string exe;
        if (OperatingSystem.IsWindows())
        {
            exe = Path.Combine(dir, name + ".cmd");
            var body = hang ? "@ping -n 60 127.0.0.1 > nul\r\n" : string.Concat(lines.Select(l => "@echo " + l.Replace("<", "^<").Replace(">", "^>") + "\r\n")) + $"@exit /b {exitCode}\r\n";
            File.WriteAllText(exe, body);
        }
        else
        {
            exe = Path.Combine(dir, name + ".sh");
            var body = hang ? "#!/bin/sh\nsleep 60\n" : "#!/bin/sh\n" + string.Concat(lines.Select(l => "echo '" + l + "'\n")) + $"exit {exitCode}\n";
            ExecutableScript.Write(exe, body);
        }
        return exe;
    }

    private static SlotAudio Audio() => new(Slot, Mode.Wspr, new short[1000]);

    [Fact]
    public async Task DecodeAsync_NormalOutput_ParsesSpotsAndSkipsGarbage()
    {
        var dir = Directory.CreateTempSubdirectory("ft8client-wsprd").FullName;
        var exe = Script(dir, "ok", ["0918  -9  1.1  14.097046  0  ND6P DM04 30", "0918 -21  0.5  14.097117  0  PJ4/K1ABC 37", "something odd", "<DecodeFinished>"]);
        var decoder = new WsprdDecoder(new WsprdOptions(exe, Path.Combine(dir, "tmp")));

        var result = await decoder.DecodeAsync(Audio(), Dial, Ct);

        result.Status.Should().Be(DecodeStatus.Ok);
        result.Spots.Select(s => s.Callsign).Should().Equal("ND6P", "PJ4/K1ABC");
        result.Spots[0].SlotStartUtc.Should().Be(Slot);
        result.Spots[0].DialHz.Should().Be(Dial);
        result.SkippedLines.Should().Be(1);
    }

    [Fact]
    public async Task DecodeAsync_ProcessExceedsTimeout_KillsAndReportsTimedOut()
    {
        var dir = Directory.CreateTempSubdirectory("ft8client-wsprd-hang").FullName;
        var exe = Script(dir, "hang", [], hang: true);
        var decoder = new WsprdDecoder(new WsprdOptions(exe, Path.Combine(dir, "tmp"), TimeSpan.FromMilliseconds(500)));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await decoder.DecodeAsync(Audio(), Dial, Ct);

        sw.Stop();
        result.Status.Should().Be(DecodeStatus.TimedOut);
        result.Message.Should().Contain("exceeded");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        Directory.EnumerateDirectories(Path.Combine(dir, "tmp")).Should().BeEmpty("the scratch folder is removed after a run");
    }

    [Fact]
    public async Task DecodeAsync_Cancelled_ReportsCancelled()
    {
        var dir = Directory.CreateTempSubdirectory("ft8client-wsprd-cancel").FullName;
        var exe = Script(dir, "hang", [], hang: true);
        var decoder = new WsprdDecoder(new WsprdOptions(exe, Path.Combine(dir, "tmp"), TimeSpan.FromSeconds(30)));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        var result = await decoder.DecodeAsync(Audio(), Dial, cts.Token);

        result.Status.Should().Be(DecodeStatus.Cancelled);
    }

    [Fact]
    public async Task DecodeAsync_NonZeroExit_ReportsFailedWithDetail()
    {
        var dir = Directory.CreateTempSubdirectory("ft8client-wsprd-fail").FullName;
        var exe = Script(dir, "fail", ["boom"], exitCode: 3);
        var decoder = new WsprdDecoder(new WsprdOptions(exe, Path.Combine(dir, "tmp")));

        var result = await decoder.DecodeAsync(Audio(), Dial, Ct);

        result.Status.Should().Be(DecodeStatus.Failed);
        result.Message.Should().Contain("code 3").And.Contain("boom");
    }

    [Fact]
    public async Task DecodeAsync_MissingExecutable_ReportsFailed()
    {
        var dir = Directory.CreateTempSubdirectory("ft8client-wsprd-missing").FullName;
        var decoder = new WsprdDecoder(new WsprdOptions(Path.Combine(dir, "nope"), dir));

        var result = await decoder.DecodeAsync(Audio(), Dial, Ct);

        result.Status.Should().Be(DecodeStatus.Failed);
        result.Message.Should().Contain("Could not start wsprd");
    }

    [Fact]
    public async Task DecodeAsync_FtSlot_Throws()
    {
        var dir = Directory.CreateTempSubdirectory("ft8client-wsprd-ft").FullName;
        var decoder = new WsprdDecoder(new WsprdOptions(Path.Combine(dir, "x"), dir));
        await FluentActions.Awaiting(() => decoder.DecodeAsync(new SlotAudio(Slot, Mode.Ft8, new short[10]), Dial, Ct))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task DecodeAsync_RunKeepsHashTable_NextRunStartsWithIt()
    {
        var dir = Directory.CreateTempSubdirectory("ft8client-wsprd-hash").FullName;
        string exe;
        if (OperatingSystem.IsWindows())
        {
            exe = Path.Combine(dir, "hash.cmd");
            File.WriteAllText(exe, "@echo heard>>hashtable.txt\r\n");
        }
        else
        {
            exe = Path.Combine(dir, "hash.sh");
            ExecutableScript.Write(exe, "#!/bin/sh\necho heard >> hashtable.txt\n");
        }
        var tmp = Path.Combine(dir, "tmp");
        var decoder = new WsprdDecoder(new WsprdOptions(exe, tmp));

        (await decoder.DecodeAsync(Audio(), Dial, Ct)).Status.Should().Be(DecodeStatus.Ok);
        (await decoder.DecodeAsync(Audio(), Dial, Ct)).Status.Should().Be(DecodeStatus.Ok);

        File.ReadAllLines(Path.Combine(tmp, "hashtable.txt")).Select(l => l.Trim()).Should().Equal("heard", "heard");
        Directory.EnumerateFiles(tmp, "keep-*").Should().BeEmpty();
    }

    [Fact]
    public void BuildArguments_Dial_PassesDataFolderAndMegahertz() =>
        string.Join(' ', WsprdDecoder.BuildArguments(14_095_600, "/w", "150426_0918.wav")).Should().Be("-a /w -f 14.095600 150426_0918.wav");

    [Fact]
    public void WavName_Slot_IsYyMmDdHhMm() => WsprdDecoder.WavName(Slot).Should().Be("150426_0918.wav");

    [Fact]
    public async Task DecodeAsync_RealWsprdOnSample_MatchesGolden()
    {
        var wsprd = WsprdLocator.Find();
        Assert.SkipWhen(wsprd is null, "wsprd is not installed; run third_party/fetch or install WSJT-X.");

        var wav = Path.Combine(TestPaths.Samples, "wspr", "150426_0918.wav");
        var (samples, rate) = WavFile.Read(wav);
        rate.Should().Be(12_000);
        var expected = File.ReadAllLines(Path.Combine(TestPaths.Samples, "wspr", "150426_0918.golden.txt"))
            .SkipWhile(l => l != "# stdout").Skip(1).TakeWhile(l => !l.StartsWith('#')).ToList();
        var (want, _) = WsprdDecoder.Parse(string.Join('\n', expected), Slot, Dial);

        var tmp = Path.Combine(Path.GetTempPath(), "ft8client-tests", Guid.NewGuid().ToString("N"));
        var decoder = new WsprdDecoder(new WsprdOptions(wsprd!, tmp, TimeSpan.FromSeconds(120)));
        var result = await decoder.DecodeAsync(new SlotAudio(Slot, Mode.Wspr, samples), Dial, Ct);

        result.Status.Should().Be(DecodeStatus.Ok, result.Message);
        result.SkippedLines.Should().Be(0);
        result.Spots.Should().BeEquivalentTo(want, o => o.WithStrictOrdering());
        result.Spots.Should().HaveCount(9);
    }
}
