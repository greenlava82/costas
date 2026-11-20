// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Services;
using Ft8Client.Rig.Flex;

namespace Ft8Client.Rig.Tests.Flex;

public class FlexAccessoryTests
{
    // Shapes of the lines a FLEX-8600 sent for a paired PGXL and TGXL, from a third-party capture
    // (NereusSDR captures/flex-tgxl-direct-NOTES.md, docs/08-open-questions.md V-FLX1), serial numbers made up.
    private const string PgxlLine = "S0|amplifier 0x18E5F0AA ip=192.168.1.35 model=PowerGeniusXL serial_num=00-000/00-0000 ant=ANT1:PORTA,ANT2:NONE state=STANDBY";
    private const string TgxlLine = "S0|amplifier 0x2C01D3B4 ip=192.168.1.34 model=TunerGeniusXL serial_num=000000-0 version=1.2.17 nickname=Tuner_Genius_XL operate=1 bypass=0 tuning=0";

    [Fact]
    public void Parse_AmplifierStatus_ReadsHandleAndKeys()
    {
        var s = FlexStatusLine.Parse(PgxlLine)!;

        s.Handle.Should().Be("0x18E5F0AA");
        s.Values["model"].Should().Be("PowerGeniusXL");
        s.Values["state"].Should().Be("STANDBY");
        s.Values["ant"].Should().Be("ANT1:PORTA,ANT2:NONE");
        s.Removed.Should().BeFalse();
    }

    [Fact]
    public void Parse_UpperCaseKeys_AreLowered()
    {
        FlexStatusLine.Parse("S0|amplifier 0x1 Operate=1")!.Values.Should().ContainKey("operate");
    }

    [Theory]
    [InlineData("R12|0|")]
    [InlineData("S0|slice 0 RF_frequency=14.074000")]
    [InlineData("S0|amplifier nothex state=IDLE")]
    [InlineData("S0|amplifier")]
    [InlineData("M10000001|Client connected")]
    [InlineData("V1.4.0.0")]
    [InlineData("")]
    public void Parse_OtherLines_Null(string line)
    {
        FlexStatusLine.Parse(line).Should().BeNull();
    }

    [Fact]
    public void Parse_Removed_IsRemoved()
    {
        FlexStatusLine.Parse("S0|amplifier 0x18E5F0AA removed")!.Removed.Should().BeTrue();
    }

    [Fact]
    public void With_PgxlAndTgxl_KeptApartByModel()
    {
        var a = FlexAccessories.Empty.With(FlexStatusLine.Parse(PgxlLine)!).With(FlexStatusLine.Parse(TgxlLine)!);

        a.Amplifier.Should().Be(new FlexAmplifier("0x18E5F0AA", "PowerGeniusXL", FlexAmpState.Standby));
        a.Amplifier!.Operate.Should().BeFalse();
        a.Amplifier.Name.Should().Be("PowerGenius XL");
        a.Tuner.Should().Be(new FlexTuner("0x2C01D3B4", "TunerGeniusXL", Operate: true, Bypass: false, Tuning: false));
        a.Tuner!.Name.Should().Be("TunerGenius XL");
    }

    [Fact]
    public void With_LaterLineWithOnlyState_KeepsModel()
    {
        var a = FlexAccessories.Empty.With(FlexStatusLine.Parse(PgxlLine)!).With(FlexStatusLine.Parse("S0|amplifier 0x18E5F0AA state=IDLE")!);

        a.Amplifier!.State.Should().Be(FlexAmpState.Idle);
        a.Amplifier.Operate.Should().BeTrue();
        a.Amplifier.Model.Should().Be("PowerGeniusXL");
    }

    [Fact]
    public void With_NoModelYet_NotShown()
    {
        var a = FlexAccessories.Empty.With(FlexStatusLine.Parse("S0|amplifier 0x18E5F0AA state=IDLE")!);

        a.Amplifiers.Should().BeEmpty();
        a.With(FlexStatusLine.Parse("S0|amplifier 0x18E5F0AA model=PowerGeniusXL")!).Amplifier!.State.Should().Be(FlexAmpState.Idle);
    }

    [Fact]
    public void With_Removed_DropsDevice()
    {
        var a = FlexAccessories.Empty.With(FlexStatusLine.Parse(PgxlLine)!).With(FlexStatusLine.Parse("S0|amplifier 0x18E5F0AA removed")!);

        a.Amplifier.Should().BeNull();
    }

