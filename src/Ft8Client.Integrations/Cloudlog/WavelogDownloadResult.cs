// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Integrations.Cloudlog;

/// <summary>What asking Wavelog for one page of contacts came to.</summary>
/// <param name="Page">The page, or null when Wavelog did not give one.</param>
/// <param name="Error">Why not, in words for the status line; never contains the token.</param>
/// <param name="Permanent">True when retrying soon cannot help (bad, expired or under-scoped token): retry slowly.</param>
/// <param name="RetryAfter">How long the site asked Costas to wait, or null.</param>
public sealed record WavelogDownloadResult(WavelogDownloadPage? Page, string? Error = null, bool Permanent = false, TimeSpan? RetryAfter = null);
