// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Decoding;

namespace Ft8Client.Core.Wspr;

/// <summary>
/// Decodes one 2-minute WSPR slot. A separate interface from <see cref="IDecoder"/>, whose context is shaped for the FT
/// modes; the one implementation runs <c>wsprd</c>.
/// </summary>
public interface IWsprDecoder
{
    /// <summary>Decodes a slot recorded at <paramref name="dialHz"/>. Never throws for a failed run: it reports the status.</summary>
    Task<WsprDecodeResult> DecodeAsync(SlotAudio slot, long dialHz, CancellationToken ct);
}
