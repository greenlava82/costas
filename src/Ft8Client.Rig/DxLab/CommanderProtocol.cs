// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ft8Client.Rig.DxLab;

/// <summary>
/// DXLab Commander's TCP command format, as WSJT-X's <c>Transceiver/DXLabSuiteCommanderTransceiver.cpp</c> uses it
/// (read 2026-11-22, WSJT-X master b4f9a43): <c>&lt;command:10&gt;CmdGetFreq&lt;parameters:0&gt;</c>, every value preceded
/// by a <c>&lt;name:length&gt;</c> tag. Replies have the same shape: <c>&lt;CmdFreq:10&gt;14,074.000</c>. Frequencies are in
/// kilohertz with three decimals, written in the computer's number format.
/// </summary>
public static partial class CommanderProtocol
{
    /// <summary>Commander's command port when its Base Port is the default 52000.</summary>
    public const int DefaultPort = 52002;

    [GeneratedRegex(@"<([A-Za-z0-9_]+):(\d+)>")]
    private static partial Regex Tag();

    /// <summary>One tagged value: <c>&lt;xcvrfreq:10&gt;14,074.000</c>.</summary>
    public static string Field(string name, string value) => $"<{name}:{value.Length}>{value}";

    /// <summary>A whole command: <c>&lt;command:10&gt;CmdSetFreq&lt;parameters:N&gt;</c> then the parameters.</summary>
    public static string Command(string name, params (string Name, string Value)[] parameters)
    {
        var p = string.Concat(parameters.Select(x => Field(x.Name, x.Value)));
        return Field("command", name) + Field("parameters", p);
    }

    /// <summary>
    /// Hertz as Commander takes it: kilohertz with three decimals in <paramref name="culture"/>'s format, with its group
    /// separator, as WSJT-X writes it (<c>14,074.000</c> in English, <c>14.074,000</c> in German).
    /// </summary>
    public static string FormatFrequency(long hz, CultureInfo culture) =>
        (hz / 1000m).ToString("N3", culture);

    /// <summary>
    /// A frequency Commander sent, in hertz, or null when it is not one. Commander always writes three decimals of a
    /// kilohertz, so its digits alone are the frequency in hertz whatever the separators are (WSJT-X notes that the group
    /// separator can arrive as an invalid byte). Fewer decimals are scaled, and a value with none is read as whole kilohertz.
    /// </summary>
    public static long? ParseFrequency(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        var digits = new string(t.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0 || digits.Length > 15) return null;
        var value = long.Parse(digits, CultureInfo.InvariantCulture);
        // One to three digits after the last separator, with nothing after them, are a kilohertz's decimals.
        var last = t.LastIndexOfAny(['.', ',']);
        var decimals = last >= 0 && t[(last + 1)..].All(char.IsAsciiDigit) ? t.Length - last - 1 : 0;
        var hz = decimals switch { 1 => value * 100, 2 => value * 10, 3 => value, _ => value * 1000 };
        return hz > 0 ? hz : null;
    }

    /// <summary>
    /// The first complete reply in <paramref name="buffer"/>: its tag name and value. Null while the reply is still
    /// arriving. <paramref name="consumed"/> is how many characters it took.
    /// </summary>
    public static (string Name, string Value)? ReadReply(string buffer, out int consumed)
    {
        consumed = 0;
        var m = Tag().Match(buffer);
        if (!m.Success) return null;
        var length = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var start = m.Index + m.Length;
        if (buffer.Length - start < length) return null;
        consumed = start + length;
        return (m.Groups[1].Value, buffer.Substring(start, length));
    }

    /// <summary>Commander's mode name for a mode the app sets: <c>DATA-U</c> for the data mode, else <c>USB</c>.</summary>
    public static string ModeName(RigMode mode) => mode == RigMode.PktUsb ? "DATA-U" : "USB";

    /// <summary>
    /// A mode Commander reports, in Hamlib's naming as the rest of the app expects (<c>PKTUSB</c>, <c>USB</c>). WSJT-X
    /// reads PKT-R, DATA-U, Data-U and DIGU as upper-sideband data and PKT, DATA-L, Data-L and DIGL as lower.
    /// </summary>
    public static string HamlibMode(string commanderMode) => commanderMode.Trim() switch
    {
        "PKT-R" or "DATA-U" or "Data-U" or "DIGU" => "PKTUSB",
        "PKT" or "DATA-L" or "Data-L" or "DIGL" => "PKTLSB",
        "RTTY-R" => "RTTYR",
        "CW-R" => "CWR",
        "WBFM" => "WFM",
        var m => m.ToUpperInvariant(),
    };

    /// <summary>True for <c>ON</c>, false for <c>OFF</c>, null for anything else (Commander's TX and split replies).</summary>
    public static bool? OnOff(string value) => value.Trim().ToUpperInvariant() switch
    {
        "ON" => true,
        "OFF" => false,
        _ => null,
    };
}
