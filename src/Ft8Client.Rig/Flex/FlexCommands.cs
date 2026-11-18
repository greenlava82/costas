// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;

namespace Ft8Client.Rig.Flex;

/// <summary>
/// The SmartSDR TCP API commands Costas sends for paired accessories (docs/05-integrations.md section 2a). Only these
/// three change anything, and only on the operator's click.
/// </summary>
public static class FlexCommands
{
    /// <summary>Subscribes to every amplifier's (and TunerGenius XL's) status.</summary>
    public const string SubscribeAmplifiers = "sub amplifier all";

    /// <summary>Asks the radio for a reply; used to notice a dead connection.</summary>
    public const string Ping = "ping";

    /// <summary>True for a handle as the radio writes them, <c>0x</c> and 1 to 8 hexadecimal digits.</summary>
    public static bool IsHandle(string s) =>
        s.Length is > 2 and <= 10 && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && s.AsSpan(2).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    /// <summary>
    /// Puts an amplifier in operate or standby: <c>amplifier set &lt;handle&gt; operate=1|0</c> (SmartSDR API wiki,
    /// "TCPIP amplifier", SET).
    /// </summary>
    public static string AmpOperate(string handle, bool operate) => $"amplifier set {Checked(handle)} operate={Bit(operate)}";

    /// <summary>Puts a TunerGenius XL in operate or standby: <c>tgxl set handle=&lt;handle&gt; mode=1|0</c> (FlexLib <c>Tuner.cs</c>).</summary>
    public static string TunerOperate(string handle, bool operate) => $"tgxl set handle={Checked(handle)} mode={Bit(operate)}";

    /// <summary>Bypasses a TunerGenius XL's matching network or puts it back: <c>tgxl set handle=&lt;handle&gt; bypass=1|0</c> (FlexLib <c>Tuner.cs</c>).</summary>
    public static string TunerBypass(string handle, bool bypass) => $"tgxl set handle={Checked(handle)} bypass={Bit(bypass)}";

    /// <summary>A command line as sent: <c>C&lt;seq&gt;|&lt;command&gt;</c> and a new line (SmartSDR API wiki, "Command Format").</summary>
    public static string Frame(int sequence, string command) => string.Create(CultureInfo.InvariantCulture, $"C{sequence}|{command}\n");

    private static string Bit(bool on) => on ? "1" : "0";

    private static string Checked(string handle) =>
        IsHandle(handle) ? handle : throw new ArgumentException($"Not a FlexRadio handle: {handle}", nameof(handle));
}
