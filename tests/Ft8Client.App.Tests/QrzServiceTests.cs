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
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Data.Reference;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.Fakes;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

public sealed class QrzServiceTests : IDisposable
{
    private const string LogbookKey = "ABCD-1234-ABCD-1234";
    private readonly string _dir = Directory.CreateTempSubdirectory("costas-qrzservice-").FullName;
    private readonly ManualClock _clock = new(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc));
    private readonly FakeQrzServer _server;
    private readonly InMemorySecretStore _secrets = new();
    private readonly Session _session;
    private readonly CallsignCacheRepository _cache;
    private readonly QsoRepository _qsos;
    private readonly UploadQueueRepository _queue;
    private readonly string _profileId;
    private readonly QrzService _svc;

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

    public QrzServiceTests()
    {
        _server = new FakeQrzServer(LogbookKey, "W7LIT") { Now = () => _clock.UtcNow };
        _server.AddLookup("ZL2RPA", "Bob Smith", "Wellington", null, "New Zealand", "RE78");
        _server.AddLookup("K1ABC", "Ann Jones", "Boston", "MA", "United States", "FN42");
        var freqs = ReferenceData.LoadFrequencies();
        var countries = ReferenceData.LoadCountries();
        var tx = new Transmitter(new SimulatedRig(_clock), new NullAudioOutput { Now = () => _clock.UtcNow }, _clock, freqs);
        _session = new Session(new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", Band = "20m", Mode = Mode.Ft8 }, new NoDecoder(),
            new NoAudio(), tx, _clock, countries, freqs);
        var s = new AppSettings();
        s.Profile.Callsign = "W7LIT";
        var settings = new AppSettingsAccessor(new SettingsStore(Path.Combine(_dir, "settings.json")), s);
        var db = new SqliteDatabase(Path.Combine(_dir, "log.db"));
        _cache = new CallsignCacheRepository(db);
        _qsos = new QsoRepository(db);
        _queue = new UploadQueueRepository(db);
        _profileId = s.Profile.Id;
        _svc = new QrzService(_session, settings, _secrets, _qsos, _queue, new SyncStateRepository(db),
            _cache, countries, _clock, _server)
        {
            Delay = (_, _) => Task.CompletedTask,
        };
        _secrets.Set(SecretNames.QrzUsername, "user");
        _secrets.Set(SecretNames.QrzPassword, "pass");
        _secrets.Set(SecretNames.QrzLogbookKey(_profileId), LogbookKey);
    }

    private void QueueContact(string call)
    {
        var id = _qsos.Insert(new QsoRecord
        {
            ProfileId = _profileId, Call = call, QsoDateOn = _clock.UtcNow, Band = "20m", Mode = "FT8", StationCallsign = "W7LIT",
            RstSent = "-10", RstRcvd = "-12", Source = QsoSource.Local,
        });
        _queue.Enqueue(id, "qrz", _clock.UtcNow);
    }

    public void Dispose()
    {
        _svc.Dispose();
        _server.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ServiceStatus QrzStatus => _session.Snapshot.Services[ServiceNames.Qrz];

    [Fact]
    public async Task LookupOneAsync_WrongPassword_ShowsDownAndStopsLoggingIn()
    {
        _secrets.Set(SecretNames.QrzPassword, "wrong");
        var ct = TestContext.Current.CancellationToken;

        await _svc.LookupOneAsync("ZL2RPA", ct);
        var asked = _server.RequestTimes.Count;
        await _svc.LookupOneAsync("K1ABC", ct);
        _clock.Advance(TimeSpan.FromMinutes(30));
        await _svc.LookupOneAsync("W1AW", ct);

        asked.Should().Be(1);
        _server.RequestTimes.Should().HaveCount(1, "QRZ refused these credentials, so no call logs in again with them");
        QrzStatus.Health.Should().Be(ServiceHealth.Down);
        QrzStatus.Message.Should().Contain("Username/password incorrect").And.NotContain("wrong");
    }

    [Fact]
    public async Task LookupOneAsync_PasswordChangedAfterRefusal_LooksUpAndRecovers()
    {
        _secrets.Set(SecretNames.QrzPassword, "wrong");
        var ct = TestContext.Current.CancellationToken;
        await _svc.LookupOneAsync("ZL2RPA", ct);

        _secrets.Set(SecretNames.QrzPassword, "pass");
        await _svc.LookupOneAsync("K1ABC", ct);

        _server.XmlLogins.Should().Be(1);
        _cache.GetFresh("K1ABC", _clock.UtcNow)!.Name.Should().Be("Ann Jones");
        QrzStatus.Health.Should().Be(ServiceHealth.Ok);
    }

    [Fact]
    public async Task LookupOneAsync_RefusedLoginPauseOver_TriesAgain()
    {
        _secrets.Set(SecretNames.QrzPassword, "wrong");
        var ct = TestContext.Current.CancellationToken;
        await _svc.LookupOneAsync("ZL2RPA", ct);

        _clock.Advance(QrzService.RefusedLoginPause);
        await _svc.LookupOneAsync("K1ABC", ct);

        _server.RequestTimes.Should().HaveCount(2);
    }

    [Fact]
    public async Task LookupOneAsync_ServerError_ShowsDegradedAndCachesNothing()
    {
        _server.LookupError = "Lookup limit reached";

        await _svc.LookupOneAsync("ZL2RPA", TestContext.Current.CancellationToken);

        QrzStatus.Health.Should().Be(ServiceHealth.Degraded);
        QrzStatus.Message.Should().Contain("Lookup limit reached");
        _cache.GetFresh("ZL2RPA", _clock.UtcNow).Should().BeNull();
    }

    [Fact]
    public async Task UploadDueAsync_FailedThenUploaded_ReportsOkAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        QueueContact("ZL2RPA");
        _server.FailNext = 1;
        await _svc.UploadDueAsync(ct);
        QrzStatus.Health.Should().Be(ServiceHealth.Degraded);

        _clock.Advance(TimeSpan.FromHours(1));
        await _svc.UploadDueAsync(ct);

        _queue.Count().Should().Be(0);
        QrzStatus.Health.Should().Be(ServiceHealth.Ok, "the upload went through, so the indicator no longer says retrying");
    }

    [Fact]
    public async Task UploadDueAsync_UploadedWhileLookupsRefused_StillShowsDown()
    {
        var ct = TestContext.Current.CancellationToken;
        _secrets.Set(SecretNames.QrzPassword, "wrong");
        await _svc.LookupOneAsync("K1ABC", ct);
        QueueContact("ZL2RPA");

        await _svc.UploadDueAsync(ct);

        _queue.Count().Should().Be(0);
        QrzStatus.Health.Should().Be(ServiceHealth.Down, "an upload working does not hide the refused lookup login");
    }
}
