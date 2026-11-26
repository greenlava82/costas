// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>The QRZ logbook sync as the qrz_sync sensor shows it.</summary>
public enum QrzSyncState
{
    /// <summary>Set up and nothing waiting.</summary>
    Ok,

    /// <summary>Contacts are waiting to upload.</summary>
    Pending,

    /// <summary>The last sync or upload failed.</summary>
    Error,

    /// <summary>The QRZ logbook is not set up.</summary>
    Disabled,
}
