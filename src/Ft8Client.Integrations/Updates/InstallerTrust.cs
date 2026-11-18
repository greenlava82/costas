// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Integrations.Updates;

/// <summary>
/// Decides whether a downloaded installer may run, from its Authenticode signature. The SHA-256 check proves the file is
/// what the release lists; this proves the release was signed by Costas's publisher, so a release published by someone
/// who got hold of the GitHub repository (but not the signing account) is refused.
/// </summary>
/// <remarks>
/// A broken signature is always refused. Until <see cref="ExpectedPublisher"/> is set (code signing is not live yet),
/// any valid signature and unsigned installers are accepted, as before. Once it is set, a release installer must be
/// signed by that publisher; a development build may still be unsigned, but if it is signed it must be by that publisher.
/// </remarks>
public static class InstallerTrust
{
    /// <summary>
    /// The subject common name (CN) of the certificate Artifact Signing signs releases with, or null while signing is not
    /// set up. OWNER: set this to the CN shown on a signed release (Properties, Digital Signatures, Details, View
    /// Certificate, Subject; or <c>(Get-AuthenticodeSignature Costas-Setup-*.exe).SignerCertificate.Subject</c>) once
    /// scripts/sign.ps1 signs releases. It is the validated name on the Artifact Signing identity, so it stays the same
    /// as the short-lived certificates are renewed.
    /// </summary>
    public const string? ExpectedPublisher = null;

    /// <summary>
    /// Why <paramref name="signature"/> is not good enough to run the installer, or null when it may run.
    /// <paramref name="development"/> is a development build; <paramref name="expectedPublisher"/> is normally
    /// <see cref="ExpectedPublisher"/>.
    /// </summary>
    public static string? Refusal(InstallerSignature signature, bool development, string? expectedPublisher)
    {
        switch (signature.Status)
        {
            case InstallerSignatureStatus.Invalid:
                return $"The downloaded installer's signature is not valid{Because(signature.Detail)}.";
            case InstallerSignatureStatus.Unsigned:
                return development || expectedPublisher is null ? null : "The downloaded installer is not signed, so it was not run.";
            default:
                if (expectedPublisher is null) return null;
                return string.Equals(signature.Publisher, expectedPublisher, StringComparison.Ordinal)
                    ? null
                    : $"The downloaded installer is signed by \"{signature.Publisher}\", not by {expectedPublisher}, so it was not run.";
        }
    }

    private static string Because(string? detail) => string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail})";
}
