// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.Audio.Backends;
using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Data.Reference;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.DxCluster;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

public sealed class DxClusterServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 18, 20, 15, 30, DateTimeKind.Utc);
    private readonly string _dir = Directory.CreateTempSubdirectory("costas-cluster-").FullName;
    private readonly TcpListener _node = new(IPAddress.Loopback, 0);

    private sealed class NoAudio : ISlotSource
    {
        public void BeginSlot(DateTime slotStartUtc, Mode mode)
        {
        }

        public void CancelSlot()
        {
        }

        public SlotAudio? Cutoff(DateTime slotStartUtc, Mode mode) => null;
    }

    private sealed class NoDecoder : IDecoder
    {
        public Task<DecodeResult> DecodeAsync(SlotAudio slot, DecodeContext ctx, CancellationToken ct) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        _node.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private (DxClusterService Service, Session Session, SpotRepository Spots, AppSettingsAccessor Settings) Create(bool enabled, string host = "127.0.0.1",
        Func<IReadOnlyList<string>>? replay = null)
    {
        var clock = new ManualClock(Now);
        var freqs = ReferenceData.LoadFrequencies();
        var countries = ReferenceData.LoadCountries();
        var tx = new Transmitter(new SimulatedRig(clock), new NullAudioOutput { Now = () => clock.UtcNow }, clock, freqs);
        var session = new Session(new SessionConfig { MyCall = "W1AW", MyGrid = "FN31", Band = "20m", Mode = Mode.Ft8 }, new NoDecoder(),
            new NoAudio(), tx, clock, countries, freqs);
        var s = new AppSettings();
        s.Profile.Callsign = "W1AW";
        s.DxCluster.Enabled = enabled;
        s.DxCluster.Host = host;
        var settings = new AppSettingsAccessor(new SettingsStore(Path.Combine(_dir, "settings.json")), s);
        var spots = new SpotRepository(new SqliteDatabase(Path.Combine(_dir, "log.db")));
        _node.Start();
        var port = ((IPEndPoint)_node.LocalEndpoint).Port;
        var service = new DxClusterService(session, settings, spots, clock, (h, _) => new ClusterFeedClient(h, port, () => clock.UtcNow), replay);
        return (service, session, spots, settings);
    }

    private Task ServeAsync(string lines) => Task.Run(async () =>
    {
        using var c = await _node.AcceptTcpClientAsync();
        var s = c.GetStream();
        await s.WriteAsync(Encoding.ASCII.GetBytes("login: "));
        await s.ReadAtLeastAsync(new byte[64], 1);
        await s.WriteAsync(Encoding.ASCII.GetBytes(lines));
        await Task.Delay(TimeSpan.FromSeconds(10));
    });

    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(50);
    }

    [Fact]
    public async Task Start_Off_StatusOffAndNoConnection()
    {
        var (service, session, _, _) = Create(enabled: false);
        await using var _ = service;
        service.Start();

        service.FollowedCall.Should().BeEmpty();
        session.Snapshot.Services[ServiceNames.Cluster].Health.Should().Be(ServiceHealth.Off);
    }

    [Fact]
    public async Task Start_OnWithoutNode_StaysOffAndSaysWhy()
    {
        var (service, session, _, _) = Create(enabled: true, host: " ");
        await using var _ = service;
        service.Start();

        service.FollowedCall.Should().BeEmpty();
        session.Snapshot.Services[ServiceNames.Cluster].Message.Should().Contain("No DX cluster node");
    }

    [Fact]
    public async Task Start_NodeSendsSpots_KeepsAndStoresThem()
    {
        var (service, session, spots, _) = Create(enabled: true);
        await using var owned = service;
        _ = ServeAsync(
            "DX de DL1NCH:     21074.0 OZ8BLR       FT8 -05dB from JO46 814Hz      2014Z\r\n" +
            "DX de HA5PP:       7039.6 P3AA                                        2014Z\r\n");
        service.Start();

        await Until(() => service.Current.Count == 2);

        service.FollowedCall.Should().Be("W1AW");
        service.Current.SpotFor("OZ8BLR", "15m", Now)!.Spotter.Should().Be("DL1NCH");
        var stored = spots.Since(Now.AddHours(-1)).Where(r => r.Source == DxClusterService.Source).ToList();
        stored.Should().HaveCount(2);
        stored.Single(r => r.MyCall == "OZ8BLR").Should().Match<SpotRecord>(r => r.RxCall == "DL1NCH" && r.Band == "15m" && r.Mode == "FT8");
        await Until(() => session.Snapshot.Services[ServiceNames.Cluster].Health == ServiceHealth.Ok);
        session.Snapshot.Services[ServiceNames.Cluster].Message.Should().Be("Connected to 127.0.0.1");
        session.Snapshot.Reports.Should().BeEmpty("cluster spots are not reports of my signal");
    }

    [Fact]
    public async Task SettingsChanged_TurnedOff_DropsSpotsAndStops()
    {
        var (service, session, _, settings) = Create(enabled: true, replay: () => ["DX de DL1NCH:     21074.0 OZ8BLR       FT8 -05dB from JO46 814Hz      1200Z"]);
        await using var _ = service;
        service.Start();
        service.Current.Count.Should().Be(1);

        settings.Update(s => s.DxCluster.Enabled = false);
        await service.Settled;

        service.Current.Count.Should().Be(0);
        session.Snapshot.Services[ServiceNames.Cluster].Health.Should().Be(ServiceHealth.Off);
    }

    [Fact]
    public async Task Start_Replay_UsesRecordedLinesRetimedAndStoresNothing()
    {
        var (service, session, spots, _) = Create(enabled: true, replay: () =>
        [
            "login: recorded",
            "DX de DL1NCH:     21074.0 OZ8BLR       FT8 -05dB from JO46 814Hz      0302Z",
            "DX de SV1YH:       7047.5 UX1MW        FT4 -06dB from KN98 2038Hz     0310Z",
        ]);
        await using var _ = service;
        service.Start();

        service.Current.SpotFor("UX1MW", "40m", Now)!.TimeUtc.Should().Be(Now.AddMinutes(-1));
        service.Current.SpotFor("OZ8BLR", "15m", Now)!.TimeUtc.Should().Be(Now.AddMinutes(-9));
        spots.Since(Now.AddDays(-2)).Should().BeEmpty();
        session.Snapshot.Services[ServiceNames.Cluster].Message.Should().StartWith("Simulated");
    }
}
