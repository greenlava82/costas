// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Text;
using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.Core;
using Ft8Client.Core.Contacts;
using Ft8Client.Core.Logbook;
using Ft8Client.Data.Adif;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Data.Reference;
using Ft8Client.Integrations.Tests;
using Ft8Client.Integrations.Wsjtx;

namespace Ft8Client.App.Tests;

/// <summary>
/// One contact through every way it leaves Costas: logged to the database, released by the log hold, sent to loggers
/// over UDP (QSO Logged for DXLab SpotCollector and DXKeeper, JTAlert, Log4OM; Logged ADIF), written to an ADIF export
/// and read back by an import. Each mode, with a name and QTH that are not plain ASCII.
/// </summary>
public sealed class LoggingPipelineTests : IDisposable
{
    private static readonly DateTime On = new(2026, 11, 23, 23, 59, 30, DateTimeKind.Utc);
    private static readonly DateTime Off = new(2026, 11, 24, 0, 1, 15, DateTimeKind.Utc);
    private static readonly Ft8Client.Core.Geo.CountryFile Countries = ReferenceData.LoadCountries();

    private readonly Microsoft.Data.Sqlite.SqliteConnection _keep, _keepOther;
    private readonly QsoRepository _qsos, _other;
    private readonly UploadQueueRepository _queue;
    private DateTime _now = Off;

    public LoggingPipelineTests()
    {
        var db = SqliteDatabase.InMemory(out _keep);
        _qsos = new QsoRepository(db);
        _queue = new UploadQueueRepository(db);
        _other = new QsoRepository(SqliteDatabase.InMemory(out _keepOther));
    }

    public void Dispose()
    {
        _keep.Dispose();
        _keepOther.Dispose();
    }

    public static TheoryData<Mode> Modes => [Mode.Ft8, Mode.Ft4, Mode.Psk31, Mode.Psk63, Mode.Rtty];

    // ADIF mode and submode, the mode name on UDP, the sent report as logged, and the frequency logged (dial + offset).
    private static (string AdifMode, string? Submode, string UdpMode, string Report, long Hz) Expect(Mode mode) => mode switch
    {
        Mode.Ft8 => ("FT8", null, "FT8", "-08", 14_075_650),
        Mode.Ft4 => ("MFSK", "FT4", "FT4", "-08", 14_081_200),
        Mode.Psk31 => ("PSK", "PSK31", "PSK31", "599", 14_071_500),
        Mode.Psk63 => ("PSK", "PSK63", "PSK63", "599", 14_072_500),
        _ => ("RTTY", null, "RTTY", "599", 14_083_000),
    };

