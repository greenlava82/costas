// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Text.RegularExpressions;
using Ft8Client.Data.Secrets;

namespace Ft8Client.App.Tests;

/// <summary>Tests never start the app on the developer's own credential store.</summary>
public partial class TestHostTests
{
    [Fact]
    public void Args_Default_UsesAnInMemoryStore()
    {
        TestHost.Args("x").Secrets.Should().BeOfType<InMemorySecretStore>();
        TestHost.Args("x").Secrets.Should().NotBeSameAs(TestHost.Args("x").Secrets, "each test has its own");
    }

    [Fact]
    public void AppHostStart_EveryTest_GoesThroughTestHost()
    {
        var folder = Path.Combine(TestPaths.RepoRoot, "tests");
        var offenders = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => StartWithoutTestHost().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(folder, f))
            .ToList();
        offenders.Should().BeEmpty("AppHost.StartAsync(TestHost.Args(dir)) keeps a test off the developer's saved passwords");
    }

    // A call to AppHost.StartAsync whose argument is anything but TestHost.Args.
    [GeneratedRegex(@"AppHost\.StartAsync\((?!\s*TestHost\.Args\()")]
    private static partial Regex StartWithoutTestHost();
}
