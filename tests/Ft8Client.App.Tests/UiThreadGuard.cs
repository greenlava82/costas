// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Reflection;
using Avalonia.Threading;
using Xunit.v3;

[assembly: Ft8Client.App.Tests.UiThreadGuard]

namespace Ft8Client.App.Tests;

/// <summary>
/// Fails a test that creates an Avalonia object (a control, a brush) outside the headless session. That gives the
/// test's own thread an Avalonia dispatcher, and while the session is starting a test's application (between resetting
/// the UI thread and setting it up again) such a dispatcher becomes the UI thread: the UI test running in the session at
/// that moment then fails with "The calling thread cannot access this object because a different thread owns it". It
/// happened about once in a hundred full runs on the Windows runner; this makes the cause fail every time instead.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class UiThreadGuard : BeforeAfterTestAttribute
{
    [ThreadStatic]
    private static bool _hadDispatcher;

    /// <inheritdoc />
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) =>
        _hadDispatcher = Dispatcher.FromThread(Thread.CurrentThread) is not null;

    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!_hadDispatcher && Dispatcher.FromThread(Thread.CurrentThread) is not null)
            throw new InvalidOperationException(
                "This test created an Avalonia object outside HeadlessUnitTestSession.Dispatch. Do that inside the session, " +
                "or pass null where the code under test does not use the control.");
    }
}
