// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Transmit;

/// <summary>
/// Automatic transmit level: after each transmission, moves the audio level so the radio's ALC sits just where it
/// starts to act, the clean level WSJT-X operators aim for (full power, no ALC compression). The ALC reading is the
/// median of one transmission on Hamlib's 0 to 1 scale. No ALC means the level may be below the radio's set power, so
/// it rises in small steps; too much ALC means overdrive, so it falls, faster the further over it was. The lowest level
/// that made ALC act is remembered so the level does not swing back and forth across it; after
/// <see cref="RelaxAfter"/> quiet transmissions there (the operator may have turned the radio's power up) it probes
/// once more. Only the next transmission's level changes; a transmission on the air is never altered.
/// </summary>
public static class TxLevelTuner
{
    /// <summary>ALC below this counts as not acting (on the FT-710 Hamlib reads the radio's meter in steps of 1/64).</summary>
    public const double Quiet = 0.02;

    /// <summary>ALC at or above this is more than the level needs, so the level goes down.</summary>
    public const double High = 0.25;

    /// <summary>
    /// With zero ALC as the target (PSK, docs/09-keyboard-modes.md section 8), any reading at or above this counts as
    /// ALC acting and lowers the level: ALC compression spreads a PSK signal over its neighbours. It is below the
    /// FT-710's first meter step of 1/64.
    /// </summary>
    public const double ZeroTarget = 0.01;

    /// <summary>The level never goes below this, in dBFS.</summary>
    public const double FloorDb = -40;

    /// <summary>Each rise, in dB.</summary>
    public const double RaiseStepDb = 0.5;

    /// <summary>Quiet transmissions held just below the remembered ceiling before probing above it again.</summary>
    public const int RelaxAfter = 10;

    /// <summary>
    /// The state for the next transmission after one that read <paramref name="alc"/>. The level never goes above
    /// <paramref name="maxDb"/> (at most 0 dBFS) or below <paramref name="minDb"/> (at least <see cref="FloorDb"/>). With
    /// <paramref name="zeroAlc"/> the level aims for no ALC at all (see <see cref="ZeroTarget"/>) instead of the point
    /// where it starts to act. Below <paramref name="riseToDb"/>, a transmission with no ALC rises straight to it (still
    /// within the maximum and half a decibel under a remembered ceiling) instead of half a decibel: a level known to be
    /// safely below the ALC, so the climb to it need not take many transmissions. Once the ALC has acted at or below
    /// that level it is not safe after all, and the level only ever rises half a decibel at a time from then on.
    /// </summary>
    public static TxLevelStep Next(TxLevelState state, double alc, double maxDb, double minDb = FloorDb, bool zeroAlc = false,
        double? riseToDb = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        maxDb = Math.Min(maxDb, 0);
        var floor = Math.Max(FloorDb, minDb);
        var g = state.GainDb;
        var high = zeroAlc ? ZeroTarget : High;
        var quiet = zeroAlc ? ZeroTarget : Quiet;

        if (alc >= high)
        {
            var drop = alc >= 0.75 ? 6.0 : alc >= 0.5 ? 3.0 : 1.0;
            var ceiling = Math.Min(state.CeilingDb ?? g, g);
            var alcBelow = state.AlcBelowRiseTo || (riseToDb is { } r && g <= r + 0.05);
            return new(new(Round(Math.Max(floor, g - drop)), ceiling, 0, alcBelow), TxLevelChange.Lowered);
        }
        if (alc >= quiet) return new(state with { QuietAtCeiling = 0 }, TxLevelChange.Held);

        var limit = state.CeilingDb is { } c ? Math.Min(maxDb, c - RaiseStepDb) : maxDb;
        if (limit > g + 0.05)
        {
            var to = state.AlcBelowRiseTo ? null : riseToDb;
            var step = Math.Max(g + RaiseStepDb, to ?? double.NegativeInfinity);
            return new(state with { GainDb = Round(Math.Min(step, limit)), QuietAtCeiling = 0 }, TxLevelChange.Raised);
        }
        if (state.CeilingDb is null || maxDb <= limit) return new(state with { QuietAtCeiling = 0 }, TxLevelChange.AtMaximum);

        var quietCount = state.QuietAtCeiling + 1;
        return quietCount >= RelaxAfter
            ? new(state with { CeilingDb = null, QuietAtCeiling = 0 }, TxLevelChange.AtCeiling)
            : new(state with { QuietAtCeiling = quietCount }, TxLevelChange.AtCeiling);
    }

    private static double Round(double db) => Math.Round(db, 1);
}
