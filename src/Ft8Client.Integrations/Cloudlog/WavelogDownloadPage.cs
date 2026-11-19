// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Integrations.Cloudlog;

/// <summary>One page of a Wavelog v2 ADIF download (<c>GET api/v2/qso?format=adif</c>).</summary>
/// <param name="Exported">How many contacts the page holds.</param>
/// <param name="LastFetchedId">Wavelog's id of the last contact on the page: ask from here next time.</param>
/// <param name="Adif">The contacts as ADIF, or null when there were none.</param>
/// <param name="HasMore">True when more pages follow.</param>
public sealed record WavelogDownloadPage(int Exported, long LastFetchedId, string? Adif, bool HasMore);
