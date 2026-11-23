// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Decoding;

/// <summary>Finds the <c>jt9</c> executable.</summary>
public static class Jt9Locator
{
    /// <summary>File name of the decoder on this platform.</summary>
    public static string ExecutableName => OperatingSystem.IsWindows() ? "jt9.exe" : "jt9";

    /// <summary>
    /// Returns the first existing candidate: the configured path, the bundled <c>third_party/wsjtx/bin</c>
    /// beside the app or in the repository, an installed WSJT-X, then the PATH. Null if none exists.
    /// </summary>
    public static string? Find(string? configuredPath = null) => Find(configuredPath, static _ => true);

    /// <summary>
    /// Returns the first existing candidate that <paramref name="usable"/> accepts, such as <see cref="Runs"/>, so a
    /// copy that cannot start (on Linux, the bundled jt9 on a system whose C library is too old for it) gives way to
    /// the next one, such as the distribution's. Null if none is accepted.
    /// </summary>
    public static string? Find(string? configuredPath, Func<string, bool> usable)
    {
        ArgumentNullException.ThrowIfNull(usable);
        foreach (var candidate in Candidates(configuredPath))
        {
            // An AppImage starts its whole program (WSJT-X's opens its main window), never jt9 alone.
            if (IsAppImage(candidate) || !File.Exists(candidate)) continue;
            var full = Path.GetFullPath(candidate);
            if (usable(full)) return full;
        }
        return null;
    }

    /// <summary>True when <paramref name="path"/> names an AppImage, which cannot be run as jt9.</summary>
    public static bool IsAppImage(string? path) =>
        path is not null && path.Trim().EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where <c>--appimage-extract</c>, run in the AppImage's own folder, puts jt9:
    /// <c>squashfs-root/usr/bin/jt9</c> beside it.
    /// </summary>
    public static string ExtractedFromAppImage(string appImagePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(appImagePath.Trim())) ?? ".", "squashfs-root", "usr", "bin", ExecutableName);

    /// <summary>
    /// True when this jt9 starts: the program loader found every library it needs. It is asked for its help text;
    /// a library that cannot be loaded makes the loader stop it at once (exit 127 on Linux), and a file that is not an
    /// executable for this computer cannot be started at all.
    /// </summary>
    public static bool Runs(string jt9Path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(jt9Path, "-h")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // Still running after the wait: it started, so its libraries loaded.
            if (Probe(psi) is not { } result) return true;
            if (result.ExitCode is 127 or DllNotFound or EntryPointNotFound) return false;
            return !result.Text.Contains("error while loading shared libraries", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when this jt9 has the multithreaded FT8 decoder (<c>-M</c>, WSJT-X 3.x). Probed from its help text.
    /// </summary>
    public static bool SupportsMultithread(string jt9Path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(jt9Path, "-h")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (Run(psi) is not { } text) return false;
            return text.Contains("--multithread", StringComparison.Ordinal) ||
                   System.Text.RegularExpressions.Regex.IsMatch(text, @"(?m)^\s*-M\b");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The WSJT-X version of this jt9, such as <c>WSJT-X 3.0.1</c>, read from <c>wsjtx_app_version -v</c> in the same
    /// folder (jt9 itself has no version option). Null when that program is missing or says nothing.
    /// </summary>
    public static string? Version(string jt9Path)
    {
        var dir = Path.GetDirectoryName(jt9Path) ?? ".";
        var tool = Path.Combine(dir, OperatingSystem.IsWindows() ? "wsjtx_app_version.exe" : "wsjtx_app_version");
        if (!File.Exists(tool)) return null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(tool, "-v")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (Run(psi) is not { } text) return null;
            var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return string.IsNullOrEmpty(line) ? null : line;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>All places looked at, in order.</summary>
    public static IEnumerable<string> Candidates(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (IsAppImage(configuredPath))
            {
                yield return ExtractedFromAppImage(configuredPath);
            }
            else if (Directory.Exists(configuredPath))
            {
                // The folder jt9 is in, or a WSJT-X folder: an installed one (bin) or an extracted AppImage (usr/bin).
                yield return Path.Combine(configuredPath, ExecutableName);
                yield return Path.Combine(configuredPath, "bin", ExecutableName);
                yield return Path.Combine(configuredPath, "usr", "bin", ExecutableName);
            }
            else
            {
                yield return configuredPath;
            }
        }

        var env = Environment.GetEnvironmentVariable("COSTAS_JT9") ?? Environment.GetEnvironmentVariable("FT8CLIENT_JT9");
        if (!string.IsNullOrWhiteSpace(env)) yield return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, "third_party", "wsjtx", "bin", ExecutableName);
        }

        if (OperatingSystem.IsWindows())
        {
            yield return @"C:\WSJT\wsjtx\bin\jt9.exe";
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(pf, "wsjtx", "bin", "jt9.exe");
        }
        else
        {
            yield return "/usr/bin/jt9";
            yield return "/usr/local/bin/jt9";
            yield return "/Applications/wsjtx.app/Contents/MacOS/jt9";
        }

        foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(p, ExecutableName);
        }
    }

    // Windows exit codes for a program whose DLL, or a function in one, is missing.
    private const int DllNotFound = unchecked((int)0xC0000135);
    private const int EntryPointNotFound = unchecked((int)0xC0000139);

    private static string? Run(System.Diagnostics.ProcessStartInfo psi) => Probe(psi)?.Text;

    private readonly record struct ProbeResult(int ExitCode, string Text);

    // Runs a probe for at most 5 s and returns its exit code and what it printed. Reading to the end before waiting
    // made the timeout useless: a probe that hung froze Costas's start-up.
    private static ProbeResult? Probe(System.Diagnostics.ProcessStartInfo psi)
    {
        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null) return null;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(5000))
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            return null;
        }
        return Task.WaitAll([output, error], 1000) ? new ProbeResult(p.ExitCode, output.Result + error.Result) : null;
    }
}
