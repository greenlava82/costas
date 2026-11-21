// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;

namespace Ft8Client.App.Tests;

/// <summary>One listener that throws does not stop the others or the code that raised the event (Sentry COSTAS-6).</summary>
public class ListenersTests
{
    [Fact]
    public void Raise_FirstListenerThrows_OthersStillCalledAndNothingEscapes()
    {
        var heard = new List<string>();
        Action<int, string, double>? handlers = null;
        handlers += (_, _, _) => throw new InvalidOperationException("database is locked");
        handlers += (n, s, d) => heard.Add($"{n} {s} {d}");

        var raise = () => Listeners.Raise(handlers, 1, "a", 2.5, "Test");

        raise.Should().NotThrow();
        heard.Should().Equal("1 a 2.5");
    }

    [Fact]
    public void Raise_EveryArity_CallsEachListenerOnce()
    {
        var calls = 0;
        Action? none = null;
        Action zero = () => throw new InvalidOperationException();
        zero += () => calls++;
        Action<int> one = _ => calls++;
        Action<int, int> two = (_, _) => throw new InvalidOperationException();
        two += (_, _) => calls++;

        Listeners.Raise(none, "None");
        Listeners.Raise(zero, "Zero");
        Listeners.Raise(one, 1, "One");
        Listeners.Raise(two, 1, 2, "Two");

        calls.Should().Be(3);
    }
}
