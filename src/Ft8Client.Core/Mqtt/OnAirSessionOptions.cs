// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Mqtt;

/// <summary>Timing for the on-air session that MQTT subscribers (such as Home Assistant) use to switch loads.</summary>
/// <param name="IdleTimeout">Time with no transmission, after the last over ends, before the session ends.</param>
/// <param name="MaxLength">Longest a session may run before it is ended as a failsafe.</param>
public sealed record OnAirSessionOptions(TimeSpan IdleTimeout, TimeSpan MaxLength)
{
    /// <summary>Shortest allowed idle timeout.</summary>
    public static readonly TimeSpan MinIdle = TimeSpan.FromMinutes(1);

    /// <summary>Longest allowed idle timeout.</summary>
    public static readonly TimeSpan MaxIdle = TimeSpan.FromMinutes(60);

    /// <summary>Shortest allowed maximum session length.</summary>
    public static readonly TimeSpan MinLength = TimeSpan.FromMinutes(30);

    /// <summary>Longest allowed maximum session length.</summary>
    public static readonly TimeSpan MaxLengthLimit = TimeSpan.FromHours(12);

    /// <summary>The defaults: 5 minutes idle, 4 hours at most.</summary>
    public static OnAirSessionOptions Default { get; } = new(TimeSpan.FromMinutes(5), TimeSpan.FromHours(4));

    /// <summary>Builds options from minutes as stored in settings, clamped to the allowed ranges.</summary>
    public static OnAirSessionOptions FromMinutes(int idleMinutes, int maxMinutes) => new(
        Clamp(TimeSpan.FromMinutes(idleMinutes), MinIdle, MaxIdle),
        Clamp(TimeSpan.FromMinutes(maxMinutes), MinLength, MaxLengthLimit));

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) => value < min ? min : value > max ? max : value;
}
