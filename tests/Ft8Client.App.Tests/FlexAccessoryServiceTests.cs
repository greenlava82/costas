// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.App.ViewModels;
using Ft8Client.Audio.Backends;
using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Services;
using Ft8Client.Core.Time;
using Ft8Client.Data.Reference;
using Ft8Client.Data.Settings;
using Ft8Client.Rig;
using Ft8Client.Rig.Flex;
using Ft8Client.Rig.Tests.Flex;

namespace Ft8Client.App.Tests;

public sealed class FlexAccessoryServiceTests : IAsyncDisposable
{
    private const string PgxlStandby = "S0|amplifier 0x18E5F0AA ip=192.168.1.35 model=PowerGeniusXL serial_num=00-000/00-0000 ant=ANT1:PORTA,ANT2:NONE state=STANDBY";
    private const string Tgxl = "S0|amplifier 0x2C01D3B4 ip=192.168.1.34 model=TunerGeniusXL serial_num=000000-0 operate=1 bypass=0 tuning=0";

    private readonly string _dir = Directory.CreateTempSubdirectory("costas-flex-").FullName;
    private readonly FakeFlexRadio _radio = new();
    private bool _transmitting;

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

    public async ValueTask DisposeAsync()
    {
        await _radio.DisposeAsync();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private (FlexAccessoryService Service, Session Session, AppSettingsAccessor Settings) Create(int? model = 23005, string? port = "192.168.1.20:4992",
        bool simulating = false, string mode = RigModes.Hamlib)
    {
        var clock = new ManualClock(new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc));
        var freqs = ReferenceData.LoadFrequencies();
        var tx = new Transmitter(new SimulatedRig(clock), new NullAudioOutput { Now = () => clock.UtcNow }, clock, freqs);
        var session = new Session(new SessionConfig { MyCall = "W1AW", MyGrid = "FN31", Band = "20m", Mode = Mode.Ft8 }, new NoDecoder(),
            new NoAudio(), tx, clock, ReferenceData.LoadCountries(), freqs);
        var s = new AppSettings();
        s.Profile.Rig.Mode = mode;
        s.Profile.Rig.Model = model;
        s.Profile.Rig.Port = port;
        var settings = new AppSettingsAccessor(new SettingsStore(Path.Combine(_dir, "settings.json")), s);
        var service = new FlexAccessoryService(session, settings, () => _transmitting, simulating,
            (_, _) => new FlexAccessoryClient("127.0.0.1", _radio.Port, pingEvery: TimeSpan.FromMinutes(1)));
        service.Start();
        return (service, session, settings);
    }

    [Theory]
    [InlineData(23005, "192.168.1.20:4992", "192.168.1.20", 4992)]
    [InlineData(23012, " 10.0.0.5:5000 ", "10.0.0.5", 5000)]
    [InlineData(23006, "flex-8600.local", "flex-8600.local", 4992)]
    public void RadioAddress_SmartSdrSlice_ReadsHostAndPort(int model, string port, string host, int p)
    {
        FlexAccessoryService.RadioAddress(new RigSettings { Mode = RigModes.Hamlib, Model = model, Port = port }).Should().Be((host, p));
    }

    [Theory]
    [InlineData(RigModes.Hamlib, 2014, "COM5")] // Kenwood TS-2000 through SmartSDR CAT: no address for the radio
    [InlineData(RigModes.Hamlib, 2036, "127.0.0.1:5002")] // FlexRadio 6xxx: SmartSDR CAT's port, not the radio's API
    [InlineData(RigModes.Hamlib, 23005, "")]
    [InlineData(RigModes.Hamlib, 23005, "192.168.1.20:notaport")]
    [InlineData(RigModes.Vox, 23005, "192.168.1.20:4992")]
    public void RadioAddress_OtherRadios_Null(string mode, int model, string port)
    {
        FlexAccessoryService.RadioAddress(new RigSettings { Mode = mode, Model = model, Port = port }).Should().BeNull();
    }

    [Fact]
    public async Task Start_NotAFlex_OffAndNoConnection()
    {
        var (service, session, _) = Create(model: 2014, port: "COM5");
        await using var _ = service;
        await service.Settled;

        session.Snapshot.Services[ServiceNames.Accessories].Health.Should().Be(ServiceHealth.Off);
        _radio.Connections.Should().Be(0);
        AccessoryPanel.From(service.Current, service.CanChange).Any.Should().BeFalse();
    }

