// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Blocking;

namespace Ft8Client.App.ViewModels;

/// <summary>One block list entry on the Settings page.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="Text">"ON4XYZ on 20 m in FT8".</param>
public sealed record BlockRowViewModel(BlockedCall Entry, string Text);
