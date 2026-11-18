// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Neighbours;

/// <summary>
/// One band advice line: "15 m is open to Europe from your area" (docs/04-domain-logic.md, section 14a). Display only:
/// nothing changes band, ranking or who is called because of it.
/// </summary>
/// <param name="Kind">What the line says.</param>
/// <param name="Band">ADIF band, lower case ("15m").</param>
/// <param name="Continents">One or two continents, the strongest evidence first.</param>
/// <param name="NeededEntities">The receivers' countries I have no contact with on this band, most spots first.</param>
/// <param name="SampledFor">For a band listened to in turns, how long it was listened to in the last 15 minutes.</param>
public sealed record BandAdvice(BandAdviceKind Kind, string Band, IReadOnlyList<BandAdviceContinent> Continents, IReadOnlyList<string> NeededEntities,
                                TimeSpan? SampledFor = null);
