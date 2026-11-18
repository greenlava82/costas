// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Ft8Client.Core.Cluster;
using Ft8Client.Core.Services;
using Ft8Client.Integrations.DxCluster;

namespace Ft8Client.Integrations.Tests;

public class DxClusterTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 20, 15, 0, DateTimeKind.Utc);

    // Real lines, copied as received from a DXSpider node (N6WS-6) and from a skimmer feed, in the SparkSDR group thread
    // "SparkSDR meets DXCluster" (docs/08-open-questions.md, V-DXC1).
    [Fact]
    public void Parse_DxSpiderFt8Spot_ReadsAllFields()
    {
        var s = ClusterSpotParser.Parse("DX de DL1NCH:      3573.0 OZ8BLR       FT8 -05dB from JO46 814Hz      2012Z", Now);

        s.Should().Be(new ClusterSpot("OZ8BLR", 3_573_000, "DL1NCH", "FT8 -05dB from JO46 814Hz", Now.Date.AddHours(20).AddMinutes(12)));
    }

    [Fact]
    public void Parse_DxSpiderSpotWithoutComment_EmptyComment()
    {
        var s = ClusterSpotParser.Parse("DX de HA5PP:       7039.6 P3AA                                        2012Z", Now);

        s!.DxCall.Should().Be("P3AA");
        s.FrequencyHz.Should().Be(7_039_600);
        s.Comment.Should().BeEmpty();
    }

    [Theory]
    [InlineData("DX de K1RA-#:    24919.0  TR8CA          FT4  -11 dB CQ 1050 Hz JJ40  1507Z", "K1RA", "TR8CA", 24_919_000, "FT4 -11 dB CQ 1050 Hz JJ40")]
    [InlineData("DX de KD7HGL-#:  14074.0  KF7MTE       FT8    0 dB DE 14076268 Hz         2011Z", "KD7HGL", "KF7MTE", 14_074_000, "FT8 0 dB DE 14076268 Hz")]
    [InlineData("DX de SV1YH:       7047.5 UX1MW        FT4 -06dB from KN98 2038Hz     2012Z", "SV1YH", "UX1MW", 7_047_500, "FT4 -06dB from KN98 2038Hz")]
    [InlineData("DX de SM6FMB-#:   7011.3  DK7JI          CW    11 dB  17 WPM  CQ      0731Z", "SM6FMB", "DK7JI", 7_011_300, "CW 11 dB 17 WPM CQ")]
    public void Parse_SkimmerAndHumanSpots_ReadsSpotterCallAndComment(string line, string spotter, string dx, long hz, string comment)
    {
        var s = ClusterSpotParser.Parse(line, Now)!;

        (s.Spotter, s.DxCall, s.FrequencyHz, s.Comment).Should().Be((spotter, dx, hz, comment));
    }

    [Fact]
    public void Parse_LocatorAfterTime_ReadsGrid()
    {
        // DXSpider with set/dxgrid adds the spotter's locator after the time.
        var s = ClusterSpotParser.Parse("DX de DL1NCH:      3573.0 OZ8BLR       FT8 -05dB from JO46 814Hz      2012Z JO50", Now)!;

        s.Grid.Should().Be("JO50");
        s.Comment.Should().Be("FT8 -05dB from JO46 814Hz");
    }

    [Fact]
    public void Parse_TimeAheadOfNow_IsYesterday()
    {
        var s = ClusterSpotParser.Parse("DX de HA5PP:       7039.6 P3AA                                        2359Z", Now)!;

        s.TimeUtc.Should().Be(Now.Date.AddDays(-1).AddHours(23).AddMinutes(59));
    }

    [Theory]
    [InlineData("DX de 3V/KF5EYY-#:  14022.5  RT3LA          CW    34 dB  28 WPM  CQ")] // wrapped at 80 columns, as seen in the wild
    [InlineData("WWV de W0MU <18>:   SFI=68, A=4, K=1, No Storms -> No Storms")]
    [InlineData("To ALL de G4ABC: anyone hear the 6 m opening?")]
    [InlineData("DX de HA5PP:       7039.6 73                                          2012Z")]
    [InlineData("DX de HA5PP:       7039.6 P3AA                                        2561Z")]
    public void Parse_NotASpot_ReturnsNull(string line)
    {
        ClusterSpotParser.Parse(line, Now).Should().BeNull();
    }

    [Theory]
    [InlineData("login: ", true)]
    [InlineData("Please enter your call: ", true)]
    [InlineData("Please enter your callsign:", true)]
    [InlineData("Welcome to the cluster", false)]
    public void IsLoginPrompt_Text_Recognised(string text, bool expected)
    {
        ClusterFeedClient.IsLoginPrompt(text).Should().Be(expected);
    }

    [Fact]
    public async Task Start_NodePrompts_LogsInAndRaisesEverySpot()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var login = new TaskCompletionSource<string>();
        var ct = TestContext.Current.CancellationToken;
        _ = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync(ct);
            var s = c.GetStream();
            await s.WriteAsync(new byte[] { 255, 251, 1 }, ct);
            await s.WriteAsync(Encoding.ASCII.GetBytes("login: "), ct);
            var buf = new byte[64];
            var n = await s.ReadAsync(buf, ct);
            login.TrySetResult(Encoding.ASCII.GetString(buf, 0, n));
            await s.WriteAsync(Encoding.ASCII.GetBytes(
                "Hello W1AW, this is a test node\r\n" +
                "DX de DL1NCH:      3573.0 OZ8BLR       FT8 -05dB from JO46 814Hz      2012Z\r\n" +
                "DX de HA5PP:       7039.6 P3AA                                        2012Z\r\n"), ct);
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }, ct);
        var spots = new List<ClusterSpot>();
        var got = new TaskCompletionSource();
        await using var feed = new ClusterFeedClient("127.0.0.1", port, () => Now);
        feed.Spot += s =>
        {
            lock (spots)
            {
                spots.Add(s);
                if (spots.Count == 2) got.TrySetResult();
            }
        };
        feed.Start("w1aw");

        (await login.Task.WaitAsync(TimeSpan.FromSeconds(10), ct)).Should().Be("W1AW\r\n");
        await got.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);

        feed.Connected.Should().BeTrue();
        lock (spots) spots.Select(s => s.DxCall).Should().Equal("OZ8BLR", "P3AA");
        feed.LinesRead.Should().Be(2);
    }

    [Fact]
    public async Task Start_NodeAsksForPassword_ReportsDownAndWaits()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var ct = TestContext.Current.CancellationToken;
        var accepted = 0;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                using var c = await listener.AcceptTcpClientAsync(ct);
                Interlocked.Increment(ref accepted);
                var s = c.GetStream();
                await s.WriteAsync(Encoding.ASCII.GetBytes("login: "), ct);
                _ = await s.ReadAsync(new byte[64], ct);
                await s.WriteAsync(Encoding.ASCII.GetBytes("password: "), ct);
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }, ct);
        var down = new TaskCompletionSource<ServiceStatus>();
        await using var feed = new ClusterFeedClient("127.0.0.1", port, () => Now, new Backoff(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)));
        feed.StatusChanged += st =>
        {
            if (st.Health == ServiceHealth.Down) down.TrySetResult(st);
        };
        feed.Start("W1AW");

        var st = await down.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        await Task.Delay(300, ct);

        st.Message.Should().Contain("password");
        Volatile.Read(ref accepted).Should().Be(1);
    }

    [Fact]
    public async Task Start_NodeUnreachable_ReportsDown()
    {
        var down = new TaskCompletionSource<ServiceStatus>();
        await using var feed = new ClusterFeedClient("127.0.0.1", 1, () => Now);
        feed.StatusChanged += st => down.TrySetResult(st);
        feed.Start("W1AW");

        var st = await down.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        st.Health.Should().Be(ServiceHealth.Down);
        feed.Connected.Should().BeFalse();
    }
}
