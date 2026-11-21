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
using Ft8Client.Rig.Flex;

namespace Ft8Client.Rig.Tests.Flex;

public class FlexSliceTests
{
    [Fact]
    public void Parse_SliceStatus_ReadsState() =>
        FlexSlice.Parse("S6B5B2E9C|slice 1 in_use=1 sample_rate=24000 RF_frequency=14.074000 index_letter=B tx=1 dax=2 mode=DIGU")
            .Should().Be(new FlexSlice(1, true, true, 2, "B"));

    [Fact]
    public void Parse_OtherObject_ReturnsNull() => FlexSlice.Parse("S6B5B2E9C|amplifier 0x18E5F0AA operate=1").Should().BeNull();

    [Fact]
    public void FromLines_PartialUpdates_KeepEarlierValues()
    {
        var slices = FlexSlice.FromLines(["S1|slice 0 in_use=1 tx=0 dax=1 index_letter=A", "S1|slice 0 tx=1", "R1|0|"]);

        slices.Should().ContainSingle().Which.Should().Be(new FlexSlice(0, true, true, 1, "A"));
    }

    [Fact]
    public void Pick_TransmitSliceOpen_PicksIt()
    {
        FlexSlice[] slices = [new(0, true, false, 1, "A"), new(1, true, true, 2, "B"), new(2, false, false, 0, "")];

        FlexSlice.Pick(slices)!.Index.Should().Be(1);
    }

    [Fact]
    public void Pick_NoTransmitSlice_PicksFirstOpen() =>
        FlexSlice.Pick([new FlexSlice(0, false, false, 0, ""), new FlexSlice(3, true, false, 1, "D")])!.Index.Should().Be(3);

    [Fact]
    public void Pick_NoneOpen_ReturnsNull() => FlexSlice.Pick([new FlexSlice(0, false, false, 0, "")]).Should().BeNull();

    [Fact]
    public async Task ProbeAsync_RadioAnswersSubscription_ReturnsSlices()
    {
        var ct = TestContext.Current.CancellationToken;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var radio = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            var stream = client.GetStream();
            var hello = Encoding.ASCII.GetBytes("V1.4.0.0\nH6B5B2E9C\n");
            await stream.WriteAsync(hello, ct);
            var buffer = new byte[256];
            var n = await stream.ReadAsync(buffer, ct);
            Encoding.ASCII.GetString(buffer, 0, n).Should().Be("C1|sub slice all\n");
            var reply = Encoding.ASCII.GetBytes("S6B5B2E9C|slice 0 in_use=1 index_letter=A tx=1 dax=1\nR1|0|\n");
            await stream.WriteAsync(reply, ct);
            await Task.Delay(2000, ct);
        }, ct);

        var slices = await FlexSliceProbe.ProbeAsync("127.0.0.1", port, ct);

        slices.Should().ContainSingle().Which.Should().Be(new FlexSlice(0, true, true, 1, "A"));
        listener.Stop();
        await radio;
    }
}
