// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Wspr;

namespace Ft8Client.Core.Tests;

/// <summary>
/// The WSPR encoder against WSJT-X 3.0.1's <c>wsprsim</c> (captured by scripts/capture-wspr-vectors.sh): the symbols match
/// one for one where the message is one WSPR can send, and the message is refused (CLAUDE.md rule 4) where wsprsim would
/// pack something other than what was written.
/// </summary>
public class WsprEncoderTests
{
    // Messages in the vector file that wsprsim packs without checking and that decode as something else; we must refuse them.
    private static readonly HashSet<string> NotSendable = ["K1ABC/QR 37"];

    public static TheoryData<string, string, string, string> Vectors()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Vectors", "wspr.tsv")))
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var f = line.Split('\t');
            data.Add(f[0], f[1], f[2], f[3]);
        }
        return data;
    }

    [Fact]
    public void Vectors_File_HasAtLeastFortyOfEveryKind()
    {
        var rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Vectors", "wspr.tsv")).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('	')).ToList();
        rows.Count.Should().BeGreaterThanOrEqualTo(40);
        rows.Count(r => r[1] == "1").Should().BeGreaterThan(10);
        rows.Count(r => r[1] == "2").Should().BeGreaterThan(5);
        rows.Count(r => r[1] == "3").Should().BeGreaterThan(5);
        foreach (var p in WsprPower.Legal) rows.Should().Contain(r => r[0] == $"K1ABC FN42 {p}", $"power {p} has a vector");
        rows.Select(r => r[0]).Should().Contain(["N1A AA00 0", "K1AB RR99 60", "<AA0AA> AA00AA 0", "<KP4XYZ> RR99XX 60"], "the edge grids are covered");
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Encode_Message_MatchesWsprsimSymbolForSymbol(string message, string type, string data, string symbols)
    {
        var r = WsprEncoder.Encode(message);

        if (NotSendable.Contains(message))
        {
            r.Ok.Should().BeFalse($"wsprsim packs '{message}' into something other than what was written");
            r.Refusal.Should().NotBeNullOrEmpty();
            return;
        }
        r.Refusal.Should().BeNull($"'{message}' is a valid type {type} message");
        r.Message.Should().Be(message);
        string.Concat(r.Symbols.Select(s => (char)('0' + s))).Should().Be(symbols);
        r.Symbols.Should().HaveCount(162).And.OnlyContain(s => s <= 3);
        WsprMessage.TryParse(message, out var parsed).Should().BeTrue();
        ((int)parsed.Type).ToString().Should().Be(type);
        WsprPacker.TryPack(parsed, out var packed, out _).Should().BeTrue();
        Convert.ToHexString(packed.ToBytes()).Should().Be(data);
    }

    [Fact]
    public void Sync_Vector_Has162Entries() => WsprSync.Vector.Should().HaveCount(162).And.OnlyContain(b => b <= 1);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 128)]
    [InlineData(2, 64)]
    [InlineData(255, 255)]
    [InlineData(5, 160)]
    public void Reverse8_Value_ReversesBits(int value, int expected) => WsprInterleaver.Reverse8(value).Should().Be(expected);

    [Fact]
    public void Interleave_Bits_IsAPermutation()
    {
        var marked = new byte[176];
        for (var i = 0; i < 81; i++) marked[i] = 1;
        WsprInterleaver.Interleave(marked);
        marked.Take(162).Count(b => b == 1).Should().Be(81);
        marked.Skip(162).Should().OnlyContain(b => b == 0, "only the first 162 are touched");
    }

    [Theory]
    [InlineData("K1ABC")]
    [InlineData("PJ4/K1ABC")]
    [InlineData("HB9/W7LIT")]
    public void Hash_Call_Is15Bits(string call) => WsprHash.Of(call).Should().BeInRange(0, 32767);

    // wsprd's own hash of a call, read from a decode: <PJ4/K1ABC> resolves only if our hash matches (see the round trip).
    [Fact]
    public void Hash_SameCall_IsStable() => WsprHash.Of("PJ4/K1ABC").Should().Be(WsprHash.Of("PJ4/K1ABC"));

    [Theory]
    [InlineData("K1ABC FN42 38", "dBm")]
    [InlineData("K1ABC FN42 61", "dBm")]
    [InlineData("K1ABC FN42 5", "dBm")]
    [InlineData("K1ABC FS42 37", "locator")]
    [InlineData("K1ABC FN4 37", "locator")]
    [InlineData("K1ABC FN42AB 37", "locator")]
    [InlineData("KABCDE FN42 37", "digit")]
    [InlineData("K1ABCDE FN42 37", "3 to 6")]
    [InlineData("K FN42 37", "3 to 6")]
    [InlineData("k1abc FN42 37", "upper-case")]
    [InlineData("A1B2C FN42 37", "digit after")]
    [InlineData("K1ABC/PP 37", "suffix")]
    [InlineData("K1ABC/05 37", "suffix")]
    [InlineData("K1ABC/1A 37", "suffix")]
    [InlineData("ABCD/K1ABC 37", "prefix")]
    [InlineData("K1ABC/ 37", "slash")]
    [InlineData("/K1ABC 37", "slash")]
    [InlineData("A/B/K1ABC 37", "slash")]
    [InlineData("<K1ABC> FN42 37", "6-character")]
    [InlineData("<K1ABC> FN42ZZ 37", "6-character")]
    [InlineData("<K1> FN42AB 37", "hashed")]
    [InlineData("<K1ABC> FN42AB 36", "dBm")]
    public void Encode_MessageThatCannotBeSentAsShown_IsRefusedWithAReason(string message, string reasonHas)
    {
        var r = WsprEncoder.Encode(message);

        r.Ok.Should().BeFalse();
        r.Symbols.Should().BeEmpty();
        r.Refusal.Should().Contain(reasonHas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HELLO")]
    [InlineData("K1ABC 37")]
    [InlineData("CQ K1ABC FN42")]
    public void Encode_TextOfNoWsprShape_IsRefused(string text) => WsprEncoder.Encode(text).Ok.Should().BeFalse();

    [Fact]
    public void Encode_IllegalPowerOnAnyType_IsRefusedWithTheRule()
    {
        foreach (var m in new[] { WsprMessage.Standard("K1ABC", "FN42", 36), WsprMessage.Compound("PJ4/K1ABC", 44), WsprMessage.Hashed("K1ABC", "FN42AB", 1) })
            WsprEncoder.Encode(m).Refusal.Should().Contain("ending in 0, 3 or 7");
    }

    [Fact]
    public void Text_EachType_IsAsWsprdWritesIt()
    {
        WsprMessage.Standard("K1ABC", "FN42", 37).Text.Should().Be("K1ABC FN42 37");
        WsprMessage.Compound("PJ4/K1ABC", 37).Text.Should().Be("PJ4/K1ABC 37");
        WsprMessage.Hashed("PJ4/K1ABC", "FK52UD", 37).Text.Should().Be("<PJ4/K1ABC> FK52UD 37");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(37)]
    [InlineData(60)]
    public void IsLegal_ListedPower_True(int dbm) => WsprPower.IsLegal(dbm).Should().BeTrue();

    [Theory]
    [InlineData(-3)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(63)]
    [InlineData(67)]
    [InlineData(100)]
    public void IsLegal_OtherPower_False(int dbm) => WsprPower.IsLegal(dbm).Should().BeFalse();

    [Fact]
    public void Legal_List_Has19ValuesEndingInZeroThreeOrSeven()
    {
        WsprPower.Legal.Should().HaveCount(19).And.BeInAscendingOrder();
        WsprPower.Legal.Should().OnlyContain(p => p % 10 == 0 || p % 10 == 3 || p % 10 == 7);
        WsprPower.Legal.Should().OnlyContain(p => WsprPower.IsLegal(p));
    }

    [Theory]
    [InlineData(5, 0, 36.99)]
    [InlineData(5, -20, 16.99)]
    [InlineData(1, 0, 30)]
    [InlineData(100, -10, 40)]
    public void EstimateDbm_WattsAndAttenuation_IsTenLogOfMilliwattsPlusAttenuation(double watts, double att, double expected) =>
        WsprPower.EstimateDbm(watts, att).Should().BeApproximately(expected, 0.01);

    [Fact]
    public void Display_Estimate_RoundsToOneDecimal() => WsprPower.Display(36.9897).Should().Be(37.0);

    [Fact]
    public void EstimateDbm_NoPower_IsNegativeInfinity() => WsprPower.EstimateDbm(0, 0).Should().Be(double.NegativeInfinity);
}
