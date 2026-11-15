// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Diagnostics;
using System.Text.Json;
using Serilog;

namespace Ft8Client.App.Remote;

/// <summary>
/// "Check why a phone can't connect" in Settings › Mobile connection. Looks at what Costas can see from this computer:
/// whether the page is served, whether it answers on the home-network address, the Windows network category (a Public
/// network is not covered by "Allow on private networks") and the Windows Firewall rules for Costas (closing the Windows
/// question leaves a Block rule). Changes nothing.
/// </summary>
public static class RemoteDiagnostics
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PowerShellTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Gathers the facts and puts them into words, one finding per line.</summary>
    public static async Task<IReadOnlyList<string>> RunAsync(RemoteControlService remote, bool enabled, int port, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(remote);
        var urls = remote.Urls();
        var answered = new List<string>();
        if (enabled && remote.Running)
        {
            using var http = new HttpClient { Timeout = ProbeTimeout };
            foreach (var url in urls)
            {
                try
                {
                    using var r = await http.GetAsync(url, ct).ConfigureAwait(false);
                    answered.Add(url); // any answer, even a refusal, means Costas is listening there
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    Log.Information("Phone check: no answer at {Url}: {Error}", url, ex.Message);
                }
            }
        }
        var (categories, rules) = OperatingSystem.IsWindows() ? await WindowsFactsAsync(ct).ConfigureAwait(false) : ([], null);
        return Explain(new RemoteCheckFacts
        {
            Enabled = enabled, Problem = remote.Problem, Port = port, Urls = urls, AnsweredHere = answered,
            NetworkCategories = categories, FirewallRules = rules,
        });
    }

    /// <summary>Puts the facts into words for the operator, one finding per line, problems first.</summary>
    public static IReadOnlyList<string> Explain(RemoteCheckFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!f.Enabled) return ["The phone connection is off. Tick \"Let a phone on this network operate the station\" and press Save, then check again."];
        if (f.Problem is { } p) return [p];
        var lines = new List<string>();
        if (f.Urls.Count == 0)
        {
            lines.Add("✗ This computer has no address on a home network. Connect it to your router by Wi-Fi or Ethernet.");
            return lines;
        }
        if (f.AnsweredHere.Count == 0)
            lines.Add($"✗ Costas does not answer at {f.Urls[0]} even from this computer. Security software may be stopping it, or another program may be using port {f.Port}: try another port.");
        else
            lines.Add($"✓ Costas is listening: it answers at {f.AnsweredHere[0]} from this computer.");

        foreach (var cat in f.NetworkCategories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var name = cat.StartsWith("Domain", StringComparison.OrdinalIgnoreCase) ? "Domain" : cat;
            if (string.Equals(name, "Public", StringComparison.OrdinalIgnoreCase))
                lines.Add("✗ Windows treats this computer's network as Public. Allowing Costas \"on private networks\" does not cover it. If this is your home network, make it Private: Windows Settings › Network & internet › your Wi-Fi or Ethernet › Properties › Private network.");
            if (f.FirewallRules is not { } rules) continue;
            var block = rules.FirstOrDefault(r => !r.Allow && r.Covers(cat));
            if (block is not null)
                lines.Add($"✗ Windows Firewall blocks Costas on {name} networks (rule \"{block.Name}\"). This happens when the Windows question about Costas was closed or cancelled. Open Windows Security › Firewall & network protection › Allow an app through firewall › Change settings, find Costas and tick Private (and untick nothing else), or delete the Block rule in Windows Defender Firewall › Inbound Rules.");
            else if (rules.Any(r => r.Allow && r.Covers(cat)))
                lines.Add($"✓ Windows Firewall lets Costas in on {name} networks.");
            else
                lines.Add($"✗ No Windows Firewall rule lets Costas in on {name} networks. Open Windows Security › Firewall & network protection › Allow an app through firewall › Change settings › Allow another app, choose Costas, and tick Private.");
        }
        lines.Add("On the phone: use the same Wi-Fi as this computer (a guest network usually keeps devices apart), turn off any VPN, and type the address exactly as shown, starting http:// (not https). Another firewall (Norton, McAfee, Bitdefender and the like) needs Costas allowed too.");
        return lines;
    }

    // Network categories and the firewall rules naming this program, from PowerShell. Empty and null when it cannot tell.
    private static async Task<(IReadOnlyList<string> Categories, IReadOnlyList<FirewallRule>? Rules)> WindowsFactsAsync(CancellationToken ct)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return ([], null);
        var script = string.Join("; ",
            "$ErrorActionPreference='SilentlyContinue'",
            "$exe='" + exe.Replace("'", "''", StringComparison.Ordinal) + "'",
            "$cats=@(Get-NetConnectionProfile | ForEach-Object { [string]$_.NetworkCategory })",
            "$rules=@(Get-NetFirewallApplicationFilter | Where-Object { [Environment]::ExpandEnvironmentVariables($_.Program) -ieq $exe } | Get-NetFirewallRule | Where-Object { [string]$_.Direction -eq 'Inbound' -and [string]$_.Enabled -eq 'True' } | ForEach-Object { [pscustomobject]@{ Name=$_.DisplayName; Action=[string]$_.Action; Profile=[string]$_.Profile } })",
            "ConvertTo-Json -Compress -Depth 3 -InputObject ([pscustomobject]@{ Categories=$cats; Rules=$rules })");
        try
        {
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) start.ArgumentList.Add(a);
            using var p = Process.Start(start);
            if (p is null) return ([], null);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(PowerShellTimeout);
            var output = await p.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return ParseWindowsFacts(output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            Log.Information("Phone check: could not read the firewall: {Error}", ex.Message);
            return ([], null);
        }
    }

    /// <summary>Reads the PowerShell check's JSON: <c>{"Categories":[...],"Rules":[{"Name","Action","Profile"}]}</c>.</summary>
    public static (IReadOnlyList<string> Categories, IReadOnlyList<FirewallRule>? Rules) ParseWindowsFacts(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var cats = Items(root, "Categories").Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
            var rules = Items(root, "Rules").Where(e => e.ValueKind == JsonValueKind.Object).Select(e => new FirewallRule(
                Text(e, "Name"), string.Equals(Text(e, "Action"), "Allow", StringComparison.OrdinalIgnoreCase), Text(e, "Profile"))).ToList();
            return (cats, rules);
        }
        catch (JsonException)
        {
            return ([], null);
        }
    }

    // ConvertTo-Json writes a one-item array as the item itself.
    private static IEnumerable<JsonElement> Items(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var v)) return [];
        return v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : v.ValueKind == JsonValueKind.Null ? [] : [v];
    }

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;
}
