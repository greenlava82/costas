// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Integrations.Cloudlog;

/// <summary>
/// Which logbook API a key belongs to (owner request 2026-11-18): a Wavelog 3.1+ token starts <c>wl2_</c> and works
/// only on the v2 API; any other key is a Wavelog or Cloudlog v1 key. See <see cref="CloudlogClient.ApiFor"/>.
/// </summary>
public enum WavelogApi
{
    /// <summary>The original API shared by Wavelog and Cloudlog: the key goes in the request body.</summary>
    V1,

    /// <summary>Wavelog's REST API (3.1.0 and later): a <c>wl2_</c> token as a Bearer header, with scopes.</summary>
    V2,
}
