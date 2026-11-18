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
/// keyboard-mode transmission going, and the operator is told why. Editing the callsign or grid of the profile in use is held
/// to the same rule (owner decision 2026-11-17). The same rule applies at the desk and from a phone.
/// </summary>
public static class ProfileSwitch
{
    /// <summary>
    /// What keeps the station busy, as "during the contact with JA1AAA as K1ABC", or null when it is idle.
    /// </summary>
    public static string? Busy(SessionSnapshot s)
    {
        var why = s.Transmitting || s.KeyboardTx.Active ? "while transmitting"
            : s.Contact is { Outcome: ContactOutcome.InProgress } c ? $"during the contact with {c.DxCall}"
            : s.CallingCq ? "while calling CQ"
            : s.AutoCall ? "while Auto-call is on"
            : null;
        if (why is null) return null;
        return s.MyCall.Length > 0 ? $"{why} as {s.MyCall}" : why;
    }

    /// <summary>
    /// Why <paramref name="what"/> (the station profile, or the callsign or grid of the one in use: all change what goes out)
    /// cannot change now, or null when it can. Worded for Settings.
    /// </summary>
    public static string? Refusal(SessionSnapshot s, string what = "the station profile") =>
        Busy(s) is { } busy ? $"Nothing was saved: {what} cannot change {busy}. Try again when the station is idle." : null;

    /// <summary>Why the desk's profile menu cannot switch profiles now, or null when it can.</summary>
    public static string? DeskRefusal(SessionSnapshot s) =>
        Busy(s) is { } busy ? $"The station profile cannot change {busy}. Try again when the station is idle." : null;

    /// <summary>
    /// Why a remote user's command cannot switch to their own profile <paramref name="profileName"/> now, or null when it
    /// can. Worded for the phone: the command was not carried out, and nothing went out under another callsign.
    /// </summary>
    public static string? RemoteRefusal(SessionSnapshot s, string profileName) =>
        Busy(s) is { } busy
            ? $"Not done: the station is busy {busy}, so it cannot switch to your {profileName} profile yet. Try again when the station is idle."
            : null;
}
