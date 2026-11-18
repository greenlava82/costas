// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Integrations.Cloudlog;

/// <summary>What a Wavelog v2 token says about itself (<c>GET /api/v2/token</c>).</summary>
/// <param name="Name">The name the operator gave the token in Wavelog.</param>
/// <param name="Scopes">Its scopes, such as <c>qso:read</c> and <c>qso:write</c>.</param>
/// <param name="ExpiresAt">When it stops working, as Wavelog gives it; null for a token that never expires.</param>
public sealed record WavelogToken(string Name, IReadOnlyList<string> Scopes, DateTime? ExpiresAt)
{
    /// <summary>Can read contacts (a later download for worked-before).</summary>
    public bool CanRead => Has("qso:read");

    /// <summary>Can upload contacts.</summary>
    public bool CanWrite => Has("qso:write");

    /// <summary>Can list station locations, which Test uses.</summary>
    public bool CanListStations => Has("station:read");

    private bool Has(string scope) => Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
}
