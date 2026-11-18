// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Text;

namespace Ft8Client.Data.Files;

/// <summary>
/// Replaces a file in one step: the text goes to <c>name.tmp</c> beside it, is flushed to disk, and is then renamed
/// over the old file. A crash or a full disk leaves either the old file or the new one, never half of one.
/// </summary>
public static class AtomicFile
{
    /// <summary>The suffix of the temporary file.</summary>
    public const string TempSuffix = ".tmp";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes <paramref name="text"/> as UTF-8 without a byte order mark. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when it cannot; the old file is then left as it was and the temporary
    /// file is removed if possible.
    /// </summary>
    public static void WriteAllText(string path, string text)
    {
        var tmp = path + TempSuffix;
        try
        {
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8NoBom.GetBytes(text);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next write overwrites it.
        }
    }
}
