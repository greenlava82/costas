// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Text;

namespace Ft8Client.Data.Adif;

/// <summary>
/// Reads an ADIF file's text whatever program wrote it. Most loggers write UTF-8; DXLab DXKeeper, Logger32 and other
/// older Windows loggers write Windows text (code page 1252), where <c>ü</c> is one byte that is not valid UTF-8.
/// </summary>
public static class AdifFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads the file at <paramref name="path"/> as text (see <see cref="Decode"/>).</summary>
    public static string ReadText(string path) => Decode(File.ReadAllBytes(path));

    /// <summary>
    /// The text of an ADIF file's bytes: UTF-8 (a byte order mark, if any, dropped) when the bytes are valid UTF-8, as
    /// any plain ASCII file is; otherwise Windows text (code page 1252), so <c>Müller</c> from DXKeeper reads as
    /// <c>Müller</c> rather than <c>M�ller</c>.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var start = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
        try
        {
            return StrictUtf8.GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return Windows1252().GetString(bytes);
        }
    }

    // Code page 1252 comes from the code pages provider; Latin-1 (the same letters, without € and the curly quotes
    // in 0x80 to 0x9F) if that is unavailable.
    private static Encoding Windows1252()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException)
        {
            return Encoding.Latin1;
        }
    }
}
