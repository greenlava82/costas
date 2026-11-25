// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Mqtt;

/// <summary>Why an on-air session ended, as published to the MQTT broker.</summary>
public enum SessionEndReason
{
    /// <summary>No transmission for the idle timeout.</summary>
    IdleTimeout,

    /// <summary>The session reached its maximum length and was ended as a failsafe.</summary>
    MaxLength,

    /// <summary>The radio connection was lost.</summary>
    RigLost,

    /// <summary>Costas closed cleanly.</summary>
    AppClosed,
}
