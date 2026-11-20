// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Diagnostics;
using System.Text.Json;

namespace Ft8Client.Audio.Backends;

/// <summary>
/// The PulseAudio (or PipeWire's PulseAudio service) sinks and sources, listed by <c>pactl</c>. ALSA shows only one
/// "pulse" device for all of them, so virtual devices made by other programs (an SDR client's DAX channels such as
/// RimuDAX's <c>rimu.dax.tx</c> and <c>rimu.dax.rx1</c>, or a loopback) could not be picked in Costas. Each one is
/// offered as its own device, with an id of <see cref="Prefix"/> followed by its PulseAudio name.
/// </summary>
internal static class PulseDevices
{
    /// <summary>The start of a PulseAudio device's id.</summary>
    public const string Prefix = "PulseAudio:";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>The PulseAudio name in an id, or null when the id is not a PulseAudio device's.</summary>
    public static string? NameOf(string id) =>
        id.StartsWith(Prefix, StringComparison.Ordinal) && id.Length > Prefix.Length ? id[Prefix.Length..] : null;

    /// <summary>Sources (input: true) or sinks, or an empty list when pactl is missing or no server answers.</summary>
    public static IReadOnlyList<AudioDevice> List(bool input)
    {
        var kind = input ? "sources" : "sinks";
        var json = Run($"--format=json list {kind}");
        if (json is not null && ParseJson(json) is { } parsed) return parsed;
        var shortList = Run($"list short {kind}");
        return shortList is null ? [] : ParseShort(shortList);
    }

    /// <summary>Devices from <c>pactl --format=json list sinks|sources</c>, or null when the text is not that JSON.</summary>
    public static IReadOnlyList<AudioDevice>? ParseJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<AudioDevice>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("name", out var n) || n.GetString() is not { Length: > 0 } name) continue;
                var description = e.TryGetProperty("description", out var d) ? d.GetString() : null;
                list.Add(Device(name, description));
            }
            return list;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Devices from <c>pactl list short sinks|sources</c>: tab-separated index, name, driver, format, state.</summary>
    public static IReadOnlyList<AudioDevice> ParseShort(string text)
    {
        var list = new List<AudioDevice>();
        foreach (var line in text.Split('\n'))
        {
            var cols = line.Split('\t');
            if (cols.Length >= 2 && cols[1].Trim() is { Length: > 0 } name) list.Add(Device(name, null));
        }
        return list;
    }

    private static AudioDevice Device(string name, string? description)
    {
        var label = string.IsNullOrWhiteSpace(description) || description == name ? name : $"{description} – {name}";
        return new AudioDevice(Prefix + name, $"{label} (PulseAudio)");
    }

    private static string? Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("pactl", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            // English, so the short list's columns and errors read the same everywhere.
            psi.Environment["LC_ALL"] = "C";
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(Timeout))
            {
                try { p.Kill(); } catch (InvalidOperationException) { }
                return null;
            }
            return p.ExitCode == 0 && output.Wait(Timeout) ? output.Result : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // pactl is not installed
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
