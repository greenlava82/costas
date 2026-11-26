// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Decoding;

namespace Ft8Client.Decoding.Tests;

public class SampleFileWsprTests
{
    [Theory]
    [InlineData("samples/wspr/150426_0918.wav", true)]
    [InlineData(@"samples\WSPR\150426_0918.wav", true)]
    [InlineData("samples/ft8/181201_180245.wav", false)]
    [InlineData("samples/rtty/synthetic-busy-20m.wav", false)]
    public void IsWsprMode_Path_FromFolder(string path, bool expected) => SampleFile.IsWsprMode(path).Should().Be(expected);

    [Theory]
    [InlineData("samples/ft8/181201_180245.wav", true)]
    [InlineData("samples/ft4/000000_000002.wav", true)]
    [InlineData("samples/psk31/synthetic-busy-20m.wav", false)]
    [InlineData("samples/wspr/150426_0918.wav", false)]
    public void IsJt9Slot_Path_OnlyFtSlots(string path, bool expected) => SampleFile.IsJt9Slot(path).Should().Be(expected);
}
