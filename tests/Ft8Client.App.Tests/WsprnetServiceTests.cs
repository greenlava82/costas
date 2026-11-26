// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.Audio.Backends;
using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Core.Wspr;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Data.Reference;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.Fakes;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

/// <summary>The WSPRnet upload service: off by default, never in simulation, and never in the way of the session.</summary>
public sealed class WsprnetServiceTests : IAsyncDisposable
{
    private static readonly DateTime Now = new(2026, 11, 26, 14, 4, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAlive;
    private readonly WsprSpotRepository _repo;
    private readonly ManualClock _clock = new(Now);
    private readonly FakeWsprnetServer _server = new();
    private readonly AppSettingsAccessor _settings;
    private readonly Session _session;
    private WsprnetService? _service;

    public WsprnetServiceTests()
    {
        _repo = new WsprSpotRepository(SqliteDatabase.InMemory(out _keepAlive));
        var path = Path.Combine(Directory.CreateTempSubdirectory("ft8wsprnet").FullName, "settings.json");
        var store = new SettingsStore(path);
        var s = store.Load();
        s.Profile.Callsign = "W7LIT";
        s.Profile.Grid = "DN40";
        _settings = new AppSettingsAccessor(store, s);
        var freqs = ReferenceData.LoadFrequencies();
        var tx = new Transmitter(new SimulatedRig(_clock), new NullAudioOutput { Now = () => _clock.UtcNow }, _clock, freqs);
        _session = new Session(new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", Mode = Mode.Wspr }, new NullDecoder("none"), new NoSource(), tx, _clock,
            ReferenceData.LoadCountries(), freqs);
    }

    public async ValueTask DisposeAsync()
    {
        if (_service is not null) await _service.DisposeAsync();
        _keepAlive.Dispose();
    }

    private void AddPending(string call, int minutesAgo = 2) =>
        _repo.Add(WsprSpotRow.From(new WsprSpot(Now.AddMinutes(-minutesAgo), -9, 0.1, 14_097_000, 0, call, "FN42", 30, false, 14_095_600), "W7LIT", null, WsprUploadState.Pending));

    private WsprnetService Make(bool allowed = true)
    {
        _service = new WsprnetService(_session, _settings, _repo, _clock, allowed, _server, wait: () => TimeSpan.FromMilliseconds(50));
        return _service;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Deadline;
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Start_UploadEnabled_SendsPendingSpotsAndShowsIndicator()
    {
        _settings.Update(s => s.Profile.Wspr.UploadEnabled = true);
        AddPending("K1ABC");
        AddPending("VK3XYZ", 4);

        Make().Start();
        await UntilAsync(() => _repo.PendingCount("W7LIT") == 0);

        _server.Requests.Select(r => r["tcall"]).Should().BeEquivalentTo("K1ABC", "VK3XYZ");
        await UntilAsync(() => _session.Snapshot.Services.TryGetValue(ServiceNames.Wsprnet, out var st) && st.Health == ServiceHealth.Ok);
    }

    [Fact]
    public async Task Start_UploadOff_SendsNothingAndIndicatorOff()
    {
        AddPending("K1ABC");

        Make().Start();
        await UntilAsync(() => _session.Snapshot.Services.ContainsKey(ServiceNames.Wsprnet));

        _server.Uris.Should().BeEmpty();
        _session.Snapshot.Services[ServiceNames.Wsprnet].Health.Should().Be(ServiceHealth.Off);
        _session.WsprUploadWanted().Should().BeFalse();
    }

    [Fact]
    public async Task Start_Simulation_NeverUploadsEvenWhenEnabled()
    {
        _settings.Update(s => s.Profile.Wspr.UploadEnabled = true);
        AddPending("K1ABC");

        Make(allowed: false).Start();
        await UntilAsync(() => _session.Snapshot.Services.ContainsKey(ServiceNames.Wsprnet));

        _server.Uris.Should().BeEmpty();
        _session.WsprUploadWanted().Should().BeFalse("simulated spots are never queued");
        _repo.PendingCount("W7LIT").Should().Be(1);
    }

    [Fact]
    public async Task Start_UploadEnabled_SessionAsksForSpotsToBeQueued()
    {
        _settings.Update(s => s.Profile.Wspr.UploadEnabled = true);
        Make().Start();

        _session.WsprUploadWanted().Should().BeTrue();
        _settings.Update(s => s.Profile.Wspr.UploadEnabled = false);
        _session.WsprUploadWanted().Should().BeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Start_ServerDown_SessionKeepsRunningAndSpotsStayQueued()
    {
        _settings.Update(s => s.Profile.Wspr.UploadEnabled = true);
        AddPending("K1ABC");
        _server.FailNext = 1000;

        Make().Start();
        await UntilAsync(() => _session.Snapshot.Services.TryGetValue(ServiceNames.Wsprnet, out var st) && st.Health == ServiceHealth.Degraded);

        _repo.PendingCount("W7LIT").Should().Be(1);
        _session.Snapshot.Wspr.Should().NotBeNull("reception is not touched by a failing upload");
        _session.Snapshot.Services[ServiceNames.Wsprnet].Message.Should().Contain("Next try");
    }

    [Fact]
    public async Task Start_NoGrid_SaysWhatIsMissing()
    {
        _settings.Update(s =>
        {
            s.Profile.Wspr.UploadEnabled = true;
            s.Profile.Grid = string.Empty;
        });
        AddPending("K1ABC");

        Make().Start();
        await UntilAsync(() => _session.Snapshot.Services.TryGetValue(ServiceNames.Wsprnet, out var st) && st.Health == ServiceHealth.Down);

        _server.Uris.Should().BeEmpty();
    }

    private sealed class NoSource : ISlotSource
    {
        public void BeginSlot(DateTime slotStartUtc, Mode mode)
        {
        }

        public void CancelSlot()
        {
        }

        public SlotAudio? Cutoff(DateTime slotStartUtc, Mode mode) => null;
    }
}
