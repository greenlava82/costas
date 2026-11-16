// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.App.Remote;

/// <summary>What the phone connection check found, before it is put into words.</summary>
public sealed record RemoteCheckFacts
{
    /// <summary>The phone connection is turned on in Settings.</summary>
    public bool Enabled { get; init; }

    /// <summary>Why the page is not served although it is on (port taken), or null.</summary>
    public string? Problem { get; init; }

    /// <summary>The port.</summary>
    public int Port { get; init; }

    /// <summary>The home-network addresses a phone would open, most likely first.</summary>
    public IReadOnlyList<string> Urls { get; init; } = [];

    /// <summary>The addresses this computer could open itself, out of <see cref="Urls"/>.</summary>
    public IReadOnlyList<string> AnsweredHere { get; init; } = [];

    /// <summary>
    /// Windows network categories of the connected networks (<c>Private</c>, <c>Public</c>, <c>DomainAuthenticated</c>);
    /// empty when not known (not Windows, or the check failed).
    /// </summary>
    public IReadOnlyList<string> NetworkCategories { get; init; } = [];

    /// <summary>Windows Firewall inbound rules for this program; null when not known.</summary>
    public IReadOnlyList<FirewallRule>? FirewallRules { get; init; }
}
