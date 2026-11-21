// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ft8Client.App.Services;

/// <summary>
/// What this computer remembers for the usage statistics, kept in its own small file (never sent as a whole, and holds no
/// callsign, grid, frequency or log content): the day Costas was first run with statistics on, the UTC days it ran
/// (the last 60), and which features have been tried at least once. From these the statistics send an install age, a
/// count of active days and a one-time "first use" event per feature, so adoption and retention can be seen without any
/// id that follows a person. Only updated while statistics are on.
/// </summary>
public sealed class UsageHistory
{
    /// <summary>How many days of activity are kept.</summary>
    public const int KeptDays = 60;

    /// <summary>The first UTC day seen, as yyyy-MM-dd.</summary>
    [JsonPropertyName("firstDay")]
    public string? FirstDay { get; set; }

    /// <summary>UTC days Costas ran on, yyyy-MM-dd, oldest first.</summary>
    [JsonPropertyName("activeDays")]
    public List<string> ActiveDays { get; set; } = [];

    /// <summary>Features used at least once (usage event names).</summary>
    [JsonPropertyName("featuresUsed")]
    public List<string> FeaturesUsed { get; set; } = [];

    /// <summary>Marks <paramref name="utcNow"/>'s day as active. Returns true when that changed anything.</summary>
    public bool Touch(DateTime utcNow)
    {
        var today = Day(utcNow);
        var changed = false;
        if (FirstDay is null || !TryDay(FirstDay, out _))
        {
            FirstDay = today;
            changed = true;
        }
        if (!ActiveDays.Contains(today, StringComparer.Ordinal))
        {
            ActiveDays.Add(today);
            changed = true;
        }
        var oldest = utcNow.Date.AddDays(-KeptDays);
        changed |= ActiveDays.RemoveAll(d => !TryDay(d, out var t) || t <= oldest) > 0;
        return changed;
    }

    /// <summary>Whole days since the first day seen (0 on the first day).</summary>
    public int AgeDays(DateTime utcNow) =>
        FirstDay is not null && TryDay(FirstDay, out var first) ? Math.Max(0, (int)(utcNow.Date - first).TotalDays) : 0;

    /// <summary>Days with a run in the 30 days up to and including today.</summary>
    public int ActiveDaysIn30(DateTime utcNow)
    {
        var from = utcNow.Date.AddDays(-29);
        return ActiveDays.Count(d => TryDay(d, out var t) && t >= from && t <= utcNow.Date);
    }

    /// <summary>Records that <paramref name="feature"/> was used. True the first time only.</summary>
    public bool FirstUse(string feature)
    {
        if (FeaturesUsed.Contains(feature, StringComparer.Ordinal)) return false;
        FeaturesUsed.Add(feature);
        return true;
    }

    /// <summary>The install age as a range, such as "8-30d".</summary>
    public static string AgeBucket(int days) => days switch
    {
        0 => "0d",
        <= 7 => "1-7d",
        <= 30 => "8-30d",
        <= 90 => "31-90d",
        <= 365 => "91-365d",
        _ => "365d+",
    };

    /// <summary>Reads the file, or a new history when it is missing or unreadable.</summary>
    public static UsageHistory Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<UsageHistory>(File.ReadAllText(path)) ?? new UsageHistory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Statistics are optional: start again.
        }
        return new UsageHistory();
    }

    /// <summary>Writes the file. Failures are ignored: statistics never get in the operator's way.</summary>
    public void Save(string path)
    {
        try
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Day(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryDay(string s, out DateTime day) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out day);
}
