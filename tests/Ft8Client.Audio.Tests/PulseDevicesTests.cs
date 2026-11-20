// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Audio.Backends;

namespace Ft8Client.Audio.Tests;

public class PulseDevicesTests
{
    [Fact]
    public void ParseJson_SinksWithDescriptions_ListsEachWithItsName()
    {
        const string json = """
            [{"index":56,"state":"SUSPENDED","name":"alsa_output.pci-0000_00_1f.3.analog-stereo","description":"Built-in Audio Analog Stereo"},
             {"index":71,"state":"RUNNING","name":"rimu.dax.tx","description":"rimu.dax.tx"}]
            """;

        var list = PulseDevices.ParseJson(json);

        list.Should().NotBeNull();
        list!.Select(d => d.Id).Should().Equal("PulseAudio:alsa_output.pci-0000_00_1f.3.analog-stereo", "PulseAudio:rimu.dax.tx");
        list[0].Name.Should().Be("Built-in Audio Analog Stereo – alsa_output.pci-0000_00_1f.3.analog-stereo (PulseAudio)");
        list[1].Name.Should().Be("rimu.dax.tx (PulseAudio)");
    }

    [Fact]
    public void ParseJson_NotJson_ReturnsNull() => PulseDevices.ParseJson("Unknown option --format").Should().BeNull();

    [Fact]
    public void ParseShort_TabSeparatedList_ListsNames()
    {
        const string text = "1\trimu.dax.rx1\tmodule-remap-source.c\tfloat32le 2ch 48000Hz\tRUNNING\n2\tcostas.monitor\tmodule-null-sink.c\ts16le 2ch 44100Hz\tIDLE\n";

        PulseDevices.ParseShort(text).Select(d => d.Id).Should().Equal("PulseAudio:rimu.dax.rx1", "PulseAudio:costas.monitor");
    }

    [Fact]
    public void NameOf_PulseId_ReturnsName() => PulseDevices.NameOf("PulseAudio:rimu.dax.tx").Should().Be("rimu.dax.tx");

    [Fact]
    public void NameOf_AlsaId_ReturnsNull() => PulseDevices.NameOf("ALSA:pulse").Should().BeNull();
}
