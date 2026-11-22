// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Ft8Client.App.Tests.AppearanceTestAppBuilder))]
// One application for the whole assembly, started before any test runs (UiSessionWarmup). Per-test applications
// reset Avalonia's UI thread before each UI test, and in that moment the first dispatcher created on any thread becomes
// the UI thread: a test or a running app touching Avalonia on a background thread then made the next UI test fail with
// "The calling thread cannot access this object because a different thread owns it" (about once in a hundred runs on
// the Windows runner). With one application the UI thread is never reset, so no other thread can take it.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
[assembly: AssemblyFixture(typeof(Ft8Client.App.Tests.UiSessionWarmup))]

namespace Ft8Client.App.Tests;

public static class AppearanceTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
