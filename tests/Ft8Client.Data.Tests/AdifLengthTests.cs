// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Data.Adif;

namespace Ft8Client.Data.Tests;

// ADIF field lengths count UTF-8 bytes in files from QRZ, LoTW, WSJT-X and most loggers; older files (and older Costas)
// count characters. Both must read back exactly.
public class AdifLengthTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 2, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("José")]
    [InlineData("Jürgen Müller")]
    [InlineData("Søren Ødegård")]
    [InlineData("山田")]
    [InlineData("Ann 📻")]
    [InlineData("plain ASCII")]
    public void WriteThenRead_NonAsciiName_RoundTrips(string name)
    {
        var r = new AdifRecord();
        r["call"] = "K1ABC";
        r["name"] = name;
        r["qth"] = "Zürich";
        r["comment"] = "73";

        var back = AdifReader.Read(AdifWriter.Document([r, r], Now));

        back.Should().HaveCount(2);
        back.Should().AllSatisfy(b =>
        {
            b["call"].Should().Be("K1ABC");
            b["name"].Should().Be(name);
            b["qth"].Should().Be("Zürich");
            b["comment"].Should().Be("73");
        });
    }

    [Theory]
    [InlineData("José", 5)]
    [InlineData("Jürgen", 7)]
    [InlineData("Søren", 6)]
    [InlineData("山田", 6)]
    [InlineData("📻", 4)]
    public void Record_NonAsciiValue_LengthIsUtf8Bytes(string value, int bytes)
    {
        var r = new AdifRecord();
        r["name"] = value;

        AdifWriter.Record(r).Should().Be($"<name:{bytes}>{value}\n<eor>\n");
    }

    [Fact]
    public void Read_ByteCountedFileWithSeveralRecordsAfterNonAscii_FieldsStayApart()
    {
        // As QRZ and WSJT-X write it: lengths in UTF-8 bytes, no white space between fields.
        const string text = "<adif_ver:5>3.1.4<eoh>\n"
            + "<CALL:5>EA1AB<NAME:5>José<QTH:6>Málaga<BAND:3>20m<EOR>\n"
            + "<CALL:5>DL1AB<NAME:7>Jürgen<QTH:10>Köln 📻<BAND:3>40m<EOR>\n"
            + "<CALL:5>JA1AB<NAME:6>山田<BAND:3>15m<EOR>\n"
            + "<CALL:5>K1ABC<NAME:3>Bob<BAND:3>10m<EOR>\n";

        var r = AdifReader.Read(text);

        r.Select(x => x["call"]).Should().Equal("EA1AB", "DL1AB", "JA1AB", "K1ABC");
        r.Select(x => x["name"]).Should().Equal("José", "Jürgen", "山田", "Bob");
        r.Select(x => x["band"]).Should().Equal("20m", "40m", "15m", "10m");
        r[0]["qth"].Should().Be("Málaga");
        r[1]["qth"].Should().Be("Köln 📻");
    }

    [Fact]
    public void Read_CharacterCountedFile_ReadsEveryValueWhole()
    {
        // As older loggers (and Costas before this fix) wrote it: lengths in UTF-16 characters.
        const string text = "<adif_ver:5>3.1.4<eoh>\n"
            + "<call:5>EA1AB\n<name:4>José\n<qth:6>Málaga\n<band:3>20m\n<eor>\n"
            + "<call:5>DL1AB <name:6>Müller <qth:4>Köln <band:3>40m <eor>\n"
            + "<call:5>JA1AB<name:2>山田<band:3>15m<eor>\n"
            + "<call:5>K1ABC<name:6>Ann 📻<band:3>10m<eor>\n";

        var r = AdifReader.Read(text);

        r.Select(x => x["call"]).Should().Equal("EA1AB", "DL1AB", "JA1AB", "K1ABC");
        r.Select(x => x["name"]).Should().Equal("José", "Müller", "山田", "Ann 📻");
        r.Select(x => x["band"]).Should().Equal("20m", "40m", "15m", "10m");
        r[0]["qth"].Should().Be("Málaga");
        r[1]["qth"].Should().Be("Köln");
    }

    [Fact]
    public void Read_NonUtf8FileDecodedWithReplacementCharacters_KeepsFieldsApart()
    {
        // A Latin-1 file read as UTF-8: each accented letter (one byte in the file) became U+FFFD.
        var text = "<call:5>EA1AB<name:4>Jos�<band:3>20m<eor>";

        var r = AdifReader.Read(text).Single();

        r["name"].Should().Be("Jos�");
        r["band"].Should().Be("20m");
    }

    [Fact]
    public void Read_ValueWithAngleBracketByteCounted_KeptWhole()
    {
        var r = AdifReader.Read("<comment:8>né <3 ok<call:5>K1ABC<eor>").Single();

        r["comment"].Should().Be("né <3 ok");
        r["call"].Should().Be("K1ABC");
    }

    [Fact]
    public void Read_LengthPastEndOfText_TakesWhatIsThere()
    {
        AdifReader.Read("<name:20>José").Single()["name"].Should().Be("José");
    }
}
