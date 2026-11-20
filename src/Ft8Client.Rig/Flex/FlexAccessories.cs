// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Collections.Immutable;

namespace Ft8Client.Rig.Flex;

/// <summary>
/// The amplifiers and tuners paired with a FlexRadio, built up from its <c>amplifier</c> status lines. Each line adds
/// to what is known about its device (a line often carries only what changed); a device is kept apart as a tuner when
/// its model is <see cref="FlexModelNames.TunerGeniusXl"/>, and is only shown once its model is known.
/// </summary>
public sealed class FlexAccessories
{
    private readonly ImmutableDictionary<string, ImmutableDictionary<string, string>> _devices;

    private FlexAccessories(ImmutableDictionary<string, ImmutableDictionary<string, string>> devices)
    {
        _devices = devices;
        var amps = new List<FlexAmplifier>();
        var tuners = new List<FlexTuner>();
        foreach (var (handle, v) in devices.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            if (!v.TryGetValue("model", out var model) || model.Length == 0) continue;
            if (model == FlexModelNames.TunerGeniusXl)
                tuners.Add(new FlexTuner(handle, model, Flag(v, "operate"), Flag(v, "bypass"), Flag(v, "tuning")));
            else
                amps.Add(new FlexAmplifier(handle, model, StateOf(v.GetValueOrDefault("state"))));
        }
        Amplifiers = amps;
        Tuners = tuners;
    }

    /// <summary>Nothing known: not connected, or nothing paired.</summary>
    public static FlexAccessories Empty { get; } = new(ImmutableDictionary<string, ImmutableDictionary<string, string>>.Empty);

    /// <summary>The amplifiers, by handle.</summary>
    public IReadOnlyList<FlexAmplifier> Amplifiers { get; }

    /// <summary>The tuners, by handle.</summary>
    public IReadOnlyList<FlexTuner> Tuners { get; }

    /// <summary>The first amplifier, or null.</summary>
    public FlexAmplifier? Amplifier => Amplifiers.Count > 0 ? Amplifiers[0] : null;

    /// <summary>The first tuner, or null.</summary>
    public FlexTuner? Tuner => Tuners.Count > 0 ? Tuners[0] : null;

    /// <summary>What is known after <paramref name="line"/>.</summary>
    public FlexAccessories With(FlexStatusLine line)
    {
        if (line.Removed) return _devices.ContainsKey(line.Handle) ? new FlexAccessories(_devices.Remove(line.Handle)) : this;
        var now = _devices.GetValueOrDefault(line.Handle) ?? ImmutableDictionary<string, string>.Empty;
        var next = now.SetItems(line.Values);
        return next == now && _devices.ContainsKey(line.Handle) ? this : new FlexAccessories(_devices.SetItem(line.Handle, next));
    }

    /// <summary>Same devices in the same states.</summary>
    public bool SameAs(FlexAccessories other) => Amplifiers.SequenceEqual(other.Amplifiers) && Tuners.SequenceEqual(other.Tuners);

    private static bool Flag(ImmutableDictionary<string, string> v, string key) => v.GetValueOrDefault(key) == "1";

    /// <summary>Reads a <c>state=</c> value; one Costas does not know is <see cref="FlexAmpState.Unknown"/>.</summary>
    public static FlexAmpState StateOf(string? value) => value?.ToUpperInvariant() switch
    {
        "POWERUP" => FlexAmpState.PowerUp,
        "SELFCHECK" => FlexAmpState.SelfCheck,
        "STANDBY" => FlexAmpState.Standby,
        "IDLE" => FlexAmpState.Idle,
        "TRANSMIT_A" => FlexAmpState.TransmitA,
        "TRANSMIT_B" => FlexAmpState.TransmitB,
        "FAULT" => FlexAmpState.Fault,
        _ => FlexAmpState.Unknown,
    };
}
