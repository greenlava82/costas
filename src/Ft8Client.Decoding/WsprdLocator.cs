// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Decoding;

/// <summary>Finds the <c>wsprd</c> executable, which WSJT-X ships beside <c>jt9</c>.</summary>
public static class WsprdLocator
{
    /// <summary>File name of the decoder on this platform.</summary>
    public static string ExecutableName => OperatingSystem.IsWindows() ? "wsprd.exe" : "wsprd";

    /// <summary>
    /// The configured <c>wsprd</c> when it exists, else the one beside <paramref name="jt9Path"/> (or beside the
    /// <c>jt9</c> that <see cref="Jt9Locator"/> finds). Null if none exists.
    /// </summary>
    public static string? Find(string? jt9Path = null, string? configuredWsprd = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredWsprd) && File.Exists(configuredWsprd)) return Path.GetFullPath(configuredWsprd);
        var jt9 = jt9Path ?? Jt9Locator.Find();
        if (jt9 is null) return null;
        var beside = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(jt9)) ?? ".", ExecutableName);
        return File.Exists(beside) ? beside : null;
    }
}
