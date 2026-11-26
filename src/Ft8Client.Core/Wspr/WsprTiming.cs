// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Wspr;

/// <summary>
/// WSPR-2 timing and tone constants, from the WSJT-X 3.0.1 source as recorded in docs/08-open-questions.md
/// (findings V-WSPR1 and V-WSPR3).
/// </summary>
public static class WsprTiming
{
    /// <summary>Sample rate of the WSPR symbol timing and of the audio handed to <c>wsprd</c>.</summary>
    public const int SampleRate = 12_000;

    /// <summary>Number of channel symbols in one transmission (V-WSPR1).</summary>
    public const int SymbolCount = 162;

    /// <summary>Samples per symbol at 12 kHz (V-WSPR1: WSJT-X passes 8192 to its modulator).</summary>
    public const int SymbolSamples12k = 8192;

    /// <summary>Slot length in milliseconds: 120 s, slots starting on even UTC minutes.</summary>
    public const int SlotMilliseconds = 120_000;

    /// <summary>
    /// Delay from slot start to the start of transmit audio in milliseconds (V-WSPR1: WSJT-X's modulator default
    /// <c>delay_ms=1000</c>).
    /// </summary>
    public const int TxStartMilliseconds = 1_000;

    /// <summary>Length of one transmission in milliseconds: 162 symbols of 8192 samples at 12 kHz, 110.592 s.</summary>
    public const int TxMilliseconds = SymbolCount * SymbolSamples12k * 1000 / SampleRate;

    /// <summary>
    /// When the slot's audio is cut and handed to <c>wsprd</c>, in milliseconds from slot start (V-WSPR1: WSJT-X stops at
    /// half-symbol 396 of 3456 samples, 114.048 s).
    /// </summary>
    public const int CaptureCutoffMilliseconds = 396 * 3456 * 1000 / SampleRate;

    /// <summary>
    /// Number of 12 kHz samples written for <c>wsprd</c> per slot: the whole 120 s, as WSJT-X writes (V-WSPR1, V-WSPR2).
    /// </summary>
    public const int RecordSamples = SlotMilliseconds / 1000 * SampleRate;

    /// <summary>Lowest audio frequency of the WSPR window above the dial, in hertz (V-WSPR3).</summary>
    public const int WindowLowHz = 1400;

    /// <summary>Highest audio frequency of the WSPR window above the dial, in hertz (V-WSPR3).</summary>
    public const int WindowHighHz = 1600;

    /// <summary>Spacing between the four tones in hertz: 12000 / 8192, about 1.465 Hz.</summary>
    public const double ToneSpacingHz = (double)SampleRate / SymbolSamples12k;

    /// <summary>Slot length.</summary>
    public static TimeSpan SlotLength { get; } = TimeSpan.FromMilliseconds(SlotMilliseconds);

    /// <summary>Delay from slot start to the start of transmit audio.</summary>
    public static TimeSpan TxStart { get; } = TimeSpan.FromMilliseconds(TxStartMilliseconds);

    /// <summary>Length of one transmission.</summary>
    public static TimeSpan TxLength { get; } = TimeSpan.FromMilliseconds(TxMilliseconds);

    /// <summary>When the slot's audio is handed to <c>wsprd</c>, measured from slot start.</summary>
    public static TimeSpan CaptureCutoff { get; } = TimeSpan.FromMilliseconds(CaptureCutoffMilliseconds);
}
