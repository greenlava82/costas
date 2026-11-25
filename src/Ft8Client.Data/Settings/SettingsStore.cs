// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ft8Client.Data.Settings;

/// <summary>
/// Loads, migrates and saves <c>settings.json</c>. A save writes and flushes a temporary file, then swaps it in, keeping
/// the file it replaced as <c>settings.json.bak</c>. A file that cannot be read falls back to that backup, and is kept
/// as <c>settings.json.bad</c> (the last few bad files are kept, not overwritten).
/// </summary>
public sealed class SettingsStore(string path)
{
    /// <summary>Serializer options: camelCase, indented.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>How many earlier bad files are kept beside the latest (<c>.bad.1</c> to <c>.bad.3</c>).</summary>
    public const int BadCopiesKept = 3;

    /// <summary>Tries at a file that another program (an antivirus scan, a sync client) has open for a moment.</summary>
    private const int IoTries = 5;

    private static readonly TimeSpan IoRetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>The file path.</summary>
    public string Path { get; } = path;

    /// <summary>The backup kept by the last save: the settings as they were before it.</summary>
    public string BackupPath => Path + ".bak";

    /// <summary>
    /// What went wrong with the last <see cref="Load"/>, in words for the operator, or null when the file was read
    /// (or there was none yet).
    /// </summary>
    public string? LoadProblem { get; private set; }

    /// <summary>
    /// Loads settings. A missing file gives defaults. A file that cannot be read or understood is kept as
    /// <c>.bad</c> and the backup from the last save is used instead; with no usable backup, defaults.
    /// Never throws for a bad or locked file.
    /// </summary>
    public AppSettings Load()
    {
        LoadProblem = null;
        if (!File.Exists(Path)) return new AppSettings();
        if (TryRead(Path, out var settings, out var error)) return settings;

        KeepBadCopy();
        if (File.Exists(BackupPath) && TryRead(BackupPath, out var backup, out _))
        {
            LoadProblem = $"Settings could not be read ({error}). The copy from the previous save was used; the unreadable file is kept as {System.IO.Path.GetFileName(Path)}.bad.";
            return backup;
        }
        LoadProblem = $"Settings could not be read ({error}) and there was no usable backup, so defaults are in use. The unreadable file is kept as {System.IO.Path.GetFileName(Path)}.bad.";
        return new AppSettings();
    }

