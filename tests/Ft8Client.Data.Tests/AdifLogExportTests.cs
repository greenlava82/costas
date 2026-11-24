// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Text;
using Ft8Client.Core.Geo;
using Ft8Client.Data.Adif;
using Ft8Client.Data.Database;
using Ft8Client.Data.Log;
using Ft8Client.Data.Reference;

namespace Ft8Client.Data.Tests;

/// <summary>The ADIF files written from the log: export and the log copy (C8).</summary>
public sealed class AdifLogExportTests : IDisposable
{
    private static readonly CountryFile Countries = ReferenceData.LoadCountries();
    private static readonly DateTime Now = new(2026, 11, 18, 21, 0, 0, DateTimeKind.Utc);
    private readonly Microsoft.Data.Sqlite.SqliteConnection _keepA, _keepB;
    private readonly QsoRepository _a, _b;

    public AdifLogExportTests()
    {
        _a = new QsoRepository(SqliteDatabase.InMemory(out _keepA));
        _b = new QsoRepository(SqliteDatabase.InMemory(out _keepB));
    }

    public void Dispose()
    {
        _keepA.Dispose();
        _keepB.Dispose();
    }

    private static QsoRecord Contact(string call, int minutes, string station = "W7LIT") => new()
    {
        ProfileId = "home", Call = call, QsoDateOn = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc).AddMinutes(minutes),
        QsoDateOff = new DateTime(2026, 11, 1, 20, 1, 0, DateTimeKind.Utc).AddMinutes(minutes), Band = "20m", FreqHz = 14_075_650,
        Mode = "FT8", RstSent = "-08", RstRcvd = "-13", Gridsquare = "JO22", StationCallsign = station, MyGridsquare = "DN40",
        TxPwr = "25", Comment = "FT8 Sent: -08 Rcvd: -13", Source = QsoSource.Local,
    };

    private static string Doc(params QsoRecord[] qsos) => AdifLogExport.Document(AdifLogExport.ByStation(qsos).Single(), Now, "log copy");

    [Fact]
    public void ByStation_TwoCallsigns_OneGroupEachOldestFirst()
    {
        var groups = AdifLogExport.ByStation([Contact("PA3B", 10), Contact("PA3A", 0), Contact("DL1A", 5, "ve3/w7lit")]);

        groups.Select(g => g.StationCallsign).Should().Equal("VE3/W7LIT", "W7LIT");
        groups[1].Contacts.Select(q => q.Call).Should().Equal("PA3A", "PA3B");
    }

    [Fact]
    public void FileName_CompoundAndEmptyCall_SafeNames()
    {
        AdifLogExport.FileName("costas-log", "W7LIT").Should().Be("costas-log-W7LIT.adi");
        AdifLogExport.FileName("costas-log", "VE3/W7LIT").Should().Be("costas-log-VE3_W7LIT.adi");
        AdifLogExport.FileName("costas-log", "").Should().Be("costas-log-unknown.adi");
    }

    [Fact]
    public void Document_ContactWithQslState_WritesSentAndReceivedFields()
    {
        var q = Contact("PA3A", 0);
        q.LotwConfirmed = true;
        q.Confirmed = true;
        q.LotwUploadState = UploadState.Uploaded;
        q.EqslUploadState = UploadState.Uploaded;
        q.UploadState = UploadState.Uploaded;
        q.QrzLogid = 12345;

        var r = AdifReader.Read(Doc(q)).Single();

        r["lotw_qsl_sent"].Should().Be("Y");
        r["lotw_qsl_rcvd"].Should().Be("Y");
        r["eqsl_qsl_sent"].Should().Be("Y");
        r["eqsl_qsl_rcvd"].Should().BeNull("eQSL has not confirmed it");
        r["qrzcom_qso_upload_status"].Should().Be("Y");
        r["app_qrzlog_logid"].Should().Be("12345");
        r[AdifMapper.CostasConfirmed].Should().BeNull("LoTW already says it is confirmed");
    }

    [Fact]
    public void Document_ImportedWithPaperAndClubLog_KeepsThemFromTheOriginalRecord()
    {
        var q = AdifMapper.ToQso(AdifReader.Read(
            "<call:4>PA3A<qso_date:8>20261101<time_on:4>2000<band:3>20m<mode:3>FT8<station_callsign:5>W7LIT" +
            "<qsl_sent:1>Y<qsl_rcvd:1>Y<qslrdate:8>20261110<clublog_qso_upload_status:1>Y<eor>").Single(),
            "home", "W7LIT", QsoSource.Adif, Countries)!;

        var r = AdifReader.Read(Doc(q)).Single();

        r["qsl_sent"].Should().Be("Y");
        r["qsl_rcvd"].Should().Be("Y");
        r["qslrdate"].Should().Be("20261110");
        r["clublog_qso_upload_status"].Should().Be("Y");
    }

    [Fact]
    public void Document_ConfirmedWithNoFieldSayingBy_WritesCostasConfirmed()
    {
        var q = Contact("PA3A", 0);
        q.Confirmed = true;

        AdifReader.Read(Doc(q)).Single()[AdifMapper.CostasConfirmed].Should().Be("Y");
    }

    [Fact]
    public void Document_QrzConfirmationWithdrawn_DropsTheOldReceivedField()
    {
        var q = Contact("PA3A", 0);
        q.RawAdif = "<call:4>PA3A<app_qrzlog_status:1>C<qsl_rcvd:1>Y<eor>";
        q.Confirmed = false;

        var r = AdifReader.Read(Doc(q)).Single();

        r["app_qrzlog_status"].Should().BeNull();
        r["qsl_rcvd"].Should().BeNull();
    }

    [Fact]
    public void Document_NonAsciiName_LengthCountsUtf8Bytes()
    {
        var q = Contact("EA1A", 0);
        q.Name = "José Müller";

        var text = Doc(q);

        text.Should().Contain("<name:13>José Müller", "11 characters, two of them two bytes in UTF-8");
        AdifReader.Read(text).Single()["name"].Should().Be("José Müller");
    }

    [Fact]
    public void Document_PlainAscii_SpellsOutUmlautsAndCountsOneBytePerCharacter()
    {
        var q = Contact("DL1ABC", 0);
        q.Name = "Jürgen Weiß";
        q.Qth = "Köln-Mülheim";
        q.Comment = "Café\r\nline two";

        var text = AdifLogExport.Document(AdifLogExport.ByStation([q]).Single(), Now, "ADIF export", new AdifExportOptions(PlainAscii: true));

        text.Should().Contain("<name:13>Juergen Weiss").And.Contain("<qth:14>Koeln-Muelheim").And.Contain("<comment:14>Cafe  line two");
        text.All(c => c is '\n' || c is >= ' ' and <= '~').Should().BeTrue("every character of a plain ASCII export is printable ASCII");
    }

    [Fact]
    public void Document_LeaveOutNameQthComment_DropsOnlyThoseFields()
    {
        var q = Contact("DL1ABC", 0);
        q.Name = "Jürgen";
        q.Qth = "Köln";

        var r = AdifReader.Read(AdifLogExport.Document(AdifLogExport.ByStation([q]).Single(), Now, "ADIF export",
            new AdifExportOptions(LeaveOutNameQthComment: true))).Single();

        r["name"].Should().BeNull();
        r["qth"].Should().BeNull();
        r["comment"].Should().BeNull();
        r["call"].Should().Be("DL1ABC");
        r["rst_sent"].Should().Be("-08");
        r["gridsquare"].Should().Be("JO22");
    }

    [Fact]
    public void ToAscii_AccentsAndUnknownCharacters_PlainFormOrQuestionMark()
    {
        AdifExportOptions.ToAscii("Łódź Ærøskøbing São Paulo").Should().Be("Lodz AEroskobing Sao Paulo");
        AdifExportOptions.ToAscii("Москва").Should().Be("??????");
        AdifExportOptions.ToAscii("plain <text>").Should().Be("plain <text>");
        AdifExportOptions.ToAscii("ŽžŻżŰűČčŠšŘřĘęĄąŃńŚśĆćŒœſ").Should().Be("ZzZzUuCcSsRrEeAaNnSsCcOEoes");
        var latin = string.Concat(Enumerable.Range(0xC0, 0x180 - 0xC0).Select(c => (char)c));
        AdifExportOptions.ToAscii(latin).Should().NotContain("?", "every Latin-1 and Latin Extended-A letter has a plain form");
    }

    [Fact]
    public void Read_NonAsciiLengthInCharacters_StillReadsWholeValue()
    {
        // Written by a logger that counts characters (HAMRS, Xlog).
        var r = AdifReader.Read("<name:11>José Müller\n<call:4>EA1A\n<eor>").Single();

        r["name"].Should().Be("José Müller");
        r["call"].Should().Be("EA1A");
    }

    [Fact]
    public void Document_Header_NamesCallsignAndCount()
    {
        var text = Doc(Contact("PA3A", 0), Contact("PA3B", 1));

        text.Should().StartWith("Costas log copy for W7LIT, 2 contacts, written 2026-11-18 21:00 UTC.\n");
        text.Should().Contain("<eoh>");
        AdifReader.Read(text).Should().HaveCount(2);
    }

    [Fact]
    public void Import_FileWrittenFromLog_RoundTripsEveryField()
    {
        var q = Contact("PA3A", 0);
        q.Name = "Jan";
        q.Qth = "Utrecht";
        q.State = null;
        q.Country = "Netherlands";
        q.Dxcc = 263;
        q.Confirmed = q.LotwConfirmed = q.EqslConfirmed = true;
        q.LotwUploadState = q.EqslUploadState = q.UploadState = UploadState.Uploaded;
        q.QrzLogid = 987;
        q.RawAdif = "<call:4>PA3A<sig:4>POTA<sig_info:6>K-1234<qsl_sent:1>Y<eor>";
        var plain = Contact("DL1A", 30);
        _a.Insert(q);
        _a.Insert(plain);

        var text = Doc([.. _a.List()]);
        var result = AdifImporter.Import(_b, text, "other", "N0CALL", Countries);

        result.Imported.Should().Be(2);
        result.Skipped.Should().Be(0);
        var back = _b.List().Single(r => r.Call == "PA3A");
        back.Should().BeEquivalentTo(q, o => o
            .Including(x => x.Call).Including(x => x.QsoDateOn).Including(x => x.QsoDateOff).Including(x => x.Band)
            .Including(x => x.FreqHz).Including(x => x.Mode).Including(x => x.Submode).Including(x => x.RstSent).Including(x => x.RstRcvd)
            .Including(x => x.Gridsquare).Including(x => x.Name).Including(x => x.Qth).Including(x => x.Country).Including(x => x.Dxcc)
            .Including(x => x.StationCallsign).Including(x => x.MyGridsquare).Including(x => x.TxPwr).Including(x => x.Comment)
            .Including(x => x.Confirmed).Including(x => x.LotwConfirmed).Including(x => x.EqslConfirmed)
            .Including(x => x.LotwUploadState).Including(x => x.EqslUploadState).Including(x => x.UploadState).Including(x => x.QrzLogid));
        var raw = AdifReader.Read(back.RawAdif!).Single();
        raw["sig_info"].Should().Be("K-1234");
        raw["qsl_sent"].Should().Be("Y");
        var other = _b.List().Single(r => r.Call == "DL1A");
        other.Confirmed.Should().BeFalse();
        other.UploadState.Should().Be(UploadState.None);
        other.StationCallsign.Should().Be("W7LIT", "the file's station callsign wins over the importing profile's");
    }

    [Fact]
    public void Import_SameFileTwice_MergesInsteadOfDuplicating()
    {
        _a.Insert(Contact("PA3A", 0));
        var text = Doc([.. _a.List()]);

        AdifImporter.Import(_b, text, "home", "W7LIT", Countries);
        var second = AdifImporter.Import(_b, text, "home", "W7LIT", Countries);

        second.Merged.Should().Be(1);
        _b.Count().Should().Be(1);
    }

    [Fact]
    public void Document_Utf8File_HasNoByteOrderMarkAndIsAscii()
    {
        var bytes = Encoding.UTF8.GetBytes(Doc(Contact("PA3A", 0)));

        bytes.Should().OnlyContain(b => b < 0x80, "an all-ASCII log gives a pure ASCII file, as ADIF 3.1 expects");
    }
}
