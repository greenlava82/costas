// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Notifications;

/// <summary>What kind of "needs you" event asked for the operator's attention.</summary>
public enum AttentionKind
{
    /// <summary>A station on the Hunt list was heard.</summary>
    Hunt,

    /// <summary>A station called me (it appeared in Calling you), or replied after I stopped its contact.</summary>
    Caller,
}