    /// <summary>
    /// Saves settings: the new file is flushed to disk before it replaces the old one, which becomes the backup.
    /// A file another program holds open for a moment is retried; throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> if it stays locked.
    /// </summary>
    public void Save(AppSettings settings)
    {
        settings.Schema = AppSettings.CurrentSchema;
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (dir is not null) Directory.CreateDirectory(dir);
        var tmp = Path + ".tmp";
        var json = JsonSerializer.Serialize(settings, Json);
        Retry(() =>
        {
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(f))
            {
                w.Write(json);
                w.Flush();
                f.Flush(flushToDisk: true);
            }
            if (File.Exists(Path)) File.Replace(tmp, Path, BackupPath, ignoreMetadataErrors: true);
            else File.Move(tmp, Path);
        });
    }

    private static bool TryRead(string file, out AppSettings settings, out string error)
    {
        settings = new AppSettings();
        error = string.Empty;
        try
        {
            string text = string.Empty;
            Retry(() => text = File.ReadAllText(file));
            var node = JsonNode.Parse(text) as JsonObject ?? throw new JsonException("the file is not a JSON object");
            Migrate(node);
            settings = node.Deserialize<AppSettings>(Json) ?? throw new JsonException("the file is empty");
            return true;
        }
        // Not JSON, a value of the wrong type (GetValue and the serializer throw InvalidOperationException or
        // FormatException), or a file that stayed locked.
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException
                                       or IOException or UnauthorizedAccessException)
        {
            error = ex is IOException or UnauthorizedAccessException ? "the file is locked or cannot be opened" : "the file is damaged";
            return false;
        }
    }

    // The latest bad file is .bad; earlier ones move up to .bad.1 .. .bad.N and the oldest is dropped.
    private void KeepBadCopy()
    {
        try
        {
            var bad = Path + ".bad";
            if (File.Exists(bad))
            {
                var oldest = $"{bad}.{BadCopiesKept}";
                if (File.Exists(oldest)) File.Delete(oldest);
                for (var i = BadCopiesKept - 1; i >= 1; i--)
                {
                    if (File.Exists($"{bad}.{i}")) File.Move($"{bad}.{i}", $"{bad}.{i + 1}");
                }
                File.Move(bad, $"{bad}.1");
            }
            File.Copy(Path, bad);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keeping a copy is a courtesy; a locked file is still not read.
        }
    }

    private static void Retry(Action io)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                io();
                return;
            }
            catch (Exception ex) when (ex is IOException and not FileNotFoundException and not DirectoryNotFoundException or UnauthorizedAccessException
                                       && attempt < IoTries)
            {
                Thread.Sleep(IoRetryDelay);
            }
        }
    }

    /// <summary>
    /// Brings older files up to <see cref="AppSettings.CurrentSchema"/>. Schema 0 (pre-release) had the profile's
    /// call and grid at the top level and no schema field. Schema 1 could hold band transmit levels walked down to
    /// silence by a misread ALC. Schema 2 had one LoTW account for every station profile, schema 3 one set of contest
    /// exchanges, schema 4 the development builds switch under developer options.
    /// </summary>
    public static void Migrate(JsonObject node)
    {
        var schema = node["schema"]?.GetValue<int>() ?? 0;
        if (schema < 1)
        {
            var call = node["callsign"]?.GetValue<string>();
            var grid = node["grid"]?.GetValue<string>();
            if (call is not null || grid is not null)
            {
                var profiles = node["profiles"] as JsonArray ?? [];
                if (profiles.Count == 0) profiles.Add(new JsonObject { ["id"] = "home", ["name"] = "Home" });
                var p = (JsonObject)profiles[0]!;
                p["callsign"] ??= call;
                p["grid"] ??= grid;
                node["profiles"] = profiles;
                node["activeProfile"] ??= p["id"]?.GetValue<string>() ?? "home";
            }
            node.Remove("callsign");
            node.Remove("grid");
            // Secrets must never live in settings; drop any a hand edit put there.
            foreach (var key in new[] { "qrzKey", "qrzPassword", "apiKey", "password" }) node.Remove(key);
            node["schema"] = 1;
        }
        if (schema < 2)
        {
            DropRunawayBandLevels(node);
            node["schema"] = 2;
        }
        if (schema < 3)
        {
            MoveLotwAccountIntoProfiles(node);
            node["schema"] = 3;
        }
        if (schema < 4)
        {
            MoveContestExchangesIntoProfiles(node);
            node["schema"] = 4;
        }
        if (schema < 5)
        {
            MoveDevelopmentBuildsToUpdates(node);
            node["schema"] = 5;
        }
    }

    // Schema 5: the development builds switch moved from the hidden developer options to Updates, where anyone can find it.
    // It used to count only while developer mode was on too, so it is carried over only then: a station that had turned
    // developer mode off was getting releases and keeps getting them.
    private static void MoveDevelopmentBuildsToUpdates(JsonObject node)
    {
        if (node["developer"] is not JsonObject developer) return;
        var wanted = developer["developmentBuilds"]?.GetValue<bool>() == true && developer["enabled"]?.GetValue<bool>() == true;
        developer.Remove("developmentBuilds");
        if (!wanted) return;
        if (node["updates"] is not JsonObject updates) node["updates"] = updates = new JsonObject();
        updates["developmentBuilds"] = true;
    }

    // Schema 4: the contest exchanges (state, class, section) moved from the operating settings into each station profile.
    // Every profile had been sending the same ones, so each gets a copy.
    private static void MoveContestExchangesIntoProfiles(JsonObject node)
    {
        if (node["operating"] is not JsonObject operating || operating["contestExchanges"] is not JsonObject shared) return;
        operating.Remove("contestExchanges");
        if (shared.Count == 0 || node["profiles"] is not JsonArray profiles) return;
        foreach (var p in profiles.OfType<JsonObject>())
            p["contestExchanges"] ??= shared.DeepClone();
    }

    // Schema 3: the LoTW account (login, Station Location, upload on log) moved from the shared LoTW settings into each
    // station profile, so another operator's profile can use their own. Every profile had been using the shared one, so
    // each gets a copy and nothing changes until the operator edits a profile. The TQSL path, download interval and user
    // list stay shared.
    private static readonly string[] LotwAccountKeys = ["enabled", "username", "stationLocation", "uploadOnComplete"];

    private static void MoveLotwAccountIntoProfiles(JsonObject node)
    {
        if (node["lotw"] is not JsonObject shared) return;
        var account = new JsonObject();
        foreach (var key in LotwAccountKeys)
        {
            if (shared[key] is { } v) account[key] = v.DeepClone();
            shared.Remove(key);
        }
        if (account.Count == 0 || node["profiles"] is not JsonArray profiles) return;
        foreach (var p in profiles.OfType<JsonObject>())
            p["lotw"] ??= account.DeepClone();
    }

    // Schema 2: before 0.1.0.336 a stale rigctld reply could read SWR as ALC, and automatic level walked a band's level
    // down to -40 dBFS, where the radio sends almost nothing. The fix does not lift a level already saved, so a band
    // level further below the operator's own than automatic level may now go is dropped: the band uses the operator's.
    private static void DropRunawayBandLevels(JsonObject node)
    {
        if (node["profiles"] is not JsonArray profiles) return;
        foreach (var p in profiles)
        {
            if (p?["audio"] is not JsonObject audio || audio["txGainByBand"] is not JsonObject bands) continue;
            var own = audio["txGainDb"] is JsonValue v && v.TryGetValue<double>(out var g) ? g : new AudioSettings().TxGainDb;
            var lowest = own - AudioSettings.AutoLowestBelowDb;
            foreach (var band in bands.Where(b => b.Value is JsonValue x && x.TryGetValue<double>(out var level) && level < lowest)
                         .Select(b => b.Key).ToList())
                bands.Remove(band);
        }
    }
}
