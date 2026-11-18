// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Data.Settings;

/// <summary>Radio control settings.</summary>
public sealed class RigSettings
{
    /// <summary><c>none</c> (simulation or not set up), <c>hamlib</c>, or <c>vox</c> (audio only).</summary>
    public string Mode { get; set; } = RigModes.None;

    /// <summary>Hamlib model number from <c>rigctl -l</c>.</summary>
    public int? Model { get; set; }

    /// <summary>Serial port, e.g. <c>COM4</c>, or server <c>host:port</c> for network models such as NET rigctl and FLRig.</summary>
    public string? Port { get; set; }

    /// <summary>Baud rate.</summary>
    public int? Baud { get; set; }

    /// <summary>PTT method: <c>cat</c>, <c>rts</c>, <c>dtr</c>, <c>vox</c>.</summary>
    public string Ptt { get; set; } = "cat";

    /// <summary>Separate PTT serial port for RTS or DTR.</summary>
    public string? PttPort { get; set; }

    /// <summary>
    /// Extra Hamlib settings passed to rigctld as <c>-C</c>, comma-separated <c>name=value</c> pairs, for example
    /// <c>civaddr=0x94</c> for an Icom radio set to a different CI-V address. Empty for none.
    /// </summary>
    public string? Config { get; set; }

    /// <summary>
    /// The most power the radio can send, in watts, when Hamlib reports it wrongly (an FTDX10 shown as 0 to 200 W, so
    /// asking for 100 W gave 50 W). Costas then reads and sets the radio's power as a share of this. Null to use what
    /// Hamlib reports.
    /// </summary>
    public int? MaxPowerWatts { get; set; }

    /// <summary>
    /// A FlexRadio's own address (<c>192.168.1.20</c>, or with a port, <c>192.168.1.20:4992</c>), for switching the
    /// amplifier and tuner paired with it (docs/05-integrations.md section 2a). It works however the radio itself is
    /// connected. Null or empty for none, except that a SmartSDR Slice model's own address is used then.
    /// </summary>
    public string? FlexAddress { get; set; }
}