    [Fact]
    public async Task Start_Simulating_NoConnection()
    {
        var (service, session, _) = Create(simulating: true);
        await using var _ = service;
        await service.Settled;

        session.Snapshot.Services[ServiceNames.Accessories].Message.Should().Be("Not while replaying recordings");
        _radio.Connections.Should().Be(0);
    }

    [Fact]
    public async Task Start_SmartSdrSlice_ShowsDevicesAndSwitchesNothing()
    {
        _radio.OnSubscribe.AddRange([PgxlStandby, Tgxl]);
        var (service, session, _) = Create();
        await using var _ = service;

        await WaitUntil(() => service.Current.Amplifier is not null && service.Current.Tuner is not null);

        session.Snapshot.Services[ServiceNames.Accessories].Health.Should().Be(ServiceHealth.Ok);
        _radio.Commands.Should().Equal("sub amplifier all");
    }

    [Fact]
    public async Task SetAmpOperate_Standby_SendsOperate()
    {
        _radio.OnSubscribe.Add(PgxlStandby);
        var (service, _, _) = Create();
        await using var _ = service;
        await WaitUntil(() => service.Current.Amplifier is not null);

        var r = await service.SetAmpOperateAsync(true);

        r.Should().BeNull();
        _radio.Commands.Should().Contain("amplifier set 0x18E5F0AA operate=1");
    }

