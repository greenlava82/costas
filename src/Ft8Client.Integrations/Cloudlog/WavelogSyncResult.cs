// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Integrations.Cloudlog;

/// <summary>What a Wavelog download came to. Pages read before a failure are kept.</summary>
/// <param name="Fetched">Contacts read from Wavelog.</param>
/// <param name="Inserted">New in the local log.</param>
/// <param name="Merged">Already in the local log (missing fields and confirmations taken).</param>
/// <param name="Full">True when it started from the beginning of the Wavelog log.</param>
/// <param name="Error">Why it stopped early, or null when it read everything.</param>
/// <param name="Permanent">True when retrying soon cannot help.</param>
/// <param name="RetryAfter">How long the site asked Costas to wait, or null.</param>
public sealed record WavelogSyncResult(int Fetched, int Inserted, int Merged, bool Full, string? Error = null, bool Permanent = false,
    TimeSpan? RetryAfter = null)
{
    /// <summary>True when the local log changed.</summary>
    public bool Changed => Inserted + Merged > 0;
}
