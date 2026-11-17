// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using Ft8Client.Core.Remote;

namespace Ft8Client.Core.Tests;

public class RemoteSignInTests
{
    private static readonly DateTime T0 = new(2026, 11, 17, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Verify_RightPassword_True()
    {
        var stored = PasswordHash.Hash("correct horse battery");
        PasswordHash.Verify("correct horse battery", stored).Should().BeTrue();
        stored.Should().StartWith("pbkdf2-sha256$210000$").And.NotContain("correct");
    }

    [Theory]
    [InlineData("Correct horse battery")]
    [InlineData("correct horse batter")]
    [InlineData("")]
    [InlineData(null)]
    public void Verify_WrongPassword_False(string? password)
    {
        PasswordHash.Verify(password, PasswordHash.Hash("correct horse battery")).Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedOrMalformedHash_False()
    {
        var stored = PasswordHash.Hash("correct horse battery");
        var parts = stored.Split('$');
        PasswordHash.Verify("correct horse battery", string.Join('$', parts[0], parts[1], parts[2], Convert.ToBase64String(new byte[32]))).Should().BeFalse();
        PasswordHash.Verify("correct horse battery", stored[..^4]).Should().BeFalse();
        PasswordHash.Verify("correct horse battery", "sha1$1$abc$def").Should().BeFalse();
        PasswordHash.Verify("correct horse battery", string.Join('$', parts[0], "1", parts[2], parts[3])).Should().BeFalse("too few rounds is not accepted");
        PasswordHash.Verify("correct horse battery", null).Should().BeFalse();
    }

    [Fact]
    public void Hash_SamePasswordTwice_DifferentSalt()
    {
        PasswordHash.Hash("correct horse battery").Should().NotBe(PasswordHash.Hash("correct horse battery"));
    }

    [Theory]
    [InlineData("short", true)]
    [InlineData("123456789", true)]
    [InlineData("1234567890", false)]
    [InlineData(null, true)]
    public void Problem_TooShort_Explained(string? password, bool problem)
    {
        (PasswordHash.Problem(password) is not null).Should().Be(problem);
    }

    [Fact]
    public void Verify_UnusedFor30Days_SignedOut()
    {
        var s = new UserSessions();
        var token = s.Issue(T0);
        s.Verify(token, T0.AddDays(29)).Should().BeTrue();
        s.Verify(token, T0.AddDays(30)).Should().BeFalse();
        s.Verify("00" + token[2..], T0).Should().BeFalse();
    }

    [Fact]
    public void Touch_UsedAgain_LifetimeRestartsAndSavedOncePerDay()
    {
        var s = new UserSessions();
        var token = s.Issue(T0);
        s.Touch(token, T0.AddHours(1)).Should().BeFalse("still the same day: nothing to save");
        s.Touch(token, T0.AddDays(20)).Should().BeTrue();
        s.Verify(token, T0.AddDays(45)).Should().BeTrue();
        s.Verify(token, T0.AddDays(50)).Should().BeFalse();
    }

    [Fact]
    public void Remove_SignedOut_OtherDevicesStay()
    {
        var s = new UserSessions();
        var a = s.Issue(T0);
        var b = s.Issue(T0);
        s.Remove(a).Should().BeTrue();
        s.Verify(a, T0).Should().BeFalse();
        s.Verify(b, T0).Should().BeTrue();
    }

    [Fact]
    public void Issue_PastTheCap_ForgetsLeastRecentlyUsed()
    {
        var s = new UserSessions();
        var first = s.Issue(T0);
        var second = s.Issue(T0);
        s.Touch(first, T0.AddDays(1));
        for (var i = 2; i < UserSessions.MaxDevices; i++) s.Issue(T0.AddDays(1));
        s.Issue(T0.AddDays(2));
        s.Count.Should().Be(UserSessions.MaxDevices);
        s.Verify(second, T0.AddDays(2)).Should().BeFalse();
        s.Verify(first, T0.AddDays(2)).Should().BeTrue();
    }

    [Fact]
    public void Parse_Saved_RoundTripsAndDropsJunk()
    {
        var s = new UserSessions();
        var token = s.Issue(T0);
        var copy = UserSessions.Parse(s.Serialize() + ",junk,ABC:1," + new string('A', 64) + ":x");
        copy.Count.Should().Be(1);
        copy.Verify(token, T0).Should().BeTrue();
        copy.Prune(T0.AddDays(31)).Should().BeTrue();
        copy.Count.Should().Be(0);
    }

    [Fact]
    public void Wait_FiveFailures_ThenLockoutDoublesToCap()
    {
        var t = new LoginThrottle();
        for (var i = 0; i < LoginThrottle.FreeFailures; i++) t.Failed("dan", "10.0.0.2", T0);
        t.Wait("dan", "10.0.0.2", T0).Should().Be(TimeSpan.Zero);
        t.Failed("dan", "10.0.0.2", T0);
        t.Wait("dan", "10.0.0.2", T0).Should().Be(TimeSpan.FromSeconds(30));
        t.Failed("dan", "10.0.0.2", T0);
        t.Wait("dan", "10.0.0.2", T0).Should().Be(TimeSpan.FromSeconds(60));
        for (var i = 0; i < 20; i++) t.Failed("dan", "10.0.0.2", T0);
        t.Wait("dan", "10.0.0.2", T0).Should().Be(LoginThrottle.MaxLockout);
        t.Wait("dan", "10.0.0.2", T0 + LoginThrottle.MaxLockout).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Wait_ManyUsernamesFromOneAddress_AddressLockedOut()
    {
        var t = new LoginThrottle();
        for (var i = 0; i <= LoginThrottle.FreeFailures; i++) t.Failed("user" + i, "203.0.113.7", T0);
        t.Wait("someone", "203.0.113.7", T0).Should().BeGreaterThan(TimeSpan.Zero);
        t.Wait("someone", "10.0.0.2", T0).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Succeeded_AfterFailures_Cleared()
    {
        var t = new LoginThrottle();
        for (var i = 0; i <= LoginThrottle.FreeFailures; i++) t.Failed("Dan", "10.0.0.2", T0);
        t.Succeeded("dan", "10.0.0.2");
        t.Wait("DAN", "10.0.0.2", T0).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Read_NoHeaders_Direct()
    {
        var p = ProxyHeaders.Read(IPAddress.Parse("192.168.1.20"), null, null, null, null);
        p.Proxied.Should().BeFalse();
        p.Client.Should().Be(IPAddress.Parse("192.168.1.20"));
    }

    [Fact]
    public void Read_LocalProxy_TakesNearestAddressSchemeAndHost()
    {
        var p = ProxyHeaders.Read(IPAddress.Loopback, "1.2.3.4, 203.0.113.7", "https", "radio.example.org", null);
        p.Proxied.Should().BeTrue();
        p.Client.Should().Be(IPAddress.Parse("203.0.113.7"), "the first address can be made up by the browser");
        p.Proto.Should().Be("https");
        p.Host.Should().Be("radio.example.org");
    }

    [Fact]
    public void Read_ForwardedHeader_Parsed()
    {
        var p = ProxyHeaders.Read(IPAddress.Parse("10.0.0.5"), null, null, null, "for=\"[2001:db8::1]:4711\";proto=HTTPS;host=radio.example.org");
        p.Client.Should().Be(IPAddress.Parse("2001:db8::1"));
        p.Proto.Should().Be("https");
        p.Host.Should().Be("radio.example.org");
    }

    [Fact]
    public void Read_HeadersFromOutsideNetwork_NotBelieved()
    {
        var p = ProxyHeaders.Read(IPAddress.Parse("203.0.113.7"), "192.168.1.2", "https", "radio.example.org", null);
        p.Proxied.Should().BeTrue();
        p.Client.Should().Be(IPAddress.Parse("203.0.113.7"));
        p.Proto.Should().BeNull();
    }
}
