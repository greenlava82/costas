// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Integrations.Wsprnet;

/// <summary>How WSPRnet answered one spot.</summary>
/// <param name="Accepted">True when WSPRnet took it.</param>
/// <param name="Retry">True when the spot should be sent again later (network or server trouble).</param>
/// <param name="Message">What to show when it was not accepted; empty on success.</param>
public sealed record WsprnetResult(bool Accepted, bool Retry, string Message)
{
    /// <summary>WSPRnet took the spot.</summary>
    public static WsprnetResult Ok { get; } = new(true, false, string.Empty);

    /// <summary>The spot was not delivered, and sending it again later may work.</summary>
    public static WsprnetResult TryLater(string message) => new(false, true, message);

    /// <summary>WSPRnet refused the spot; it is not sent again.</summary>
    public static WsprnetResult Refused(string message) => new(false, false, message);
}
