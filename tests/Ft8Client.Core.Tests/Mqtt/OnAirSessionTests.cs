// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Mqtt;
using Ft8Client.Core.Time;

namespace Ft8Client.Core.Tests.Mqtt;

/// <summary>The on-air session published to MQTT: once per operating session, never once per over.</summary>
public class OnAirSessionTests
{
    private static readonly DateTime T0 = new(2026, 11, 24, 18, 0, 0, DateTimeKind.Utc);

    private static (OnAirSession Session, ManualClock Clock, List<OnAirSnapshot> Changes) New(OnAirSessionOptions? options = null)
    {
        var clock = new ManualClock(T0);
        var n = 0;
        var s = new OnAirSession(clock, options ?? OnAirSessionOptions.Default, () => $"s{++n}");
        var changes = new List<OnAirSnapshot>();
        s.Changed += changes.Add;
        return (s, clock, changes);
    }

    /// <summary>One FT8 over: commit, key, 12.6 s of audio, unkey.</summary>
    private static void Over(OnAirSession s, ManualClock clock)
    {
        s.Commit();
        s.Ptt(true);
        clock.Advance(TimeSpan.FromSeconds(13));
        s.Ptt(false);
        clock.Advance(TimeSpan.FromSeconds(17));
        s.Tick();
    }

    [Fact]
    public void Snapshot_New_IdleWithNoSession()
    {
        var (s, _, changes) = New();

        s.Snapshot.Should().Be(OnAirSnapshot.Idle);
        changes.Should().BeEmpty();
    }

    [Fact]
    public void Commit_Idle_StartsSessionBeforePtt()
    {
        var (s, _, changes) = New();

        s.Commit();

        s.Snapshot.Active.Should().BeTrue();
        s.Snapshot.Transmitting.Should().BeFalse();
        s.Snapshot.SessionId.Should().Be("s1");
        s.Snapshot.SinceUtc.Should().Be(T0);
        changes.Should().ContainSingle();
    }

    [Fact]
    public void Ptt_KeyedWithNoCommit_StartsSession()
    {
        var (s, _, _) = New();

        s.Ptt(true);

        s.Snapshot.Active.Should().BeTrue();
        s.Snapshot.Transmitting.Should().BeTrue();
    }

    [Fact]
    public void Ptt_TwoHoursOfOvers_OneSessionOnly()
    {
        var (s, clock, changes) = New();

        for (var i = 0; i < 240; i++) Over(s, clock);

        s.Snapshot.Active.Should().BeTrue();
        s.Snapshot.SessionId.Should().Be("s1");
        changes.Count(c => !c.Active).Should().Be(0);
        changes.Select(c => c.SessionId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void Tick_IdleTimeoutAfterLastUnkey_EndsSession()
    {
        var (s, clock, _) = New();
        s.Commit();
        s.Ptt(true);
        clock.Advance(TimeSpan.FromSeconds(13));
        s.Ptt(false);

        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        s.Tick();
        s.Snapshot.Active.Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(1));
        s.Tick();
        s.Snapshot.Active.Should().BeFalse();
        s.Snapshot.EndReason.Should().Be(SessionEndReason.IdleTimeout);
        s.Snapshot.SessionId.Should().Be("s1");
    }

    [Fact]
    public void Tick_KeyedLongerThanIdleTimeout_SessionStays()
    {
        var (s, clock, _) = New(OnAirSessionOptions.FromMinutes(1, 240));
        s.Ptt(true);

        clock.Advance(TimeSpan.FromMinutes(10));
        s.Tick();

        s.Snapshot.Active.Should().BeTrue();
    }

    [Fact]
    public void Tick_WallClockJumps_SessionUnaffected()
    {
        var (s, clock, _) = New();
        s.Commit();

        clock.JumpWallClock(TimeSpan.FromHours(6));
        s.Tick();

        s.Snapshot.Active.Should().BeTrue();
    }

    [Fact]
    public void Tick_MaxLengthReached_EndsSession()
    {
        var (s, clock, _) = New(OnAirSessionOptions.FromMinutes(5, 30));

        for (var i = 0; i < 60; i++) Over(s, clock);

        s.Snapshot.Active.Should().BeFalse();
        s.Snapshot.EndReason.Should().Be(SessionEndReason.MaxLength);
    }

    [Fact]
    public void Tick_MaxLengthWhileKeyed_EndsOnlyAfterUnkey()
    {
        var (s, clock, _) = New(OnAirSessionOptions.FromMinutes(5, 30));
        s.Commit();
        clock.Advance(TimeSpan.FromMinutes(29.9));
        s.Ptt(true);
        clock.Advance(TimeSpan.FromMinutes(1));

        s.Tick();
        s.Snapshot.Active.Should().BeTrue();

        s.Ptt(false);
        s.Tick();
        s.Snapshot.Active.Should().BeFalse();
        s.Snapshot.EndReason.Should().Be(SessionEndReason.MaxLength);
    }

    [Fact]
    public void Commit_AfterSessionEnded_StartsNewSession()
    {
        var (s, _, _) = New();
        s.Commit();
        s.End(SessionEndReason.Halted);

        s.Commit();

        s.Snapshot.Active.Should().BeTrue();
        s.Snapshot.SessionId.Should().Be("s2");
        s.Snapshot.EndReason.Should().BeNull();
    }

    [Theory]
    [InlineData(SessionEndReason.Halted)]
    [InlineData(SessionEndReason.RigLost)]
    [InlineData(SessionEndReason.AppClosed)]
    public void End_Running_EndsAtOnceWithReason(SessionEndReason reason)
    {
        var (s, clock, _) = New();
        s.Commit();
        clock.Advance(TimeSpan.FromSeconds(20));

        s.End(reason);

        s.Snapshot.Active.Should().BeFalse();
        s.Snapshot.EndReason.Should().Be(reason);
        s.Snapshot.SinceUtc.Should().Be(T0.AddSeconds(20));
    }

    [Fact]
    public void End_NoSession_NoChange()
    {
        var (s, _, changes) = New();

        s.End(SessionEndReason.AppClosed);

        changes.Should().BeEmpty();
    }

    [Fact]
    public void Ptt_SameEdgeTwice_OneChange()
    {
        var (s, _, changes) = New();
        s.Ptt(true);

        s.Ptt(true);

        changes.Should().ContainSingle();
    }

    [Fact]
    public void Commit_DuringSession_PushesIdleTimeoutBack()
    {
        var (s, clock, _) = New();
        s.Commit();
        clock.Advance(TimeSpan.FromMinutes(4));

        s.Commit();
        clock.Advance(TimeSpan.FromMinutes(4));
        s.Tick();

        s.Snapshot.Active.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0, 1, 30)]
    [InlineData(5, 240, 5, 240)]
    [InlineData(999, 9999, 60, 720)]
    public void FromMinutes_OutOfRange_Clamped(int idle, int max, int wantIdle, int wantMax)
    {
        var o = OnAirSessionOptions.FromMinutes(idle, max);

        o.IdleTimeout.Should().Be(TimeSpan.FromMinutes(wantIdle));
        o.MaxLength.Should().Be(TimeSpan.FromMinutes(wantMax));
    }
}
