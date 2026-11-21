// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Audio.Backends;

/// <summary>
/// Finds a saved audio device in the current list. Windows gives a device a new endpoint id when it comes back on
/// another USB port or after its driver reinstalls, so a device not found by id is looked for by its name.
/// </summary>
public static class AudioDeviceMatch
{
    /// <summary>The name radios' built-in USB sound cards (Yaesu, Icom, Kenwood) and many interfaces give themselves.</summary>
    public const string RadioCodecName = "USB Audio CODEC";

    /// <summary>
    /// The device with <paramref name="id"/>, else the only one named <paramref name="name"/>, else null (two devices
    /// with the same name are not guessed between).
    /// </summary>
    public static AudioDevice? Find(IReadOnlyList<AudioDevice> devices, string? id, string? name)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (id is not null && devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal)) is { } byId) return byId;
        if (string.IsNullOrEmpty(name)) return null;
        var byName = devices.Where(d => string.Equals(d.Name, name, StringComparison.Ordinal)).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    /// <summary>
    /// The radio's USB sound device to offer when none is chosen yet: the only one whose name has
    /// <see cref="RadioCodecName"/> in it, else null. Only offered in Station setup, never used without the operator.
    /// </summary>
    public static AudioDevice? SuggestRadio(IReadOnlyList<AudioDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var codecs = devices.Where(d => d.Name.Contains(RadioCodecName, StringComparison.OrdinalIgnoreCase)).ToList();
        return codecs.Count == 1 ? codecs[0] : null;
    }

    /// <summary>
    /// A FlexRadio's DAX device to offer once the radio is chosen: on Linux RimuDAX's <c>rimu.dax.tx</c> and
    /// <c>rimu.dax.rx&lt;n&gt;</c> (PulseAudio), on Windows SmartSDR DAX's "DAX Audio TX" and "DAX Audio RX &lt;n&gt;". Null
    /// when there is no such device, or more than one would fit.
    /// </summary>
    /// <param name="devices">Inputs, or outputs.</param>
    /// <param name="transmit">True for the transmit device (an output), false for a receive device (an input).</param>
    /// <param name="channel">The DAX receive channel, 1 to 8; ignored for transmit.</param>
    public static AudioDevice? SuggestFlex(IReadOnlyList<AudioDevice> devices, bool transmit, int channel = 1)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var rimu = transmit ? "PulseAudio:rimu.dax.tx" : $"PulseAudio:rimu.dax.rx{channel}";
        if (devices.FirstOrDefault(d => string.Equals(d.Id, rimu, StringComparison.Ordinal)) is { } r) return r;
        var dax = transmit ? "DAX Audio TX" : $"DAX Audio RX {channel}";
        var matches = devices.Where(d => d.Name.StartsWith(dax, StringComparison.OrdinalIgnoreCase)
                                         && (d.Name.Length == dax.Length || !char.IsDigit(d.Name[dax.Length]))).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
