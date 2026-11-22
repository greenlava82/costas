// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia.Headless;

namespace Ft8Client.App.Tests;

/// <summary>
/// Starts the headless application before any test runs, so its session thread is Avalonia's UI thread from the start.
/// Otherwise a test that creates an Avalonia object on its own thread first would make that thread the UI thread.
/// </summary>
public sealed class UiSessionWarmup
{
    public UiSessionWarmup() =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiSessionWarmup).Assembly)
            .Dispatch(() => { }, CancellationToken.None).GetAwaiter().GetResult();
}
