// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Data.Secrets;

namespace Ft8Client.App.Tests;

/// <summary>
/// How a test starts the whole app: in its own data folder, replaying the FT8 samples, with an in-memory credential
/// store. The platform's store is the developer's own (Windows Credential Manager), and a test profile id such as
/// "home" is a real one too, so a test there could read and overwrite their saved QRZ, LoTW and logbook passwords.
/// </summary>
internal static class TestHost
{
    /// <summary>Command line for <see cref="AppHost.StartAsync"/> with <paramref name="home"/> as the data folder.</summary>
    public static CommandLine Args(string home, ISecretStore? secrets = null) => new()
    {
        Home = home,
        SimulateFolder = Path.Combine(TestPaths.Samples, "ft8"),
        Secrets = secrets ?? new InMemorySecretStore(),
    };
}
