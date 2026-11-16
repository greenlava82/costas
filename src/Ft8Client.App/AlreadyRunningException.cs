// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.App;

/// <summary>Thrown at start-up when another copy of Costas is running on the same data folder; it has been asked to come forward.</summary>
public sealed class AlreadyRunningException(string root) : Exception($"Costas is already running with its data in {root}.");