    [Theory]
    [InlineData("POWERUP", FlexAmpState.PowerUp, false)]
    [InlineData("SELFCHECK", FlexAmpState.SelfCheck, false)]
    [InlineData("STANDBY", FlexAmpState.Standby, false)]
    [InlineData("IDLE", FlexAmpState.Idle, true)]
    [InlineData("TRANSMIT_A", FlexAmpState.TransmitA, true)]
    [InlineData("TRANSMIT_B", FlexAmpState.TransmitB, true)]
    [InlineData("FAULT", FlexAmpState.Fault, true)]
    [InlineData("WARMING", FlexAmpState.Unknown, false)]
    public void StateOf_EachValue_ReadsStateAndOperate(string value, FlexAmpState state, bool operate)
    {
        FlexAccessories.StateOf(value).Should().Be(state);
        new FlexAmplifier("0x1", "PowerGeniusXL", state).Operate.Should().Be(operate);
    }

    [Fact]
    public void Commands_AsFlexLibSendsThem()
    {
        FlexCommands.AmpOperate("0xA0F8467", true).Should().Be("amplifier set 0xA0F8467 operate=1");
        FlexCommands.AmpOperate("0xA0F8467", false).Should().Be("amplifier set 0xA0F8467 operate=0");
        FlexCommands.TunerOperate("0x2C01D3B4", false).Should().Be("tgxl set handle=0x2C01D3B4 mode=0");
        FlexCommands.TunerBypass("0x2C01D3B4", true).Should().Be("tgxl set handle=0x2C01D3B4 bypass=1");
        FlexCommands.Frame(44, "amplifier set 0xA0F8467 operate=1").Should().Be("C44|amplifier set 0xA0F8467 operate=1\n");
    }

    [Theory]
    [InlineData("0x1 operate=1")]
    [InlineData("1234")]
    [InlineData("0x")]
    [InlineData("0x123456789")]
    public void Commands_NotAHandle_Throws(string handle)
    {
        var act = () => FlexCommands.AmpOperate(handle, true);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("R44|0|", 44, 0u, "")]
    [InlineData("R44|0", 44, 0u, "")]
    [InlineData("R7|500000A7|", 7, 0x500000A7u, "")]
    [InlineData("R7|50000016|Malformed Command", 7, 0x50000016u, "Malformed Command")]
    public void Reply_Parse_ReadsSequenceAndCode(string line, int seq, uint code, string message)
    {
        FlexReply.Parse(line).Should().Be(new FlexReply(seq, code, message));
    }

    [Fact]
    public async Task Client_Connects_SubscribesAndReadsDevices()
    {
        await using var radio = new FakeFlexRadio();
        radio.OnSubscribe.AddRange([PgxlLine, TgxlLine]);
        await using var client = new FlexAccessoryClient("127.0.0.1", radio.Port, pingEvery: TimeSpan.FromMinutes(1));
        var seen = new TaskCompletionSource<FlexAccessories>();
        client.Changed += a => { if (a.Amplifier is not null && a.Tuner is not null) seen.TrySetResult(a); };

        client.Start();
        var a = await seen.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        a.Amplifier!.State.Should().Be(FlexAmpState.Standby);
        a.Tuner!.Operate.Should().BeTrue();
        radio.Commands.Should().Equal("sub amplifier all");
        client.Connected.Should().BeTrue();
    }

    [Fact]
    public async Task Client_Connecting_SendsNothingThatChangesAnything()
    {
        await using var radio = new FakeFlexRadio();
        radio.OnSubscribe.AddRange([PgxlLine, TgxlLine]);
        await using var client = new FlexAccessoryClient("127.0.0.1", radio.Port, pingEvery: TimeSpan.FromMilliseconds(50));
        client.Start();

        await WaitUntil(() => radio.Commands.Count(c => c == "ping") >= 3);

        radio.Commands.Should().OnlyContain(c => c == "sub amplifier all" || c == "ping");
    }

    [Fact]
    public async Task Client_Send_ReturnsNullWhenTaken()
    {
        await using var radio = new FakeFlexRadio();
        radio.OnSubscribe.Add(PgxlLine);
        await using var client = new FlexAccessoryClient("127.0.0.1", radio.Port, pingEvery: TimeSpan.FromMinutes(1));
        client.Start();
        await WaitUntil(() => client.Current.Amplifier is not null);

        var r = await client.SendAsync(FlexCommands.AmpOperate("0x18E5F0AA", true), CancellationToken.None);

        r.Should().BeNull();
        radio.Commands.Should().Contain("amplifier set 0x18E5F0AA operate=1");
    }

