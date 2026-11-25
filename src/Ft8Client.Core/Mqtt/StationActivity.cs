// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>What the station is doing, as published in the state topic.</summary>
public enum StationActivity
{
    /// <summary>No radio connected, or not receiving.</summary>
    Idle,

    /// <summary>Receiving, not calling anyone.</summary>
    Listening,

    /// <summary>Calling CQ.</summary>
    CallingCq,

    /// <summary>In a contact.</summary>
    InQso,
}
