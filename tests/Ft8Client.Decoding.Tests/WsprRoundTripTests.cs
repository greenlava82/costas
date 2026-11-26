// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Wspr;

namespace Ft8Client.Decoding.Tests;

/// <summary>
/// WSPR audio from our encoder and waveform, decoded by WSJT-X's own <c>wsprd</c>: the same call, grid and power come back,
/// with a time offset inside a second and the frequency within 2 Hz.
/// </summary>
public class WsprRoundTripTests
{
    private static readonly DateTime Slot = new(2026, 11, 26, 14, 2, 0, DateTimeKind.Utc);
    private const long Dial = 14_095_600;
    private const int Offset = 1500;
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // A message sent in a slot: one second of silence, the transmission, then silence to the end of the 2 minutes, with a
    // little noise (wsprd measures its noise from the audio).
    private static SlotAudio Audio(string text, int seed)
    {
        var enc = WsprEncoder.Encode(text);
        enc.Refusal.Should().BeNull();
        var wave = WsprWaveform.Render(enc.Symbols, Offset, 12_000, attenuationDb: -10);
        var samples = new short[WsprTiming.RecordSamples];
        var rng = new Random(seed);
        for (var i = 0; i < samples.Length; i++) samples[i] = (short)(rng.NextDouble() * 400 - 200);
        for (var i = 0; i < wave.Length; i++) samples[12_000 + i] = (short)Math.Clamp(samples[12_000 + i] + wave[i] * 12_000, short.MinValue, short.MaxValue);
        return new SlotAudio(Slot, Mode.Wspr, samples);
    }

    private static string Text(WsprSpot s) => s.IsHashed ? $"<{s.Callsign}> {s.Grid} {s.PowerDbm}" : s.Grid is null ? $"{s.Callsign} {s.PowerDbm}" : $"{s.Callsign} {s.Grid} {s.PowerDbm}";

    private static async Task<WsprSpot> RoundTripAsync(string text, string[]? prime = null)
    {
        var wsprd = WsprdLocator.Find();
        Assert.SkipWhen(wsprd is null, "wsprd is not installed; run third_party/fetch or install WSJT-X.");
        var tmp = Path.Combine(Path.GetTempPath(), "ft8client-tests", Guid.NewGuid().ToString("N"));
        var decoder = new WsprdDecoder(new WsprdOptions(wsprd!, tmp, TimeSpan.FromSeconds(120)));
        // A hashed call is only named by wsprd once it has heard the call itself (it keeps a hash table between runs).
        foreach (var p in prime ?? [])
            (await decoder.DecodeAsync(Audio(p, 1), Dial, Ct)).Status.Should().Be(DecodeStatus.Ok);

        var result = await decoder.DecodeAsync(Audio(text, 7), Dial, Ct);

        result.Status.Should().Be(DecodeStatus.Ok, result.Message);
        // wsprd now and then reports the one signal twice, a few hertz apart; every report must be the message sent, and one of them at the right place.
        result.Spots.Should().NotBeEmpty();
        result.Spots.Select(Text).Distinct().Should().ContainSingle("one message in the slot, but wsprd gave: " + string.Join(" | ", result.Spots.Select(Text)));
        var real = result.Spots.Where(s => Math.Abs(s.DtSeconds) <= 1.0 && Math.Abs(s.AudioHz - Offset) <= 2).ToList();
        real.Should().NotBeEmpty("a report within 1 s and 2 Hz of what was sent, among: " + string.Join(" | ", result.Spots.Select(s => $"{s.AudioHz} Hz dt {s.DtSeconds}")));
        return real[0];
    }

    [Theory]
    [InlineData("K1ABC FN42 37")]
    [InlineData("W7LIT DN40 30")]
    [InlineData("N1A AA00 0")]
    [InlineData("KP4XYZ RR99 60")]
    [InlineData("PJ4/K1ABC 37")]
    [InlineData("W/K1ABC 33")]
    [InlineData("HB9/W7LIT 20")]
    [InlineData("NYN/K1ABC 37")]
    [InlineData("K1ABC/P 30")]
    [InlineData("K1ABC/12 23")]
    public async Task RoundTrip_PlainAndCompoundMessages_DecodeToTheSameText(string text)
    {
        var spot = await RoundTripAsync(text);

        Text(spot).Should().Be(text);
    }

    [Theory]
    [InlineData("<K1ABC> FN42AB 37", "K1ABC FN42 37")]
    [InlineData("<PJ4/K1ABC> FK52UD 33", "PJ4/K1ABC 37")]
    [InlineData("<HB9/W7LIT> JN47XX 20", "HB9/W7LIT 20")]
    public async Task RoundTrip_HashedMessage_ResolvesTheCallFromOurHash(string text, string firstHeard)
    {
        var spot = await RoundTripAsync(text, [firstHeard]);

        spot.IsHashed.Should().BeTrue();
        Text(spot).Should().Be(text, "wsprd looks the call up by the hash we sent, so a wrong hash would show <...>");
    }
}
