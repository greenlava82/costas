// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using System.Security.Cryptography;

namespace Ft8Client.Core.Remote;

/// <summary>
/// Hashes the remote page's passwords with PBKDF2-SHA256 and a random salt, stored as
/// <c>pbkdf2-sha256$iterations$salt$hash</c> (salt and hash in base64). Only the hash is kept, never the password.
/// </summary>
public static class PasswordHash
{
    /// <summary>Shortest password accepted.</summary>
    public const int MinLength = 10;

    /// <summary>Longest password accepted, so a huge one cannot tie up the hashing.</summary>
    public const int MaxLength = 256;

    /// <summary>PBKDF2 rounds for new hashes (OWASP's figure for PBKDF2-HMAC-SHA256 is 600,000; this is about 0.1 s here).</summary>
    public const int Iterations = 210_000;

    private const string Scheme = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Why <paramref name="password"/> cannot be used, or null when it can.</summary>
    public static string? Problem(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinLength ? $"Use at least {MinLength} characters."
        : password.Length > MaxLength ? $"Use at most {MaxLength} characters."
        : null;

    /// <summary>Hashes a password with a new salt. Throws ArgumentException when <see cref="Problem"/> objects.</summary>
    public static string Hash(string password)
    {
        if (Problem(password) is { } why) throw new ArgumentException(why, nameof(password));
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join('$', Scheme, Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>True when <paramref name="password"/> matches <paramref name="stored"/>; false for anything malformed.</summary>
    public static bool Verify(string? password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || password.Length > MaxLength || string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations is < 10_000 or > 10_000_000)
            return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (salt.Length < 8 || expected.Length != HashBytes) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
