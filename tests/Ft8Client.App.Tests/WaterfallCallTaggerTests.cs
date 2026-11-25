// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.App.Engine;
using Ft8Client.App.Views;
using Ft8Client.Core;
using Ft8Client.Core.Contacts;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Logbook;
using Ft8Client.Core.Messages;
using Ft8Client.Core.Ranking;
using Ft8Client.Core.Stations;

namespace Ft8Client.App.Tests;

public class WaterfallCallTaggerTests
{
    private static readonly DateTime Slot = new(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Slot.AddSeconds(14);

    private static RawDecodeLine Line(string text, int hz = 1000, DateTime? slot = null, bool toMe = false, bool tx = false, Mode mode = Mode.Ft8) =>
        new(new Decode(slot ?? Slot, -10, 0.1, hz, text, false), MessageParser.Parse(text), null, toMe, tx, mode);

    private static RankedStation Ranked(string call, NeedTag tag) =>
        new(new Station { Call = call }, new NeedResult(tag, 1), new HearsYou(HearsYouKind.None), Chance.Good, true, null);

    private static SessionSnapshot Snap(IReadOnlyList<RawDecodeLine> lines, Mode mode = Mode.Ft8, RankResult? rank = null, ContactView? contact = null) =>
        new() { RawDecodes = lines, Mode = mode, Rank = rank ?? RankResult.Empty, Contact = contact };

    private static ContactView Contact(string call, ContactOutcome outcome) =>
        new(call, null, ContactFlow.CalledCq, outcome, null, null, [], false, false, string.Empty);

    [Fact]
    public void Take_NewDecodes_TagsEachSenderAtTheMiddleOfItsSignal()
    {
        var tagger = new WaterfallCallTagger();
        var tags = tagger.Take(Snap([Line("CQ K5WE EM12", 1500), Line("W1AW K1ABC FN42", 400)]), Now);

        tags.Should().Equal(new WaterfallTag(425, "K1ABC", WaterfallTagKind.Normal), new WaterfallTag(1525, "K5WE", WaterfallTagKind.Normal));
    }

    [Fact]
    public void Take_Ft4_CentresOnTheWiderSignal()
    {
        var tags = new WaterfallCallTagger().Take(Snap([Line("CQ K5WE EM12", 1500, mode: Mode.Ft4)], Mode.Ft4), Now);

        tags.Should().ContainSingle().Which.Hz.Should().Be(1545);
    }

    [Fact]
    public void Take_SameLinesAgain_TagsNothing()
    {
        var tagger = new WaterfallCallTagger();
        IReadOnlyList<RawDecodeLine> lines = [Line("CQ K5WE EM12")];
        tagger.Take(Snap(lines), Now).Should().HaveCount(1);

        tagger.Take(Snap(lines), Now).Should().BeEmpty();
        tagger.Take(Snap([.. lines]), Now.AddSeconds(1)).Should().BeEmpty("a new snapshot holds the same lines");
    }

    [Fact]
    public void Take_LinesAddedLater_TagsOnlyTheNewOnes()
    {
        var tagger = new WaterfallCallTagger();
        var first = Line("CQ K5WE EM12", 1500);
        tagger.Take(Snap([first]), Now);

        var tags = tagger.Take(Snap([first, Line("CQ N8PFK EN80", 600)]), Now.AddSeconds(1));

        tags.Should().ContainSingle().Which.Label.Should().Be("N8PFK");
    }

    [Fact]
    public void Take_EarlyAndFullPassBothHearSender_TaggedOnceASlot()
    {
        var tagger = new WaterfallCallTagger();
        var early = Line("CQ K5WE EM12", 1500);
        tagger.Take(Snap([early]), Now).Should().HaveCount(1);

        // The full decode can report the same station again with a different message text.
        tagger.Take(Snap([early, Line("CQ DX K5WE EM12", 1501)]), Now.AddSeconds(1)).Should().BeEmpty();
    }

    [Fact]
    public void Take_SenderHeardInNextSlot_TaggedAgain()
    {
        var tagger = new WaterfallCallTagger();
        var first = Line("CQ K5WE EM12", 1500);
        tagger.Take(Snap([first]), Now);

        var tags = tagger.Take(Snap([first, Line("CQ K5WE EM12", 1500, Slot.AddSeconds(30))]), Now.AddSeconds(30));

        tags.Should().ContainSingle().Which.Label.Should().Be("K5WE");
    }

    [Fact]
    public void Take_DecodesOlderThanMaxAge_NotTagged()
    {
        // Starting up or switching back to FT8 finds decodes from earlier; they belong to audio long gone.
        var tagger = new WaterfallCallTagger();
        var old = Line("CQ K5WE EM12", 1500, Slot.AddMinutes(-5));
        var recent = Line("CQ N8PFK EN80", 600);

        tagger.Take(Snap([old, recent]), Now).Should().ContainSingle().Which.Label.Should().Be("N8PFK");
    }

    [Fact]
    public void Take_KeyboardMode_TagsNothingAndDoesNotTagLater()
    {
        var tagger = new WaterfallCallTagger();
        IReadOnlyList<RawDecodeLine> psk = [Line("CQ CQ DE K5WE K5WE PSE K", mode: Mode.Psk31)];

        tagger.Take(Snap(psk, Mode.Psk31), Now).Should().BeEmpty();
        tagger.Take(Snap(psk, Mode.Ft8), Now).Should().BeEmpty("lines seen in PSK31 are not tagged on switching mode");
    }

    [Fact]
    public void Take_MyOwnTransmissionOrNoSender_Skipped()
    {
        var tags = new WaterfallCallTagger().Take(Snap([Line("CQ W7LIT DN40", 1500, tx: true), Line("TNX 73 GL", 900), Line("CQ <...> EM12", 700)]), Now);

        tags.Should().BeEmpty();
    }

    [Fact]
    public void Take_CallingMe_ColouredYou()
    {
        var tags = new WaterfallCallTagger().Take(Snap([Line("W7LIT K5WE EM12", 1500, toMe: true)]), Now);

        tags.Should().ContainSingle().Which.Kind.Should().Be(WaterfallTagKind.You);
    }

    [Fact]
    public void Take_StationInContact_ColouredYou()
    {
        var snap = Snap([Line("W1AW K5WE -10", 1500)], contact: Contact("K5WE", ContactOutcome.InProgress),
            rank: new RankResult([Ranked("K5WE", NeedTag.Worked)], [], [], []));

        new WaterfallCallTagger().Take(snap, Now).Should().ContainSingle().Which.Kind.Should().Be(WaterfallTagKind.You);
    }

    [Fact]
    public void Take_ContactEnded_ColouredByNeed()
    {
        var snap = Snap([Line("W1AW K5WE -10", 1500)], contact: Contact("K5WE", ContactOutcome.Logged),
            rank: new RankResult([Ranked("K5WE", NeedTag.Worked)], [], [], []));

        new WaterfallCallTagger().Take(snap, Now).Should().ContainSingle().Which.Kind.Should().Be(WaterfallTagKind.Worked);
    }

    [Fact]
    public void Take_StationsInTheList_ColouredByWhatTheyWouldAdd()
    {
        var rank = new RankResult([Ranked("K5WE", NeedTag.NewCountry), Ranked("N8PFK", NeedTag.NewGrid)], [Ranked("YS1RR", NeedTag.NewBand)],
            [Ranked("W4LB", NeedTag.Worked)], []);
        var lines = new[] { Line("CQ K5WE EM12", 100), Line("CQ N8PFK EN80", 200), Line("CQ YS1RR EK53", 300), Line("CQ W4LB EM73", 400), Line("CQ KD7ZC DN31", 500) };

        var tags = new WaterfallCallTagger().Take(Snap(lines, rank: rank), Now);

        tags.Select(t => t.Kind).Should().Equal(WaterfallTagKind.NewCountry, WaterfallTagKind.NewBand, WaterfallTagKind.NewBand,
            WaterfallTagKind.Worked, WaterfallTagKind.Normal);
    }

    [Fact]
    public void Take_OldestLinesTrimmed_StillFindsTheNewOnes()
    {
        // The session keeps a fixed number of lines: one added, one dropped, the count unchanged.
        var tagger = new WaterfallCallTagger();
        var a = Line("CQ K5WE EM12", 1500);
        var b = Line("CQ N8PFK EN80", 600);
        tagger.Take(Snap([a, b]), Now);

        var tags = tagger.Take(Snap([b, Line("CQ YS1RR EK53", 800)]), Now.AddSeconds(1));

        tags.Should().ContainSingle().Which.Label.Should().Be("YS1RR");
    }

    [Fact]
    public void Take_NoLines_TagsNothing()
    {
        new WaterfallCallTagger().Take(Snap([]), Now).Should().BeEmpty();
    }

    [Theory]
    [InlineData("K5WE", "K5WE")]
    [InlineData("<PJ4/K1ABC>", "PJ4/K1ABC")]
    [InlineData("<...>", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Label_Sender_CallWithoutBrackets(string? sender, string? label)
    {
        WaterfallCallTagger.Label(sender).Should().Be(label);
    }

    [Theory]
    [InlineData(NeedTag.NewCountry, WaterfallTagKind.NewCountry)]
    [InlineData(NeedTag.NewBand, WaterfallTagKind.NewBand)]
    [InlineData(NeedTag.NewGrid, WaterfallTagKind.NewBand)]
    [InlineData(NeedTag.Worked, WaterfallTagKind.Worked)]
    [InlineData(NeedTag.NewCall, WaterfallTagKind.Normal)]
    public void KindOf_Need_MatchesTheStationList(NeedTag need, WaterfallTagKind kind)
    {
        WaterfallCallTagger.KindOf(need).Should().Be(kind);
    }
}
