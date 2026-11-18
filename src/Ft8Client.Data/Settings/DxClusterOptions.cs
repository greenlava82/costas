// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
namespace Ft8Client.Data.Settings;

/// <summary>DX cluster spots (docs/05-integrations.md 5b). Off until the operator picks a node.</summary>
public sealed class DxClusterOptions
{
    /// <summary>Connect to the node and show its spots beside the stations heard. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>The node's host name or address. Empty means none chosen: nothing connects.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The node's telnet port.</summary>
    public int Port { get; set; } = 7300;
}
