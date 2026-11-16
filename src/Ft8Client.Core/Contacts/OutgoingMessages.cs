// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Encoding;
using Ft8Client.Core.Messages;
using Ft8Client.Core.Time;

namespace Ft8Client.Core.Contacts;

/// <summary>
/// The text of the messages I send, built from <see cref="ContactSettings"/>. The operator can change only what keeps
/// the exchange standard (owner choice, 2026-10-11): a CQ modifier, my grid in my answer to a CQ, <c>73</c> or
/// <c>RR73</c> as my last message when I answered, and free text sent once after it.
/// </summary>
public static class OutgoingMessages
{
    /// <summary>Longest free text FT8 and FT4 can carry.</summary>
    public const int MaxFreeText = 13;

    /// <summary>
    /// My CQ: <c>CQ [modifier] ME [GRID]</c>. A compound call such as <c>VE3/W7LIT</c> is sent without a grid (and
    /// cannot take a modifier), as WSJT-X does: the message has no room for both.
    /// </summary>
    /// <param name="me">My call.</param>
    /// <param name="grid">My 4-character grid, or empty.</param>
    /// <param name="s">The settings.</param>
    public static string Cq(string me, string grid, ContactSettings s) =>
        Join("CQ", Contests.ContestInfo.Of(s.Contest) is { } c ? c.CqModifier : Modifier(s.CqModifier), me, IsCompound(me) ? string.Empty : grid);

    /// <summary>
    /// My answer to a CQ: <c>DX ME [GRID]</c>. A nonstandard DX call is sent hashed (<c>&lt;PJ4/K1ABC&gt; ME GRID</c>);
    /// with a compound call of mine the DX call is hashed and no grid is sent (<c>&lt;K1ABC&gt; VE3/W7LIT</c>).
    /// </summary>
    /// <param name="dx">The DX call, with or without angle brackets.</param>
    /// <param name="me">My call.</param>
    /// <param name="grid">My 4-character grid, or empty.</param>
    /// <param name="s">The settings.</param>
    public static string Reply(string dx, string me, string grid, ContactSettings s) =>
        IsCompound(me) ? Join($"<{Bare(dx)}>", me) : Join(Hashed(dx), me, s.GridInReply ? grid : string.Empty);

    /// <summary>My report or roger-report: <c>DX ME [R]±NN</c>, the nonstandard call of the two sent hashed.</summary>
    /// <param name="dx">The DX call, with or without angle brackets.</param>
    /// <param name="me">My call.</param>
    /// <param name="report">The report as sent, such as <c>-10</c> or <c>R-10</c>.</param>
    public static string Report(string dx, string me, string report) => Join(Hashed(dx), Hashed(me), report);

    /// <summary>
    /// A sign-off from me: <c>DX ME RR73</c> (or <c>RRR</c>, <c>73</c>). With a compound call of mine it is sent in
    /// full and the DX call hashed (<c>&lt;K1ABC&gt; VE3/W7LIT RR73</c>).
    /// </summary>
    /// <param name="dx">The DX call, with or without angle brackets.</param>
    /// <param name="me">My call.</param>
    /// <param name="word">RR73, RRR or 73.</param>
    public static string Signoff(string dx, string me, string word) =>
        IsCompound(me) ? Join($"<{Bare(dx)}>", me, word) : Join(Hashed(dx), me, word);

    /// <summary>My last message when I answered a CQ: <c>DX ME 73</c> or <c>DX ME RR73</c>.</summary>
    /// <param name="dx">The DX call, with or without angle brackets.</param>
    /// <param name="me">My call.</param>
    /// <param name="s">The settings.</param>
    public static string Final(string dx, string me, ContactSettings s) => Signoff(dx, me, s.AnswerSignoff == "RR73" ? "RR73" : "73");

