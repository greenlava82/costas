// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Audio.Backends;

namespace Ft8Client.Audio.Tests;

public class AudioDeviceMatchTests
{
    private static readonly AudioDevice Speakers = new("{a}", "Speakers (Realtek(R) Audio)");
    private static readonly AudioDevice Codec = new("{b}", "Speakers (USB Audio CODEC)");

    [Fact]
    public void Find_SameId_FindsIt() =>
        AudioDeviceMatch.Find([Speakers, Codec], "{b}", "something else").Should().Be(Codec);

    [Fact]
    public void Find_NewIdSameName_FindsItByName() =>
        AudioDeviceMatch.Find([Speakers, Codec], "{old}", "Speakers (USB Audio CODEC)").Should().Be(Codec);

    [Fact]
    public void Find_TwoDevicesWithTheName_DoesNotGuess() =>
        AudioDeviceMatch.Find([Codec, Codec with { Id = "{c}" }], "{old}", Codec.Name).Should().BeNull();

    [Fact]
    public void Find_NotPluggedIn_Null() =>
        AudioDeviceMatch.Find([Speakers], "{b}", Codec.Name).Should().BeNull();

    [Fact]
    public void SuggestRadio_OneCodec_OffersIt() =>
        AudioDeviceMatch.SuggestRadio([Speakers, Codec]).Should().Be(Codec);

    [Fact]
    public void SuggestRadio_NoneOrTwoCodecs_Null()
    {
        AudioDeviceMatch.SuggestRadio([Speakers]).Should().BeNull();
        AudioDeviceMatch.SuggestRadio([Codec, Codec with { Id = "{c}", Name = "Speakers (2- USB Audio CODEC)" }]).Should().BeNull();
    }

    [Fact]
    public void SuggestFlex_RimuDaxDevices_PicksThem()
    {
        AudioDevice[] outputs = [Speakers, new("PulseAudio:rimu.dax.tx", "rimu.dax.tx (PulseAudio)"), new("ALSA:pulse", "pulse")];
        AudioDevice[] inputs = [new("PulseAudio:rimu.dax.rx1", "rimu.dax.rx1 (PulseAudio)"), new("PulseAudio:rimu.dax.rx2", "rimu.dax.rx2 (PulseAudio)")];

        AudioDeviceMatch.SuggestFlex(outputs, transmit: true)!.Id.Should().Be("PulseAudio:rimu.dax.tx");
        AudioDeviceMatch.SuggestFlex(inputs, transmit: false, channel: 2)!.Id.Should().Be("PulseAudio:rimu.dax.rx2");
    }

    [Fact]
    public void SuggestFlex_SmartSdrDaxOnWindows_PicksChannelNotItsNeighbours()
    {
        AudioDevice[] inputs =
        [
            new("{1}", "DAX Audio RX 1 (FlexRadio Systems DAX Audio)"),
            new("{10}", "DAX Audio RX 10 (FlexRadio Systems DAX Audio)"),
            new("{2}", "DAX Audio RX 2 (FlexRadio Systems DAX Audio)"),
        ];
        AudioDevice[] outputs = [Speakers, new("{t}", "DAX Audio TX (FlexRadio Systems DAX TX)")];

        AudioDeviceMatch.SuggestFlex(inputs, transmit: false, channel: 1)!.Id.Should().Be("{1}");
        AudioDeviceMatch.SuggestFlex(outputs, transmit: true)!.Id.Should().Be("{t}");
    }

    [Fact]
    public void SuggestFlex_NoDaxDevice_ReturnsNull() => AudioDeviceMatch.SuggestFlex([Speakers, Codec], transmit: true).Should().BeNull();
}
