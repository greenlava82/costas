// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Rig;

/// <summary>Puts the radio on a dial frequency in a mode, in the order that leaves it where it was asked to be.</summary>
public static class RigTuning
{
    /// <summary>
    /// Sets the frequency, then the mode, then the frequency again. Some radios (an FTDX10, for one) move the dial when
    /// the mode changes, so a frequency set only before the mode can end up a few hundred hertz off (7.073 300 for
    /// 7.074 000). The mode is not simply set first because a band change can bring back that band's last mode (Hamlib
    /// uses the band stack on some Yaesu radios). The dial is not read back to decide: Hamlib may answer from its cache
    /// of the frequency just set. Throws <see cref="RigException"/> as the radio's own commands do.
    /// </summary>
    public static async Task TuneAsync(IRig rig, long hz, RigMode mode, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rig);
        await rig.SetFrequencyAsync(hz, ct).ConfigureAwait(false);
        await rig.SetModeAsync(mode, ct).ConfigureAwait(false);
        await rig.SetFrequencyAsync(hz, ct).ConfigureAwait(false);
    }
}
