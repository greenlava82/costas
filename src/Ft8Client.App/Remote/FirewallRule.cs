// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.App.Remote;

/// <summary>A Windows Firewall inbound rule that names the Costas program.</summary>
/// <param name="Name">The rule's display name.</param>
/// <param name="Allow">True for Allow, false for Block.</param>
/// <param name="Profiles">The network profiles it covers, as Windows names them (<c>Private</c>, <c>Public</c>, <c>Domain</c>, <c>Any</c>), comma-separated.</param>
public sealed record FirewallRule(string Name, bool Allow, string Profiles)
{
    /// <summary>True when the rule covers a network of this category (Private, Public or DomainAuthenticated).</summary>
    public bool Covers(string category)
    {
        var p = Profiles;
        if (p.Contains("Any", StringComparison.OrdinalIgnoreCase)) return true;
        var want = category.StartsWith("Domain", StringComparison.OrdinalIgnoreCase) ? "Domain" : category;
        return p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(x => string.Equals(x, want, StringComparison.OrdinalIgnoreCase));
    }
}
