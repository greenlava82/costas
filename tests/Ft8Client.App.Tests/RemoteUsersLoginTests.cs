// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using FluentAssertions;
using Ft8Client.App.Remote;
using Ft8Client.Core.Remote;
using Ft8Client.Core.Time;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

public sealed class RemoteUsersLoginTests
{
    private readonly ManualClock _clock = new(new DateTime(2026, 10, 12, 20, 31, 0, DateTimeKind.Utc));
    private readonly RemoteOptions _options = new() { RequireSignIn = true };
    private readonly InMemorySecretStore _secrets = new();

    [Fact]
    public async Task Login_ManyWrongGuessesAtOnce_OnlyTheAllowanceChecked()
    {
        var users = new RemoteUsers(() => _options, _secrets, _clock);
        AddUser(users, "dan", "correct horse battery");

        using var start = new ManualResetEventSlim();
        var guesses = Enumerable.Range(0, 40)
            .Select(i => Task.Run(() => { start.Wait(); return users.Login("dan", "wrong guess " + i, "203.0.113.7"); }))
            .ToArray();
        start.Set();
        var results = await Task.WhenAll(guesses);

        results.Count(r => r.Wait == TimeSpan.Zero).Should().BeLessThanOrEqualTo(LoginThrottle.FreeFailures + 1,
            "guesses still being checked count against the limit");
        results.Should().OnlyContain(r => r.Token == null);
    }

    [Fact]
    public async Task Login_GuessesBeyondTheLimitAtOnce_RefusedWithoutHashing()
    {
        using var gate = new ManualResetEventSlim();
        var hashes = 0;
        var users = new RemoteUsers(() => _options, _secrets, _clock,
            verify: (_, _) => { Interlocked.Increment(ref hashes); gate.Wait(); return false; },
            throttle: new LoginThrottle(maxConcurrent: 100));
        AddUser(users, "dan", "correct horse battery");

        var held = Enumerable.Range(0, LoginThrottle.FreeFailures + 1).Select(i => Guess(users, "dan", "203.0.113.7", i)).ToArray();
        await WaitUntil(() => Volatile.Read(ref hashes) == held.Length);

        var extra = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Guess(users, "dan", "203.0.113.7", i)));
        extra.Should().OnlyContain(r => r.Token == null && r.Wait == LoginThrottle.BusyWait, "refused at once, not queued");
        Volatile.Read(ref hashes).Should().Be(held.Length, "no password was checked for them");

        gate.Set();
        (await Task.WhenAll(held)).Should().OnlyContain(r => r == RemoteLogin.Refused);
        users.Login("dan", "correct horse battery", "203.0.113.7").Wait.Should().Be(LoginThrottle.FirstLockout);
    }

    [Fact]
    public async Task Login_ManyAddressesAtOnce_ConcurrencyCapHolds()
    {
        using var gate = new ManualResetEventSlim();
        int running = 0, most = 0, hashes = 0;
        var users = new RemoteUsers(() => _options, _secrets, _clock, verify: (_, _) =>
        {
            Interlocked.Increment(ref hashes);
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref most, now);
            gate.Wait();
            Interlocked.Decrement(ref running);
            return false;
        });
        AddUser(users, "dan", "correct horse battery");

        var held = Enumerable.Range(0, LoginThrottle.DefaultMaxConcurrent).Select(i => Guess(users, "dan", "198.51.100." + i, i)).ToArray();
        await WaitUntil(() => Volatile.Read(ref hashes) == held.Length);
        var extra = await Task.WhenAll(Enumerable.Range(10, 20).Select(i => Guess(users, "user" + i, "198.51.100." + i, i)));
        extra.Should().OnlyContain(r => r.Wait == LoginThrottle.BusyWait);

        gate.Set();
        await Task.WhenAll(held);
        most.Should().Be(LoginThrottle.DefaultMaxConcurrent);
        hashes.Should().Be(LoginThrottle.DefaultMaxConcurrent);
    }

    [Fact]
    public void Login_RightPasswordNotLocked_SignsIn()
    {
        var users = new RemoteUsers(() => _options, _secrets, _clock);
        AddUser(users, "Dan", "correct horse battery");
        for (var i = 0; i < LoginThrottle.FreeFailures; i++) users.Login("dan", "wrong guess " + i, "10.0.0.2").Should().Be(RemoteLogin.Refused);

        var r = users.Login("dan", "correct horse battery", "10.0.0.2");
        r.Token.Should().NotBeNull();
        r.Username.Should().Be("Dan");
        users.SignIn(r.Token)!.Username.Should().Be("Dan");
    }

    // Each guess on its own thread, so a held check does not starve the others of a pool thread.
    private static Task<RemoteLogin> Guess(RemoteUsers users, string username, string address, int i) =>
        Task.Factory.StartNew(() => users.Login(username, "wrong guess " + i, address), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("condition not met");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }

    private void AddUser(RemoteUsers users, string username, string password)
    {
        var user = new RemoteUser { Username = username };
        _options.Users.Add(user);
        users.SetPassword(user.Id, password);
    }
}
