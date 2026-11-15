// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
namespace Ft8Client.Integrations.Crash;

/// <summary>What <see cref="CrashReporter.SendTestAsync"/> did.</summary>
public enum CrashReportTestOutcome
{
    /// <summary>No reporter is running (no DSN in this build, reports were off when Costas opened, or simulation).</summary>
    NotRunning,

    /// <summary>Reports are switched off now (or Costas is simulating).</summary>
    Off,

    /// <summary>Handed to Sentry and sent.</summary>
    Sent,

    /// <summary>The computer is offline: the report waits in the cache and goes out when it is back online.</summary>
    Waiting,
}