    private QsoRecord Log(Mode mode, long hz, LogHold? hold = null, int? sent = -8, int? rcvd = -13)
    {
        var slotted = ModeInfo.IsSlotted(mode);
        var log = new DatabaseSessionLog(_qsos, _queue, () => "home", () => true, () => 50, eqslUploadEnabled: () => true,
            cloudlogUploadEnabled: () => true, clubLogUploadEnabled: () => true, hold: hold);
        var entry = new ContactLogEntry("DL1ABC", "JO31", slotted ? sent : 599, slotted ? rcvd : 579, On, Off);
        var context = new LogContext("20m", mode, hz, "DF2RG", "JO31ab", 100, new NeedResult(NeedTag.NewCall, 1), "230",
            "Federal Republic of Germany", "Jürgen Weiß", "NW", "EU", "Köln-Mülheim");
        return log.Write(entry, context)!;
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Write_EachMode_StoresWhatLoggersNeed(Mode mode)
    {
        var (adifMode, submode, udpMode, report, hz) = Expect(mode);
        var q = Log(mode, hz);
        var row = _qsos.Get(q.Id)!;

        row.Call.Should().Be("DL1ABC");
        row.QsoDateOn.Should().Be(On);
        row.QsoDateOff.Should().Be(Off);
        row.Mode.Should().Be(adifMode);
        row.Submode.Should().Be(submode);
        row.Band.Should().Be("20m");
        row.FreqHz.Should().Be(hz);
        row.RstSent.Should().Be(report);
        row.Gridsquare.Should().Be("JO31");
        row.Name.Should().Be("Jürgen Weiß");
        row.Qth.Should().Be("Köln-Mülheim");
        row.State.Should().Be("NW");
        row.StationCallsign.Should().Be("DF2RG");
        row.MyGridsquare.Should().Be("JO31ab");
        row.TxPwr.Should().Be("50", "the radio's power wins over the settings");
        row.Source.Should().Be(QsoSource.Local);
        _queue.All().Select(i => i.Target).Should().BeEquivalentTo(["qrz", "eqsl", "cloudlog", "clublog"]);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void LoggedMessages_EachMode_QsoLoggedCarriesEveryField(Mode mode)
    {
        var (adifMode, submode, udpMode, report, hz) = Expect(mode);
        var q = Log(mode, hz);

        var (logged, _) = UdpInteropService.LoggedMessages(_qsos.Get(q.Id)!, Off);

        var r = Open(logged, WsjtxMessageType.QsoLogged);
        r.DateTime().Should().Be(Off);
        r.Utf8().Should().Be("DL1ABC");
        r.Utf8().Should().Be("JO31");
        r.UInt64().Should().Be((ulong)hz);
        r.Utf8().Should().Be(udpMode, "loggers expect WSJT-X's mode names (FT4, not MFSK)");
        r.Utf8().Should().Be(report);
        r.Utf8().Should().Be(ModeInfo.IsSlotted(mode) ? "-13" : "579");
        r.Utf8().Should().Be("50");
        r.Utf8().Should().Be(ModeInfo.IsSlotted(mode) ? $"{udpMode} Sent: -08 Rcvd: -13" : $"{udpMode} Sent: 599 Rcvd: 579");
        r.Utf8().Should().Be("Jürgen Weiß", "QString travels as UTF-8, as WSJT-X sends it");
        r.DateTime().Should().Be(On);
        r.Utf8().Should().BeEmpty(); // operator
        r.Utf8().Should().Be("DF2RG");
        r.Utf8().Should().Be("JO31ab");
        r.Utf8().Should().BeEmpty(); // exchange sent
        r.Utf8().Should().BeEmpty(); // exchange received
        r.Utf8().Should().BeEmpty(); // propagation mode
        r.AtEnd.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void LoggedMessages_EachMode_LoggedAdifReadsBackAsTheContact(Mode mode)
    {
        var (adifMode, submode, udpMode, report, hz) = Expect(mode);
        var q = Log(mode, hz);

        var (_, adif) = UdpInteropService.LoggedMessages(_qsos.Get(q.Id)!, Off);

        var r = Open(adif, WsjtxMessageType.LoggedAdif);
        var text = r.Utf8()!;
        r.AtEnd.Should().BeTrue();
        text.Should().Contain("<eoh>").And.Contain("<eor>");
        var rec = AdifReader.Read(text).Single();
        rec["call"].Should().Be("DL1ABC");
        rec["qso_date"].Should().Be("20261123");
        rec["time_on"].Should().Be("235930");
        rec["qso_date_off"].Should().Be("20261124", "the contact ran past midnight");
        rec["time_off"].Should().Be("000115");
        rec["band"].Should().Be("20m");
        rec["freq"].Should().Be((hz / 1e6).ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
        rec["mode"].Should().Be(adifMode);
        rec["submode"].Should().Be(submode);
        rec["rst_sent"].Should().Be(report);
        rec["gridsquare"].Should().Be("JO31");
        rec["station_callsign"].Should().Be("DF2RG");
        rec["operator"].Should().Be("DF2RG");
        rec["my_gridsquare"].Should().Be("JO31ab");
        rec["name"].Should().Be("Jürgen Weiß");
        rec["qth"].Should().Be("Köln-Mülheim");
        rec["state"].Should().Be("NW");
        text.Should().Contain($"<name:{Encoding.UTF8.GetByteCount("Jürgen Weiß")}>Jürgen Weiß", "ADIF lengths count UTF-8 bytes");
    }

    [Fact]
    public void Hold_EditWhileHeld_LoggersGetTheEditedContactOnce()
    {
        var hold = new LogHold(_queue, _qsos, () => _now);
        var released = new List<QsoRecord>();
        hold.Released += released.Add;
        var q = Log(Mode.Ft8, 14_075_650, hold);
        hold.Keep(q.Id, true);
        var edited = _qsos.Get(q.Id)!;
        edited.Name = "Juergen";
        _qsos.SaveEdit(edited);
        hold.Keep(q.Id, false);

        _now += LogHold.Window;
        hold.Tick();
        hold.Tick();

        released.Should().ContainSingle();
        var r = Open(UdpInteropService.LoggedMessages(released[0], _now).QsoLogged, WsjtxMessageType.QsoLogged);
        r.DateTime();
        r.Utf8();
        r.Utf8();
        r.UInt64();
        for (var i = 0; i < 5; i++) r.Utf8();
        r.Utf8().Should().Be("Juergen");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ExportThenImport_EachMode_SameContactNoDuplicates(Mode mode)
    {
        var (adifMode, submode, udpMode, report, hz) = Expect(mode);
        var q = Log(mode, hz);
        var text = AdifLogExport.Document(AdifLogExport.ByStation(_qsos.List(LogFilter.All)).Single(), Off, "ADIF export");

        var first = AdifImporter.Import(_other, text, "home", "DF2RG", Countries);
        var again = AdifImporter.Import(_other, text, "home", "DF2RG", Countries);

        first.Imported.Should().Be(1);
        again.Imported.Should().Be(0, "importing the same file twice merges");
        var back = _other.List(LogFilter.All).Single();
        back.Call.Should().Be(q.Call);
        back.QsoDateOn.Should().Be(On);
        back.QsoDateOff.Should().Be(Off);
        back.Mode.Should().Be(adifMode);
        back.Submode.Should().Be(submode);
        back.FreqHz.Should().Be(hz);
        back.RstSent.Should().Be(report);
        back.Name.Should().Be("Jürgen Weiß");
        back.Qth.Should().Be("Köln-Mülheim");
        back.StationCallsign.Should().Be("DF2RG");
        back.MyGridsquare.Should().Be("JO31ab");
        back.Comment.Should().Be(q.Comment);
    }

    [Fact]
    public void Export_PlainAsciiFile_IsPureAsciiAndImportsWithSpelledOutNames()
    {
        Log(Mode.Ft8, 14_075_650);
        var text = AdifLogExport.Document(AdifLogExport.ByStation(_qsos.List(LogFilter.All)).Single(), Off, "ADIF export",
            new AdifExportOptions(PlainAscii: true));

        Encoding.UTF8.GetBytes(text).Should().OnlyContain(b => b == '\n' || (b >= 32 && b < 127));
        AdifImporter.Import(_other, text, "home", "DF2RG", Countries).Imported.Should().Be(1);
        var back = _other.List(LogFilter.All).Single();
        back.Name.Should().Be("Juergen Weiss");
        back.Qth.Should().Be("Koeln-Muelheim");
    }

    [Fact]
    public void Import_Windows1252File_ReadsUmlauts()
    {
        // As DXKeeper writes it: Windows text, one byte per character, lengths in those bytes.
        var cp1252 = CodePagesEncoding();
        var text = "DXKeeper export\n<EOH>\n<CALL:6>DL1ABC<QSO_DATE:8>20261123<TIME_ON:6>235930<BAND:3>20M<MODE:3>FT8<NAME:11>Jürgen Weiß<QTH:12>Köln-Mülheim<EOR>\n";
        var bytes = cp1252.GetBytes(text);
        bytes.Should().Contain(0xFC, "ü is the single byte FC in Windows text");

        var decoded = AdifFile.Decode(bytes);

        AdifImporter.Import(_other, decoded, "home", "DF2RG", Countries).Imported.Should().Be(1);
        var back = _other.List(LogFilter.All).Single();
        back.Name.Should().Be("Jürgen Weiß");
        back.Qth.Should().Be("Köln-Mülheim");
    }

    [Fact]
    public void Decode_Utf8WithAndWithoutBom_Utf8()
    {
        const string text = "<NAME:12>Jürgen Weiß<EOR>";
        AdifFile.Decode(Encoding.UTF8.GetBytes(text)).Should().Be(text);
        AdifFile.Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)]).Should().Be(text);
        AdifFile.Decode(CodePagesEncoding().GetBytes("€ “quoted”")).Should().Be("€ “quoted”", "code page 1252 has € and curly quotes");
    }

    private static Encoding CodePagesEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    private static QDataStreamReader Open(byte[] d, WsjtxMessageType type)
    {
        var r = new QDataStreamReader(d);
        r.UInt32().Should().Be(0xADBCCBDA);
        r.UInt32().Should().Be(2u);
        r.UInt32().Should().Be((uint)type);
        r.Utf8().Should().Be("Costas");
        return r;
    }
}
