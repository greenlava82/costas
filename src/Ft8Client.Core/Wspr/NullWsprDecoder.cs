// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Decoding;

namespace Ft8Client.Core.Wspr;

/// <summary>A WSPR decoder that decodes nothing and says why, for when <c>wsprd</c> cannot be found.</summary>
/// <param name="reason">What the operator is told.</param>
public sealed class NullWsprDecoder(string reason) : IWsprDecoder
{
    /// <inheritdoc />
    public Task<WsprDecodeResult> DecodeAsync(SlotAudio slot, long dialHz, CancellationToken ct) =>
        Task.FromResult(new WsprDecodeResult([], 0, TimeSpan.Zero, DecodeStatus.Failed, reason));
}
