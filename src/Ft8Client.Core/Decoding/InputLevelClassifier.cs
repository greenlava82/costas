// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Decoding;

/// <summary>
/// The receive level bands shared by Setup's input meter and the Operate screen's "why is it quiet" line: good from
/// -40 to -10 dBFS RMS, clipping at a peak of -1 dBFS or more. Pure.
/// </summary>
public static class InputLevelClassifier
{
    /// <summary>Bottom of the good band, RMS dBFS.</summary>
    public const double GoodLowDbfs = -40;

    /// <summary>Top of the good band, RMS dBFS.</summary>
    public const double GoodHighDbfs = -10;

    /// <summary>A sample peak at or above this counts as clipping, dBFS.</summary>
    public const double ClipDbfs = -1;

    /// <summary>The level reported for all-zero samples, dBFS.</summary>
    public const double SilenceDbfs = -120;

    /// <summary>Classifies a block of samples that arrived, from its RMS and peak levels.</summary>
    /// <param name="rmsDbfs">RMS level, dBFS.</param>
    /// <param name="peakDbfs">Largest sample, dBFS; <see cref="SilenceDbfs"/> or less for all zero.</param>
    public static InputLevelState Classify(double rmsDbfs, double peakDbfs) =>
        peakDbfs <= SilenceDbfs ? InputLevelState.Silence
        : peakDbfs >= ClipDbfs ? InputLevelState.Clipping
        : rmsDbfs < GoodLowDbfs ? InputLevelState.TooQuiet
        : rmsDbfs > GoodHighDbfs ? InputLevelState.TooLoud
        : InputLevelState.Good;
}