    [Fact]
    public async Task Client_SendRefused_SaysWhy()
    {
        await using var radio = new FakeFlexRadio();
        radio.Codes["amplifier set"] = "500000A7";
        await using var client = new FlexAccessoryClient("127.0.0.1", radio.Port, pingEvery: TimeSpan.FromMinutes(1));
        client.Start();
        await WaitUntil(() => client.Connected);

        var r = await client.SendAsync(FlexCommands.AmpOperate("0x18E5F0AA", true), CancellationToken.None);

        r.Should().Be("The radio no longer knows that device.");
    }

    [Fact]
    public async Task Client_NoReply_SaysSoAndDoesNotRetry()
    {
        await using var radio = new FakeFlexRadio();
        await using var client = new FlexAccessoryClient("127.0.0.1", radio.Port, pingEvery: TimeSpan.FromMinutes(1), replyTimeout: TimeSpan.FromMilliseconds(300));
        client.Start();
        await WaitUntil(() => client.Connected);
        radio.Answers = false;

        var r = await client.SendAsync(FlexCommands.AmpOperate("0x18E5F0AA", true), CancellationToken.None);

        r.Should().Be("The radio did not answer.");
        radio.Commands.Count(c => c.StartsWith("amplifier set", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task Client_CallerGivesUp_SaysNoAnswerInsteadOfThrowing()
    {
        await using var radio = new FakeFlexRadio();
        await using var client = new FlexAccessoryClient("127.0.0.1", radio.Port, pingEvery: TimeSpan.FromMinutes(1), replyTimeout: TimeSpan.FromMinutes(1));
        client.Start();
        await WaitUntil(() => client.Connected);
        radio.Answers = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var r = await client.SendAsync(FlexCommands.AmpOperate("0x18E5F0AA", true), cts.Token);

        r.Should().Be("The radio did not answer.");
    }

    [Fact]
    public async Task Client_NotConnected_Refuses()
    {
        await using var client = new FlexAccessoryClient("127.0.0.1", 1);

        (await client.SendAsync("ping", CancellationToken.None)).Should().Be("Not connected to the radio.");
    }

    [Fact]
    public async Task Client_RadioDrops_ForgetsDevicesAndReconnects()
    {
        await using var radio = new FakeFlexRadio();
        radio.OnSubscribe.Add(PgxlLine);
        var statuses = new List<ServiceHealth>();
        await using var client = new FlexAccessoryClient("127.0.0.1", radio.Port, pingEvery: TimeSpan.FromMinutes(1),
            backoff: new Backoff(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)));
        client.StatusChanged += s => { lock (statuses) statuses.Add(s.Health); };
        // The devices are forgotten only for the moment between the drop and the reconnect (50 ms here), too brief to
        // catch by polling on a busy runner, so the event records it.
        var forgotten = 0;
        client.Changed += a => { if (a.Amplifier is null) Interlocked.Exchange(ref forgotten, 1); };
        client.Start();
        await WaitUntil(() => client.Current.Amplifier is not null);

        radio.Drop();
        await WaitUntil(() => Volatile.Read(ref forgotten) == 1);
        await WaitUntil(() => radio.Connections >= 2 && client.Current.Amplifier is not null);

        lock (statuses) statuses.Should().Contain(ServiceHealth.Degraded).And.Contain(ServiceHealth.Ok);
    }

    [Fact]
    public async Task Client_PingUnanswered_DropsConnection()
    {
        await using var radio = new FakeFlexRadio();
        radio.OnSubscribe.Add(PgxlLine);
        await using var client = new FlexAccessoryClient("127.0.0.1", radio.Port, pingEvery: TimeSpan.FromMilliseconds(100),
            replyTimeout: TimeSpan.FromMilliseconds(200), backoff: new Backoff(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)));
        client.Start();
        await WaitUntil(() => client.Current.Amplifier is not null);

        radio.Answers = false;

        await WaitUntil(() => !client.Connected && client.Current.Amplifier is null);
    }

    [Fact]
    public async Task Client_Unreachable_ReportsDown()
    {
        var down = new TaskCompletionSource<ServiceStatus>();
        await using var client = new FlexAccessoryClient("127.0.0.1", 1, backoff: new Backoff(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)));
        client.StatusChanged += s => down.TrySetResult(s);
        client.Start();

        var s = await down.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        s.Health.Should().Be(ServiceHealth.Down);
        s.Message.Should().StartWith("Could not reach the radio at 127.0.0.1");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Condition not met");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
