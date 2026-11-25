// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Mqtt;

/// <summary>The on-air session at one moment, as published.</summary>
/// <param name="Active">A session is running: loads sensitive to RF should be off.</param>
/// <param name="SessionId">Identifies the running session, or the last one when none is running.</param>
/// <param name="SinceUtc">When the session started (or when the last one ended, when none is running).</param>
/// <param name="EndReason">Why the last session ended; null while one is running or before the first.</param>
/// <param name="Transmitting">PTT is keyed now.</param>
public sealed record OnAirSnapshot(bool Active, string? SessionId, DateTime? SinceUtc, SessionEndReason? EndReason, bool Transmitting)
{
    /// <summary>No session has run yet and nothing is keyed.</summary>
    public static OnAirSnapshot Idle { get; } = new(false, null, null, null, false);
}
