// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Cluster;

/// <summary>One DX cluster spot: someone on the cluster network reported hearing a station (docs/05-integrations.md 5b).</summary>
/// <param name="DxCall">The station spotted, upper case as given (may carry <c>/P</c> and the like).</param>
/// <param name="FrequencyHz">The frequency spotted, in hertz (a dial frequency or the signal's own frequency).</param>
/// <param name="Spotter">Who spotted it, upper case, without a skimmer's <c>-#</c> marker.</param>
/// <param name="Comment">The spot's comment, trimmed; empty when there is none.</param>
/// <param name="TimeUtc">When it was spotted, to the minute.</param>
/// <param name="Grid">A locator the node added after the time, or null.</param>
public sealed record ClusterSpot(string DxCall, long FrequencyHz, string Spotter, string Comment, DateTime TimeUtc, string? Grid = null);
