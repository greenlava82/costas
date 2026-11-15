// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
namespace Ft8Client.Integrations.Crash;

/// <summary>
/// The error a test report carries (Settings, Privacy, Send a test report). Nothing went wrong: it exists so the test
/// shows up in Sentry under a name of its own.
/// </summary>
public sealed class CrashReportTestException : Exception
{
    /// <summary>Makes the test error.</summary>
    public CrashReportTestException()
        : base("Test report from Settings, Privacy. Nothing went wrong.")
    {
    }

    /// <summary>Makes the test error with a message.</summary>
    public CrashReportTestException(string message)
        : base(message)
    {
    }

    /// <summary>Makes the test error with a message and an inner error.</summary>
    public CrashReportTestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
