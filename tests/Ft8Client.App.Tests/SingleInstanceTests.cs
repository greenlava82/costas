// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.App.Services;

namespace Ft8Client.App.Tests;

/// <summary>One copy of Costas per data folder (evaluation B13).</summary>
public class SingleInstanceTests
{
    [Fact]
    public void TryAcquire_SecondCopy_RefusedAndAsksFirstToShow()
    {
        var root = Directory.CreateTempSubdirectory("ft8one").FullName;
        using var first = SingleInstance.TryAcquire(root);
        var shown = new ManualResetEventSlim();
        first!.ShowRequested += shown.Set;

        var second = SingleInstance.TryAcquire(root);

        second.Should().BeNull();
        File.Exists(Path.Combine(root, SingleInstance.ShowFileName)).Should().BeTrue();
        shown.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue("the first copy is asked to come to the front");
    }

    [Fact]
    public void TryAcquire_AfterFirstDisposed_Succeeds()
    {
        var root = Directory.CreateTempSubdirectory("ft8one").FullName;
        SingleInstance.TryAcquire(root)!.Dispose();

        using var next = SingleInstance.TryAcquire(root);
        next.Should().NotBeNull("an update starts the new version once the old one has let go");
    }

    [Fact]
    public void TryAcquire_DifferentFolders_BothRun()
    {
        using var a = SingleInstance.TryAcquire(Directory.CreateTempSubdirectory("ft8one").FullName);
        using var b = SingleInstance.TryAcquire(Directory.CreateTempSubdirectory("ft8one").FullName);
        a.Should().NotBeNull();
        b.Should().NotBeNull();
    }
}
