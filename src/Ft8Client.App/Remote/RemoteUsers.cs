// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Collections.Concurrent;
using Ft8Client.Core.Remote;
using Ft8Client.Core.Time;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;
using Serilog;

namespace Ft8Client.App.Remote;

/// <summary>
/// Username and password sign-in for the remote page (<see cref="RemoteOptions.RequireSignIn"/>). The list of users
/// comes from settings; each user's password hash and signed-in devices are in the secret store under the user's id.
/// </summary>
public sealed class RemoteUsers
{
    // Checked when the username is unknown, so a wrong name takes as long to refuse as a wrong password.
    private static readonly Lazy<string> Decoy = new(() => PasswordHash.Hash(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))));

    private readonly Func<RemoteOptions> _options;
    private readonly ISecretStore _secrets;
    private readonly IClock _clock;
    private readonly LoginThrottle _throttle = new();
    private readonly ConcurrentDictionary<string, UserSessions> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _hasPassword = new(StringComparer.Ordinal);

    /// <summary>Creates the store.</summary>
    /// <param name="options">The current remote settings.</param>
    /// <param name="secrets">Where password hashes and sign-ins are kept.</param>
    /// <param name="clock">Clock, for the 30-day sign-in lifetime and the guessing limit.</param>
    public RemoteUsers(Func<RemoteOptions> options, ISecretStore secrets, IClock clock)
    {
        _options = options;
        _secrets = secrets;
        _clock = clock;
    }

    /// <summary>True when every device must sign in.</summary>
    public bool Required => _options().RequireSignIn;

    /// <summary>True when <paramref name="userId"/> has a password set.</summary>
    public bool HasPassword(string userId) => _hasPassword.GetOrAdd(userId, id => _secrets.Get(SecretNames.RemotePassword(id)) is not null);

    /// <summary>Who holds <paramref name="token"/>, or null when it is not a current sign-in of an enabled user.</summary>
    public RemoteIdentity? SignIn(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var now = _clock.UtcNow;
        foreach (var u in _options().Users)
        {
            if (!u.Enabled || !HasPassword(u.Id)) continue;
            var s = SessionsFor(u.Id);
            if (!s.Verify(token, now)) continue;
            if (s.Touch(token, now)) Save(u.Id, s);
            return new RemoteIdentity(RemoteIdentity.DeviceIdFor(token), u.Id, u.Username);
        }
        return null;
    }

    /// <summary>Checks a username and password from <paramref name="address"/> and signs the device in.</summary>
    public RemoteLogin Login(string? username, string? password, string address)
    {
        var now = _clock.UtcNow;
        var name = (username ?? string.Empty).Trim();
        if (name.Length is 0 or > 64) return RemoteLogin.Refused;
        var wait = _throttle.Wait(name, address, now);
        if (wait > TimeSpan.Zero)
        {
            Log.Warning("Remote sign-in for {User} from {Address} refused: too many wrong passwords", name, address);
            return RemoteLogin.TooMany(wait);
        }
        var user = _options().Users.FirstOrDefault(u => u.Enabled && string.Equals(u.Username.Trim(), name, StringComparison.OrdinalIgnoreCase));
        var stored = user is null ? null : _secrets.Get(SecretNames.RemotePassword(user.Id));
        var ok = PasswordHash.Verify(password, stored ?? Decoy.Value);
        if (user is null || stored is null || !ok)
        {
            _throttle.Failed(name, address, now);
            Log.Warning("Remote sign-in for {User} from {Address} refused: wrong username or password", name, address);
            return RemoteLogin.Refused;
        }
        _throttle.Succeeded(name, address);
        var s = SessionsFor(user.Id);
        s.Prune(now);
        var token = s.Issue(now);
        Save(user.Id, s);
        Log.Information("Remote sign-in: {User} from {Address}", user.Username, address);
        return new RemoteLogin(token, user.Username, TimeSpan.Zero);
    }

    /// <summary>Signs out the device holding <paramref name="token"/>.</summary>
    public void Logout(string? token)
    {
        foreach (var u in _options().Users)
        {
            var s = SessionsFor(u.Id);
            if (!s.Remove(token)) continue;
            Save(u.Id, s);
            Log.Information("Remote sign-out: {User}", u.Username);
            return;
        }
    }

    /// <summary>Sets a user's password and signs them out everywhere. Throws ArgumentException for a password too short.</summary>
    public void SetPassword(string userId, string password)
    {
        _secrets.Set(SecretNames.RemotePassword(userId), PasswordHash.Hash(password));
        _hasPassword[userId] = true;
        SignOutEverywhere(userId);
    }

    /// <summary>Signs a user out on every device.</summary>
    public void SignOutEverywhere(string userId)
    {
        SessionsFor(userId).Clear();
        _secrets.Delete(SecretNames.RemoteSessions(userId));
    }

    /// <summary>Deletes a removed user's password and sign-ins.</summary>
    public void Forget(string userId)
    {
        SignOutEverywhere(userId);
        _secrets.Delete(SecretNames.RemotePassword(userId));
        _hasPassword[userId] = false;
        _sessions.TryRemove(userId, out _);
    }

    private UserSessions SessionsFor(string userId) =>
        _sessions.GetOrAdd(userId, id => UserSessions.Parse(_secrets.Get(SecretNames.RemoteSessions(id))));

    private void Save(string userId, UserSessions s)
    {
        try
        {
            var text = s.Serialize();
            if (text.Length == 0) _secrets.Delete(SecretNames.RemoteSessions(userId));
            else _secrets.Set(SecretNames.RemoteSessions(userId), text);
        }
        catch (Exception ex)
        {
            // Signed in until Costas restarts; the device will have to sign in again after that.
            Log.Warning(ex, "Could not save the remote sign-ins");
        }
    }
}
