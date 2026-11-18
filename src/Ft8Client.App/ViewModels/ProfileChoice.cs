// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.App.ViewModels;

/// <summary>One entry in a remote user's "Operates as" list: a station profile, or none (whichever is in use).</summary>
/// <param name="Id">The profile's id, or null for none.</param>
/// <param name="Label">"Ash (AB1CD)", or "The profile in use".</param>
public sealed record ProfileChoice(string? Id, string Label)
{
    /// <summary>The entry for a user who operates as whichever profile is in use.</summary>
    public const string NoneLabel = "The profile in use";

    /// <inheritdoc />
    public override string ToString() => Label;
}
