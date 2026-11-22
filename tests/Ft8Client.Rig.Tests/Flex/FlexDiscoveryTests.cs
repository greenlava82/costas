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

public class FlexDiscoveryTests
{
    // A VITA-49 header (28 bytes) then the payload, null-padded, as a radio sends it.
    private static byte[] Packet(string payload)
    {
        var header = new byte[28];
        header[0] = 0x38;
        var body = Encoding.ASCII.GetBytes(payload);
        var padded = new byte[(body.Length + 3) / 4 * 4];
        body.CopyTo(padded, 0);
        return [.. header, .. padded];
    }

    private const string Payload =
        "discovery_protocol_version=3.0.0.2 model=FLEX-8600 serial=4224-1213-8600-1234 version=3.8.20.33898 nickname=Shack_Radio callsign=VK4XX ip=192.168.1.20 port=4992 status=In_Use inuse_ip=192.168.1.30";

    [Fact]
    public void Parse_DiscoveryPacket_ReadsModelNameAndAddress()
    {
        var radio = FlexDiscovery.Parse(Packet(Payload));

        radio.Should().Be(new FlexRadioInfo("FLEX-8600", "4224-1213-8600-1234", "Shack Radio", "VK4XX", "192.168.1.20", 4992));
        radio!.Address.Should().Be("192.168.1.20:4992");
    }

    [Fact]
    public void Parse_NoIpAddress_ReturnsNull() =>
        FlexDiscovery.Parse(Packet("discovery_protocol_version=3.0.0.2 model=FLEX-6400 serial=1")).Should().BeNull();

    [Fact]
    public void Parse_OtherTraffic_ReturnsNull() => FlexDiscovery.Parse("hello"u8).Should().BeNull();

    [Fact]
    public async Task ListenAsync_RadioBroadcasting_FindsItOnce()
    {
        var listening = FlexDiscovery.ListenAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        using var sender = new UdpClient();
        var packet = Packet(Payload);
        // As a radio does, broadcast every so often for as long as the search listens (however slow the runner).
        while (!listening.IsCompleted)
        {
            await sender.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, FlexDiscovery.Port), TestContext.Current.CancellationToken);
            await Task.Delay(150, TestContext.Current.CancellationToken);
        }

        var found = await listening;

        found.Should().ContainSingle().Which.Model.Should().Be("FLEX-8600");
    }
}
