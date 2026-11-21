// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.App.Engine;

/// <summary>
/// Raises an event one listener at a time, so a listener that throws is logged and the others still hear it. The
/// session raises its events in the middle of its own work (after a decode: the late transmit plan and the screen), and
/// often on a task nobody awaits: one listener's error, such as a locked database while it saved a statistic (Sentry
/// COSTAS-6), used to skip the rest of that work and the listeners after it, then surface later as an unobserved task
/// exception.
/// </summary>
public static class Listeners
{
    /// <summary>Calls each listener of <paramref name="handlers"/>.</summary>
    public static void Raise(Action? handlers, string name)
    {
        if (handlers is null) return;
        foreach (var h in handlers.GetInvocationList()) Call(name, () => ((Action)h)());
    }

    /// <summary>Calls each listener of <paramref name="handlers"/> with <paramref name="a"/>.</summary>
    public static void Raise<T>(Action<T>? handlers, T a, string name)
    {
        if (handlers is null) return;
        foreach (var h in handlers.GetInvocationList()) Call(name, () => ((Action<T>)h)(a));
    }

    /// <summary>Calls each listener of <paramref name="handlers"/> with <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static void Raise<T1, T2>(Action<T1, T2>? handlers, T1 a, T2 b, string name)
    {
        if (handlers is null) return;
        foreach (var h in handlers.GetInvocationList()) Call(name, () => ((Action<T1, T2>)h)(a, b));
    }

    /// <summary>Calls each listener of <paramref name="handlers"/> with three arguments.</summary>
    public static void Raise<T1, T2, T3>(Action<T1, T2, T3>? handlers, T1 a, T2 b, T3 c, string name)
    {
        if (handlers is null) return;
        foreach (var h in handlers.GetInvocationList()) Call(name, () => ((Action<T1, T2, T3>)h)(a, b, c));
    }

    private static void Call(string name, Action listener)
    {
        try
        {
            listener();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "A {Event} listener failed; the others still ran", name);
        }
    }
}
