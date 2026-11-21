// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Globalization;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Services;

/// <summary>
/// The properties sent with usage events. Only choices and counts: never a callsign, grid, name, frequency, port,
/// address or anything from the log.
/// </summary>
public static class UsageEvents
{
    /// <summary>"app_started": how the station is set up and which optional features are on.</summary>
    public static Dictionary<string, object> Started(AppSettings s, bool decoderFound, StartFacts? facts = null)
    {
        var rig = s.Profile.Rig;
        var p = new Dictionary<string, object>
        {
            ["rig"] = rig.Mode,
            ["ptt"] = rig.Mode == RigModes.Hamlib ? rig.Ptt : "none",
            ["decoder"] = decoderFound ? "found" : "missing",
            ["setup_complete"] = OnOff(s.SetupComplete),
            ["mode"] = s.Operating.Mode,
            ["band"] = s.Operating.Band,
            ["theme"] = s.Appearance.Theme,
            ["units"] = s.Appearance.Units,
            ["qrz_logbook"] = OnOff(s.Qrz.LogbookEnabled),
            ["lotw"] = OnOff(s.Profile.Lotw.Enabled),
            ["eqsl"] = OnOff(s.Profile.Eqsl.UploadOnComplete || s.Profile.Eqsl.SyncEnabled),
            ["psk_feed"] = OnOff(s.PskReporter.FeedEnabled),
            ["psk_upload"] = OnOff(s.PskReporter.UploadSpots),
            ["rbn"] = OnOff(s.ReverseBeacon.Enabled),
            ["pota"] = OnOff(s.Pota.SpotsEnabled),
            ["dx_cluster"] = OnOff(s.DxCluster.Enabled),
            ["udp"] = OnOff(s.Udp.Enabled),
            ["remote"] = OnOff(s.Remote.Enabled),
            ["hunt_entries"] = s.Hunt.Targets.Count,
            ["auto_update"] = OnOff(s.Updates.CheckEnabled),
            ["layout"] = s.Appearance.Layout,
            ["profiles"] = s.Profiles.Count,
            ["remote_sign_in"] = OnOff(s.Remote.Enabled && s.Remote.RequireSignIn),
        };
        if (facts is not null)
        {
            p["install"] = facts.Install;
            p["channel"] = facts.Channel;
            p["install_age"] = UsageHistory.AgeBucket(facts.AgeDays);
            p["active_days_30"] = facts.ActiveDays30;
            p["features_tried"] = facts.FeaturesTried;
        }
        // The Hamlib model number says which radios Costas is used with; it is the same for every owner of that model.
        if (rig.Mode == RigModes.Hamlib && rig.Model is { } model) p["rig_model"] = model.ToString(CultureInfo.InvariantCulture);
        return p;
    }

    /// <summary>A band and mode, such as 20m and FT8.</summary>
    public static Dictionary<string, object> BandMode(string band, string mode) => new()
    {
        ["band"] = band,
        ["mode"] = mode,
    };

    /// <summary>How long a run lasted, as a range such as "30-60m".</summary>
    public static string RunLength(TimeSpan t) => t.TotalMinutes switch
    {
        < 5 => "<5m",
        < 30 => "5-30m",
        < 60 => "30-60m",
        < 120 => "1-2h",
        < 240 => "2-4h",
        < 480 => "4-8h",
        < 1440 => "8-24h",
        _ => "24h+",
    };

    /// <summary>A count capped at <paramref name="max"/>, so a rare large number cannot single out a station.</summary>
    public static int Capped(int n, int max = 100) => Math.Clamp(n, 0, max);

    /// <summary>
    /// The kind of device a remote page runs on, from its browser's User-Agent: phone, tablet or computer (an iPad that
    /// asks for desktop pages reads as a computer). Only the kind is kept, never the string.
    /// </summary>
    public static string DeviceKind(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        bool Has(string s) => ua.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("iPad") || Has("Tablet") || (Has("Android") && !Has("Mobile"))) return "tablet";
        if (Has("iPhone") || Has("Mobile") || Has("Android")) return "phone";
        return "computer";
    }

    private static string OnOff(bool on) => on ? "on" : "off";
}
