// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Contacts;

namespace Ft8Client.App.Engine;

/// <summary>
/// When the active station profile may change (owner decision D25, 2026-11-17): only when the station is idle. A switch
/// changes the callsign that goes out, so it is refused while transmitting, in a contact, or with CQ, Auto-call or a
/// keyboard-mode transmission going, and the operator is told why. The same rule applies at the desk and from a phone.
/// </summary>
public static class ProfileSwitch
{
    /// <summary>Why the profile cannot change now under <paramref name="s"/>'s profile, or null when it can.</summary>
    public static string? Refusal(SessionSnapshot s)
    {
        var why = s.Transmitting || s.KeyboardTx.Active ? "while transmitting"
            : s.Contact is { Outcome: ContactOutcome.InProgress } c ? $"during the contact with {c.DxCall}"
            : s.CallingCq ? "while calling CQ"
            : s.AutoCall ? "while Auto-call is on"
            : null;
        if (why is null) return null;
        var under = s.MyCall.Length > 0 ? $" as {s.MyCall}" : string.Empty;
        return $"Nothing was saved: the station profile cannot change {why}{under}. Try again when the station is idle.";
    }
}