    [Fact]
    public async Task SetAmpOperate_WhileTransmitting_RefusedAndNothingSent()
    {
        _radio.OnSubscribe.Add(PgxlStandby);
        var (service, _, _) = Create();
        await using var _ = service;
        await WaitUntil(() => service.Current.Amplifier is not null);
        _transmitting = true;

        var r = await service.SetAmpOperateAsync(true);

        r.Should().Be(FlexAccessoryService.OnAir);
        _radio.Commands.Should().NotContain(c => c.StartsWith("amplifier set", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetAmpOperate_AmpSaysTransmitting_Refused()
    {
        _radio.OnSubscribe.AddRange([PgxlStandby, "S0|amplifier 0x18E5F0AA state=TRANSMIT_A"]);
        var (service, _, _) = Create();
        await using var _ = service;
        await WaitUntil(() => service.Current.Amplifier?.Transmitting == true);

        (await service.SetAmpOperateAsync(false)).Should().Be(FlexAccessoryService.OnAir);
        service.CanChange.Should().BeFalse();
    }

    [Fact]
    public async Task SetAmpOperate_StillStarting_Refused()
    {
        _radio.OnSubscribe.Add(PgxlStandby.Replace("state=STANDBY", "state=SELFCHECK", StringComparison.Ordinal));
        var (service, _, _) = Create();
        await using var _ = service;
        await WaitUntil(() => service.Current.Amplifier is not null);

        (await service.SetAmpOperateAsync(true)).Should().Be("The amplifier is still starting up.");
    }

    [Fact]
    public async Task SetAmpOperate_AlreadyThere_SendsNothing()
    {
        _radio.OnSubscribe.Add(PgxlStandby);
        var (service, _, _) = Create();
        await using var _ = service;
        await WaitUntil(() => service.Current.Amplifier is not null);

        (await service.SetAmpOperateAsync(false)).Should().BeNull();
        _radio.Commands.Should().Equal("sub amplifier all");
    }

    [Fact]
    public async Task SetTuner_OperateBypassAndStandby_SendTgxlCommands()
    {
        _radio.OnSubscribe.Add(Tgxl);
        var (service, _, _) = Create();
        await using var _ = service;
        await WaitUntil(() => service.Current.Tuner is not null);

        (await service.SetTunerBypassAsync(true)).Should().BeNull();
        (await service.SetTunerOperateAsync(false)).Should().BeNull();

        _radio.Commands.Should().Contain(["tgxl set handle=0x2C01D3B4 bypass=1", "tgxl set handle=0x2C01D3B4 mode=0"]);
    }

    [Fact]
    public async Task SetTuner_WhileTuning_Refused()
    {
        _radio.OnSubscribe.Add(Tgxl.Replace("tuning=0", "tuning=1", StringComparison.Ordinal));
        var (service, _, _) = Create();
        await using var _ = service;
        await WaitUntil(() => service.Current.Tuner is not null);

        (await service.SetTunerOperateAsync(false)).Should().Be("Wait until the tuner has finished tuning.");
        (await service.SetTunerBypassAsync(true)).Should().Be("Wait until the tuner has finished tuning.");
    }

    [Fact]
    public async Task SetTunerBypass_InStandby_Refused()
    {
        _radio.OnSubscribe.Add(Tgxl.Replace("operate=1", "operate=0", StringComparison.Ordinal));
        var (service, _, _) = Create();
        await using var _ = service;
        await WaitUntil(() => service.Current.Tuner is not null);

        (await service.SetTunerBypassAsync(true)).Should().Be("Put the tuner in operate first.");
    }

    [Fact]
    public async Task SetAmpOperate_NoAmp_Refused()
    {
        var (service, _, _) = Create();
        await using var _ = service;
        await WaitUntil(() => _radio.Commands.Contains("sub amplifier all"));

        (await service.SetAmpOperateAsync(true)).Should().Be("No amplifier is paired with the radio.");
    }

    [Fact]
    public async Task SettingsChange_ToAnotherRadio_Disconnects()
    {
        _radio.OnSubscribe.Add(PgxlStandby);
        var (service, session, settings) = Create();
        await using var _ = service;
        await WaitUntil(() => service.Current.Amplifier is not null);

        settings.Update(s => { s.Profile.Rig.Model = 2014; s.Profile.Rig.Port = "COM5"; });
        await WaitUntil(() => service.Current.Amplifier is null);
        await service.Settled;

        session.Snapshot.Services[ServiceNames.Accessories].Health.Should().Be(ServiceHealth.Off);
    }

    [Fact]
    public void Panel_StandbyAmpAndOperateTuner_OffersOperateAndBypass()
    {
        var a = Accessories(PgxlStandby, Tgxl);

        var p = AccessoryPanel.From(a, canChange: true);

        p.BarText.Should().Be("Amp Standby · Tuner Operate");
        (p.CanAmpOperate, p.CanAmpStandby).Should().Be((true, false));
        (p.CanTunerOperate, p.CanTunerStandby, p.CanTunerBypass, p.CanTunerInLine).Should().Be((false, true, true, false));
        p.AmpName.Should().Be("PowerGenius XL");
    }

    [Fact]
    public void Panel_OnTheAir_OffersNothing()
    {
        var p = AccessoryPanel.From(Accessories(PgxlStandby, Tgxl), canChange: false);

        new[] { p.CanAmpOperate, p.CanAmpStandby, p.CanTunerOperate, p.CanTunerStandby, p.CanTunerBypass, p.CanTunerInLine }.Should().OnlyContain(b => !b);
        p.Any.Should().BeTrue();
    }

    [Theory]
    [InlineData("IDLE", "Amp Operate", TextKind.Caution)]
    [InlineData("TRANSMIT_A", "Amp Transmitting", TextKind.Critical)]
    [InlineData("FAULT", "Amp Fault", TextKind.Critical)]
    [InlineData("POWERUP", "Amp Starting", TextKind.Secondary)]
    public void Panel_AmpStates_ReadAsText(string state, string text, TextKind kind)
    {
        var p = AccessoryPanel.From(Accessories(PgxlStandby.Replace("STANDBY", state, StringComparison.Ordinal)), canChange: true);

        p.BarText.Should().Be(text);
        p.BarKind.Should().Be(kind);
    }

    [Fact]
    public void Panel_FaultedAmp_CanGoToStandby()
    {
        var p = AccessoryPanel.From(Accessories(PgxlStandby.Replace("STANDBY", "FAULT", StringComparison.Ordinal)), canChange: true);

        (p.CanAmpOperate, p.CanAmpStandby).Should().Be((false, true));
    }

    private static FlexAccessories Accessories(params string[] lines) =>
        lines.Aggregate(FlexAccessories.Empty, (a, l) => a.With(FlexStatusLine.Parse(l)!));

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Condition not met");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
