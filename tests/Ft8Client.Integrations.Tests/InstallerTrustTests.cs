// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Integrations.Updates;

namespace Ft8Client.Integrations.Tests;

public class InstallerTrustTests
{
    private const string Publisher = "Example Publisher";

    private static readonly InstallerSignature Unsigned = new(InstallerSignatureStatus.Unsigned, null);
    private static readonly InstallerSignature Ours = new(InstallerSignatureStatus.Valid, Publisher);
    private static readonly InstallerSignature Theirs = new(InstallerSignatureStatus.Valid, "Someone Else");
    private static readonly InstallerSignature Broken = new(InstallerSignatureStatus.Invalid, null, "WinVerifyTrust 0x80096010");

    [Fact]
    public void Refusal_ReleaseSignedByPublisher_Accepted() =>
        InstallerTrust.Refusal(Ours, development: false, Publisher).Should().BeNull();

    [Fact]
    public void Refusal_ReleaseUnsignedWithPublisherSet_Refused() =>
        InstallerTrust.Refusal(Unsigned, development: false, Publisher).Should().Contain("not signed");

    [Fact]
    public void Refusal_DevelopmentUnsignedWithPublisherSet_Accepted() =>
        InstallerTrust.Refusal(Unsigned, development: true, Publisher).Should().BeNull();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Refusal_SignedByAnotherPublisher_Refused(bool development) =>
        InstallerTrust.Refusal(Theirs, development, Publisher).Should().Contain("Someone Else");

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, Publisher)]
    [InlineData(true, Publisher)]
    public void Refusal_BrokenSignature_AlwaysRefused(bool development, string? publisher) =>
        InstallerTrust.Refusal(Broken, development, publisher).Should().Contain("not valid").And.Contain("0x80096010");

    [Fact]
    public void Refusal_PublisherNotSet_UnsignedAndAnyValidSignatureAccepted()
    {
        InstallerTrust.Refusal(Unsigned, development: false, expectedPublisher: null).Should().BeNull();
        InstallerTrust.Refusal(Theirs, development: false, expectedPublisher: null).Should().BeNull();
    }

    [Fact]
    public void Refusal_PublisherDiffersOnlyInCase_Refused() =>
        InstallerTrust.Refusal(new InstallerSignature(InstallerSignatureStatus.Valid, Publisher.ToUpperInvariant()), false, Publisher).Should().NotBeNull();
}
