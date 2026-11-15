// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Integrations.ClubLog;

/// <summary>A real-time upload's outcome and Club Log's message, for the status line. Never holds a credential.</summary>
public sealed record ClubLogUploadResult(ClubLogOutcome Outcome, string Message);
