// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Collections;
using System.Reflection;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit.v3;

[assembly: Ft8Client.App.Tests.UiThreadGuard]

namespace Ft8Client.App.Tests;

/// <summary>
/// Fails the run when a test creates an Avalonia object (a control, a brush) outside the headless session. That gives
/// the test's thread an Avalonia dispatcher, and while the session is starting a test's application (between resetting
/// the UI thread and setting it up again) such a dispatcher becomes the UI thread: the UI test running in the session at
/// that moment then fails with "The calling thread cannot access this object because a different thread owns it". It
/// happened about once in a hundred full runs on the Windows runner. After each test this looks for a dispatcher on any
/// thread but the session's, so the mistake shows up on nearly every run instead.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class UiThreadGuard : BeforeAfterTestAttribute
{
    // Avalonia keeps one dispatcher per thread in these (internal; if a later Avalonia renames them, the check is off).
    private static readonly FieldInfo? Dispatchers = typeof(Dispatcher).GetField("s_dispatchers", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo? GlobalLock = typeof(Dispatcher).GetField("s_globalLock", BindingFlags.NonPublic | BindingFlags.Static);

    // The session runs every UI job on one thread of its own, whose dispatcher is the real UI thread.
    private static readonly Lazy<Thread> SessionThread = new(() =>
    {
        Thread? t = null;
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiThreadGuard).Assembly)
            .Dispatch(() => t = Thread.CurrentThread, CancellationToken.None).GetAwaiter().GetResult();
        return t!;
    });

    /// <inheritdoc />
    // Learnt here, on a test thread: asking the session from its own thread (where an async test can end) would wait
    // for itself.
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) => _ = SessionThread.Value;

    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (Dispatchers?.GetValue(null) is not IEnumerable table || GlobalLock?.GetValue(null) is not { } gate) return;
        var others = new List<string>();
        lock (gate)
        {
            foreach (var entry in table)
            {
                if (entry.GetType().GetProperty("Key")?.GetValue(entry) is Thread t && t != SessionThread.Value)
                    others.Add($"{t.ManagedThreadId} ({t.Name})");
            }
        }
        if (others.Count > 0)
            throw new InvalidOperationException(
                $"A test (this one, or one that just ran on thread {string.Join(", ", others)}) created an Avalonia object " +
                "outside HeadlessUnitTestSession.Dispatch. Do that inside the session, or pass null where the code under " +
                "test does not use the control.");
    }
}
