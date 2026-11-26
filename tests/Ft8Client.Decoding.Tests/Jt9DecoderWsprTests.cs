// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Decoding;

namespace Ft8Client.Decoding.Tests;

public class Jt9DecoderWsprTests
{
    [Fact]
    public void BuildArguments_Wspr_Throws() =>
        FluentActions.Invoking(() => Jt9Decoder.BuildArguments(Mode.Wspr, DecodeContext.Anonymous, "/bin", "/w", "x.wav"))
            .Should().Throw<ArgumentOutOfRangeException>();
}
