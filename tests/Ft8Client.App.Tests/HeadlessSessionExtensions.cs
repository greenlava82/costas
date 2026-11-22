// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia.Headless;

namespace Ft8Client.App.Tests;

internal static class HeadlessSessionExtensions
{
    /// <summary>
    /// Runs a UI test against a whole app, then stops the app before the session tears the test's UI down. A running app
    /// keeps raising events from its own threads (decodes, status), and the views answer them through
    /// Dispatcher.UIThread: once the session has reset the UI thread, the first such call makes that background thread
    /// the UI thread, and the next UI test fails with "a different thread owns it".
    /// </summary>
    public static Task DispatchThenStop(this HeadlessUnitTestSession session, AppHost host, Action test, CancellationToken ct) =>
        session.Dispatch<bool>(async () =>
        {
            try
            {
                test();
            }
            finally
            {
                await host.DisposeAsync();
            }
            return true;
        }, ct);
}
