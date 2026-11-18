// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using Ft8Client.Core.Bands;
using Ft8Client.Core.Contacts;
using Ft8Client.Core.Geo;
using Ft8Client.Core.Hunting;
using Ft8Client.Core.Messages;
using Ft8Client.Core.Ranking;

namespace Ft8Client.Core.Notifications;

/// <summary>The words of desktop notifications.</summary>
public static class NotificationText
{
    /// <summary>
    /// A Hunt list sighting. Title "VP8PJ on 20 m FT8"; text "New country Falkland Islands, heard here at -12 dB.",
    /// "Alaska (KL), heard 300 mi from you at -14 dB." or, for a call on the list, "On your Hunt list, heard here at -12 dB.".
    /// </summary>
    public static AttentionEvent Hunt(HuntSighting s, CountryFile? countries, DistanceUnit units)
    {
        var h = s.Heard;
        var call = Callsign.Normalize(h.Call);
        var title = $"{call} on {BandPlan.Display(h.Band)} {h.ModeKey}";
        var what = s.Target.Kind == HuntTargetKind.Call ? "On your Hunt list" : HuntText.Label(s, countries);
        return new AttentionEvent(AttentionKind.Hunt, call, title, $"{what}, {HuntText.HeardWhere(h, units)}.", title);
    }

    /// <summary>
    /// A station calling me. Title "W1ABC is calling you"; text "20 m FT8 · -12 dB · W7LIT W1ABC -12". A station replying
    /// after I stopped its contact: "W1ABC replied after you stopped".
    /// </summary>
    /// <param name="w">The caller.</param>
    /// <param name="band">ADIF band I am on, lower case.</param>
    /// <param name="modeKey">The mode: "FT8", "FT4", ...</param>
    public static AttentionEvent Caller(WaitingCaller w, string band, string modeKey)
    {
        var call = Callsign.Normalize(w.Call);
        var title = w.AfterStop ? $"{call} replied after you stopped" : $"{call} is calling you";
        var db = w.Snr.ToString("+0;-0", CultureInfo.InvariantCulture);
        var body = $"{BandPlan.Display(band)} {modeKey} · {db} dB" + (w.LastMessage is { Length: > 0 } m ? $" · {m}" : string.Empty);
        return new AttentionEvent(AttentionKind.Caller, call, title, body, call);
    }
}
