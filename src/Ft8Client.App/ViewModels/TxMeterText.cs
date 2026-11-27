// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using Ft8Client.Core.Transmit;

namespace Ft8Client.App.ViewModels;

/// <summary>How the transmit meters read on screen: a short bottom-bar label, its tooltip, and a one-line summary.</summary>
public static class TxMeterText
{
    /// <summary>
    /// The bottom-bar warning ("SWR 3.4 · ALC 70%"), the full advice for its tooltip, and its colour. Empty when the
    /// last transmission read within limits or nothing has been measured.
    /// </summary>
    public static (string Text, string Tip, TextKind Kind) Warning(TxMeterResult? r)
    {
        if (r?.Warning is not { } tip) return (string.Empty, string.Empty, TextKind.Normal);
        var parts = new List<string>();
        if (r.Swr is { } s && s >= TxMeterCheck.SwrCaution) parts.Add("SWR " + s.ToString("0.0", CultureInfo.InvariantCulture));
        if (r.Alc is { } a && a >= TxMeterCheck.AlcCaution) parts.Add("ALC " + Percent(a));
        return (string.Join(" · ", parts), tip, r.Level == TxMeterLevel.Critical ? TextKind.Critical : TextKind.Caution);
    }

    /// <summary>"ALC 12% · SWR 1.3", with "not reported" for a meter the radio does not give; "—" before any reading.</summary>
    public static string Summary(TxMeterResult? r) => r is null
        ? "—"
        : $"ALC {(r.Alc is { } a ? Percent(a) : "not reported")} · SWR {(r.Swr is { } s ? s.ToString("0.0", CultureInfo.InvariantCulture) : "not reported")}";

    /// <summary>
    /// The meter strip: the live reading while one is on the air, otherwise the last transmission's result. Null when
    /// neither has a meter value. <paramref name="maxWatts"/> scales the power bar (the radio's most, when known).
    /// </summary>
    /// <param name="live">The reading while on the air, or null.</param>
    /// <param name="held">The last transmission's result, or null.</param>
    /// <param name="maxWatts">The radio's most power, when known.</param>
    /// <param name="estimateWatts">
    /// What the power setting and attenuation should give (WSPR), or null. Shown beside a reading at the meter's floor,
    /// which cannot say how far under it the output is.
    /// </param>
    public static TxMeterStrip? Strip(TxMeterReading? live, TxMeterResult? held, int? maxWatts, double? estimateWatts = null)
    {
        var isLive = live is { Any: true };
        var (alc, swr, watts) = isLive ? (live!.Alc, live.Swr, live.Watts) : (held?.Alc, held?.Swr, held?.Watts);
        if (alc is null && swr is null && watts is null) return null;
        // Hamlib's ALC runs 0 to 1; anything else is not an ALC reading (see TxLevelController), so it is not shown.
        if (alc is < 0 or > 1) alc = null;
        // SWR below 1 is not a ratio: the radio measured no forward power.
        if (swr is < 1) swr = null;
        var alcHigh = alc >= TxMeterCheck.AlcCaution;
        var swrKind = swr >= TxMeterCheck.SwrCritical ? TextKind.Critical : swr >= TxMeterCheck.SwrCaution ? TextKind.Caution : TextKind.Normal;
        var full = maxWatts is > 0 ? maxWatts.Value : 100;
        var tip = !isLive && held?.Warning is { } warning
            ? warning
            : isLive ? "The radio's meters, live while transmitting." : "The radio's meters from the last transmission.";
        return new TxMeterStrip(
            isLive,
            isLive ? "TX" : "Last TX",
            tip,
            watts is { } w ? WattsText(w, estimateWatts) : null,
            watts is { } w2 ? Math.Clamp(w2 / full, 0, 1) : 0,
            alc is null ? null : alcHigh ? "High" : "OK",
            alc ?? 0,
            alcHigh ? TextKind.Caution : TextKind.Normal,
            swr?.ToString("0.0", CultureInfo.InvariantCulture),
            swr is { } s ? Math.Clamp((s - 1) / (TxMeterCheck.SwrCritical - 1), 0, 1) : 0,
            swrKind);
    }

    /// <summary>
    /// Power as shown. At or below <see cref="LowestReliableWatts"/> it reads "≤5 W": Hamlib's FT-710 table starts at
    /// 5 W, so a radio putting out nothing (no audio reaching it) reads exactly 5 W.
    /// </summary>
    public static string WattsText(double watts) => watts <= LowestReliableWatts
        ? "≤5 W"
        : Math.Round(watts).ToString("0", CultureInfo.InvariantCulture) + " W";

    /// <summary>
    /// As <see cref="WattsText(double)"/>, with the estimate beside a reading at the floor: "≤5 W · est. 50 mW".
    /// </summary>
    public static string WattsText(double watts, double? estimateWatts) =>
        watts <= LowestReliableWatts && estimateWatts is { } e && e > 0
            ? $"{WattsText(watts)} · est. {PreciseWattsText(e)}"
            : WattsText(watts);

    /// <summary>
    /// Power to two significant figures: "5 W", "2.5 W", "50 mW", "500 mW", "0.5 mW". WSPR's dBm steps are nominal
    /// (17 dBm is 50.1 mW), so a third figure would only be noise.
    /// </summary>
    public static string PreciseWattsText(double w)
    {
        var (value, unit) = w >= 1 ? (w, "W") : (w * 1000, "mW");
        return string.Create(CultureInfo.InvariantCulture, $"{TwoFigures(value):0.##} {unit}");
    }

    private static double TwoFigures(double v)
    {
        if (v <= 0) return 0;
        var scale = Math.Pow(10, Math.Floor(Math.Log10(v)) - 1);
        return Math.Round(v / scale) * scale;
    }

    /// <summary>The lowest power some radios' meters can tell apart from none (Hamlib's FT-710 table starts here).</summary>
    public const double LowestReliableWatts = TxMeterCheck.MeterFloorWatts;

    private static string Percent(double fraction) => Math.Round(fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
