// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Integrations.Updates;

/// <summary>
/// The rule for every address an update is read from (the release feed, checksums, installers, Velopack's packages): an
/// absolute <c>https</c> URL. Anything else, plain <c>http</c> included, is refused before a request is made. .NET does
/// not follow a redirect from <c>https</c> to <c>http</c>, so checking the address asked for is enough.
/// </summary>
public static class UpdateUrls
{
    /// <summary>True when <paramref name="url"/> is an absolute <c>https</c> URL.</summary>
    public static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && IsHttps(u);

    /// <summary>True when <paramref name="url"/> is an absolute <c>https</c> URL.</summary>
    public static bool IsHttps(Uri? url) =>
        url is { IsAbsoluteUri: true } && string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrEmpty(url.Host);

    /// <summary>
    /// <paramref name="url"/> as a <see cref="Uri"/>; throws <see cref="InvalidDataException"/> when it is not an absolute
    /// <c>https</c> URL.
    /// </summary>
    public static Uri RequireHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && IsHttps(u)
            ? u
            : throw new InvalidDataException($"Refused to download an update from a non-HTTPS address ({Describe(url)}).");

    // Enough to see what was refused in a log, without echoing a long string whole.
    private static string Describe(string? url) =>
        string.IsNullOrWhiteSpace(url) ? "no address" : url.Length > 80 ? url[..80] + "..." : url;
}
