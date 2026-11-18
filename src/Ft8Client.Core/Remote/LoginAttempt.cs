// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Remote;

/// <summary>
/// One password check reserved with <see cref="LoginThrottle.TryBegin"/>. It counts against the limit until it is
/// finished; disposing it without <see cref="Failed"/> or <see cref="Succeeded"/> releases it without counting a failure.
/// </summary>
public sealed class LoginAttempt : IDisposable
{
    private readonly LoginThrottle _throttle;

    internal LoginAttempt(LoginThrottle throttle, string username, string address)
    {
        _throttle = throttle;
        Username = username;
        Address = address;
    }

    internal string Username { get; }

    internal string Address { get; }

    // Read and written only under the throttle's lock.
    internal bool Finished { get; set; }

    /// <summary>Counts a wrong username or password and releases the reservation.</summary>
    public void Failed(DateTime nowUtc) => _throttle.Finish(this, false, nowUtc);

    /// <summary>Clears the count for this username and address and releases the reservation.</summary>
    public void Succeeded() => _throttle.Finish(this, true, default);

    /// <summary>Releases the reservation if it has not been finished.</summary>
    public void Dispose() => _throttle.Finish(this, null, default);
}
