// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Ft8Client.Core.Messages;

namespace Ft8Client.Core.Stations;

/// <summary>
/// The signature line <c>jt9</c> prints beside a SuperFox signal's messages: <c>$VERIFY$ &lt;fox call&gt; &lt;code&gt;</c>.
/// The code is a one-time password from the DXpedition's key, checked online (docs/05-integrations.md). A code of
/// <c>000000</c> means the Fox sends no signature (WSJT-X's own test signals do this) and is not checked.
/// </summary>
/// <param name="FoxCall">The Fox the signature is for.</param>
/// <param name="Code">The six-digit code.</param>
public sealed record SuperFoxSignature(string FoxCall, string Code)
{
    private const string Word = "$VERIFY$";

    /// <summary>The code a Fox with no key sends.</summary>
    public const string NoCode = "000000";

    /// <summary>True when there is a code worth checking.</summary>
    public bool Checkable => Code != NoCode;

    /// <summary>Reads a decode's text; null when it is not a signature line.</summary>
    public static SuperFoxSignature? Parse(string text)
    {
        var t = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (t.Length != 3 || !string.Equals(t[0], Word, StringComparison.Ordinal)) return null;
        var call = Callsign.Normalize(t[1]);
        if (!Callsign.IsValid(call) || t[2].Length != 6 || !t[2].All(char.IsAsciiDigit)) return null;
        return new SuperFoxSignature(call, t[2]);
    }
}