    /// <summary>How a message from the DX to me looks: <c>ME DX rest</c>, the nonstandard call of the two hashed.</summary>
    /// <param name="dx">The DX call, with or without angle brackets.</param>
    /// <param name="me">My call.</param>
    /// <param name="rest">The rest of the message, or empty.</param>
    public static string FromDx(string dx, string me, string rest) => Join(Hashed(me), Hashed(dx), rest);

    /// <summary>
    /// True for a valid call that does not fit a standard FT8 message, such as <c>VE3/W7LIT</c> or <c>PJ4/K1ABC</c>.
    /// <c>/P</c> and <c>/R</c> calls are standard.
    /// </summary>
    /// <param name="call">The call.</param>
    public static bool IsCompound(string call)
    {
        var c = Bare(call);
        return Callsign.IsValid(c) && !MessagePacker.IsStandardCall(c);
    }

    /// <summary>The free text sent after my last message, upper case and single-spaced; null for none.</summary>
    /// <param name="s">The settings.</param>
    public static string? FreeTextAfter(ContactSettings s)
    {
        var f = Tidy(s.FreeTextAfter);
        return f.Length == 0 ? null : f;
    }

    /// <summary>
    /// Why the message settings cannot be used with my call and grid, or null when every message they produce will be
    /// sent exactly as shown.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="myCall">My call.</param>
    /// <param name="myGrid">My grid as entered.</param>
    public static string? Problem(ContactSettings s, string myCall, string myGrid)
    {
        var mod = Modifier(s.CqModifier);
        if (mod.Length > 0 && !MessageParser.IsCqModifier(mod))
            return $"The CQ modifier \"{mod}\" must be 1 to 4 letters (such as DX or POTA) or 1 to 3 digits.";

        var me = Callsign.Normalize(myCall);
        if (Callsign.IsValid(me))
        {
            if (IsCompound(me) && (mod.Length > 0 || s.Contest != Contests.ContestId.None))
                return $"A compound call such as {me} is sent as plain \"CQ {me}\": FT8 has no room for a CQ modifier or a contest exchange with it.";
            var grid = Grid.Grid4(myGrid) ?? string.Empty;
            // Every message of a contact, with a standard call on the other side, must go out exactly as shown.
            const string dx = "K1ABC";
            foreach (var m in new[]
                     {
                         Cq(me, grid, s), Reply(dx, me, grid, s), Report(dx, me, "-10"), Report(dx, me, "R-10"),
                         Signoff(dx, me, "RR73"), Signoff(dx, me, "73"), Final(dx, me, s),
                     })
            {
                var enc = FtxEncoder.Encode(m, Mode.Ft8);
                if (!enc.Ok) return m.StartsWith("CQ ", StringComparison.Ordinal) ? $"Your CQ \"{m}\" cannot be sent: {enc.Error}" : $"Your message \"{m}\" cannot be sent: {enc.Error}";
            }
        }

        if (FreeTextAfter(s) is not { } f) return null;
        if (f.Length > MaxFreeText) return $"The free text can be at most {MaxFreeText} characters; \"{f}\" has {f.Length}.";
        if (MessageParser.Parse(f).Kind != MessageKind.FreeText)
            return $"The free text \"{f}\" would be read as a standard message. Leave out callsigns and grids.";
        var e = FtxEncoder.Encode(f, Mode.Ft8);
        return e.Ok ? null : $"The free text \"{f}\" cannot be sent: {e.Error}";
    }

    private static string Bare(string call) => call.Length > 2 && call[0] == '<' && call[^1] == '>' ? call[1..^1] : call;

    private static string Hashed(string call) => IsCompound(call) ? $"<{Bare(call)}>" : Bare(call);

    private static string Modifier(string? m) => (m ?? string.Empty).Trim().ToUpperInvariant();

    private static string Tidy(string? t) => string.Join(' ', (t ?? string.Empty).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Join(params string[] parts) => string.Join(' ', parts.Where(p => p.Length > 0));
}
