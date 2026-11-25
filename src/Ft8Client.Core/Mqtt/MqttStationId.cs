// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Text;

namespace Ft8Client.Core.Mqtt;

/// <summary>Makes the station id used in MQTT topics and Home Assistant ids from a callsign.</summary>
public static class MqttStationId
{
    /// <summary>The id used when the callsign gives nothing usable.</summary>
    public const string Fallback = "costas";

    /// <summary>
    /// Lower case letters and digits, every other run of characters as one underscore, no leading or trailing
    /// underscore, at most 32 characters: "W7LIT/P" gives "w7lit_p".
    /// </summary>
    public static string From(string? callsign)
    {
        var sb = new StringBuilder();
        foreach (var c in callsign ?? string.Empty)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
            else if (c is >= 'A' and <= 'Z') sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
            if (sb.Length >= 32) break;
        }
        var id = sb.ToString().Trim('_');
        return id.Length == 0 ? Fallback : id;
    }
}
