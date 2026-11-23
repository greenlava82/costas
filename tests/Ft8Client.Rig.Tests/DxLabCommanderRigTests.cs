// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Ft8Client.Core.Time;
using Ft8Client.Rig.DxLab;

namespace Ft8Client.Rig.Tests;

public sealed class DxLabCommanderRigTests
{
    // The invariant culture writes 14,074.000, as an English Windows does.
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;
    private static readonly ManualClock Clock = new(new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc));

    private static Task<DxLabCommanderRig> Connect(FakeCommander c, bool cat = true) =>
        DxLabCommanderRig.ConnectAsync($"127.0.0.1:{c.Port}", Clock, En, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(10), cat);

    [Fact]
    public async Task GetStateAsync_CommanderAnswers_FrequencyModeAndSplit()
    {
        await using var c = new FakeCommander { FrequencyHz = 7_074_000, Mode = "DATA-U", Split = true };
        await using var rig = await Connect(c);

        var state = await rig.GetStateAsync(TestContext.Current.CancellationToken);

        state.FrequencyHz.Should().Be(7_074_000);
        state.Mode.Should().Be("PKTUSB");
        state.Split.Should().BeTrue();
        state.Ptt.Should().BeFalse();
    }

    [Fact]
    public async Task SetFrequencyAsync_NewFrequency_SendsCmdSetFreqInKilohertz()
    {
        await using var c = new FakeCommander();
        await using var rig = await Connect(c);

        await rig.SetFrequencyAsync(21_074_000, TestContext.Current.CancellationToken);
        (await rig.GetFrequencyAsync(TestContext.Current.CancellationToken)).Should().Be(21_074_000);

        c.Commands.Should().Contain("CmdSetFreq<xcvrfreq:10>21,074.000");
    }

    [Fact]
    public async Task SetModeAsync_DataMode_SendsDataU()
    {
        await using var c = new FakeCommander { Mode = "USB" };
        await using var rig = await Connect(c);

        await rig.SetModeAsync(RigMode.PktUsb, TestContext.Current.CancellationToken);
        (await rig.GetStateAsync(TestContext.Current.CancellationToken)).Mode.Should().Be("PKTUSB");
    }

    [Fact]
    public async Task SetPttAsync_CommanderLagsAFewPolls_ConfirmsKeyAndUnkey()
    {
        await using var c = new FakeCommander { TxLag = 3 };
        await using var rig = await Connect(c);

        await rig.SetPttAsync(true, TestContext.Current.CancellationToken);
        c.Tx.Should().BeTrue();
        (await rig.GetStateAsync(TestContext.Current.CancellationToken)).Ptt.Should().BeTrue();

        await rig.SetPttAsync(false, TestContext.Current.CancellationToken);
        c.Tx.Should().BeFalse();
        c.Commands.Count(x => x == "CmdSendTx").Should().BeGreaterThan(3);
    }

    [Fact]
    public async Task SetPttAsync_RadioNeverKeys_ThrowsAndRaisesFault()
    {
        await using var c = new FakeCommander { IgnoreTx = true };
        await using var rig = await Connect(c);
        RigFault? fault = null;
        rig.Faulted += (_, f) => fault = f;

        var act = () => rig.SetPttAsync(true, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RigException>().WithMessage("*did not key*");
        fault.Should().NotBeNull();
    }

    [Fact]
    public async Task SetPttAsync_Vox_SendsNoPttCommand()
    {
        await using var c = new FakeCommander();
        await using var rig = await Connect(c, cat: false);

        await rig.SetPttAsync(true, TestContext.Current.CancellationToken);
        await rig.SetPttAsync(false, TestContext.Current.CancellationToken);

        c.Commands.Should().NotContain(x => x == "CmdTX" || x == "CmdRX");
    }

    [Fact]
    public async Task SetSplitAsync_OnThenOff_SendsQsxSplitThenSplitOff()
    {
        await using var c = new FakeCommander();
        await using var rig = await Connect(c);

        await rig.SetSplitAsync(true, 14_075_500, TestContext.Current.CancellationToken);
        (await rig.GetStateAsync(TestContext.Current.CancellationToken)).Split.Should().BeTrue();
        await rig.SetSplitAsync(false, 0, TestContext.Current.CancellationToken);
        (await rig.GetStateAsync(TestContext.Current.CancellationToken)).Split.Should().BeFalse();

        c.Commands.Should().Contain("CmdQSXSplit<xcvrfreq:10>14,075.500<SuppressDual:1>Y<SuppressModeChange:1>Y");
        c.Commands.Should().Contain("CmdSplit<1:3>off");
    }

    [Fact]
    public async Task DisposeAsync_WhileKeyed_SendsCmdRx()
    {
        await using var c = new FakeCommander();
        var rig = await Connect(c);
        await rig.SetPttAsync(true, TestContext.Current.CancellationToken);

        await rig.DisposeAsync();

        // The release is sent without a reply, so wait for Commander to have read it.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (c.Tx && DateTime.UtcNow < deadline) await Task.Delay(10, TestContext.Current.CancellationToken);
        c.Tx.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectAsync_NothingListening_SaysHowToFixIt()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var act = () => DxLabCommanderRig.ConnectAsync($"127.0.0.1:{port}", Clock, En, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(10));

        await act.Should().ThrowAsync<RigException>().WithMessage("*DXLab Commander is not answering*52002*");
    }

    [Theory]
    [InlineData(null, "127.0.0.1", 52002)]
    [InlineData("", "127.0.0.1", 52002)]
    [InlineData("shack-pc", "shack-pc", 52002)]
    [InlineData("192.168.1.5:52102", "192.168.1.5", 52102)]
    public void ParseAddress_Text_HostAndPort(string? text, string host, int port) =>
        DxLabCommanderRig.ParseAddress(text).Should().Be((host, port));
}
