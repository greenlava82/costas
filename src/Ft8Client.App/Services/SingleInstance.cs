// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.App.Services;

/// <summary>
/// Keeps a second copy of Costas from running on the same data folder, where it would fight the first for the radio's
/// COM port, the audio devices and the log database (evaluation B13). The first copy holds <c>costas.lock</c> open
/// with no sharing (an exclusive lock on Linux too); a second copy that cannot open it asks the first to come to the
/// front, by touching <c>costas.show</c>, and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>The lock file's name in the data folder.</summary>
    public const string LockFileName = "costas.lock";

    /// <summary>The file a second copy touches to bring the first to the front.</summary>
    public const string ShowFileName = "costas.show";

    private readonly FileStream _lock;
    private readonly FileSystemWatcher? _watcher;

    private SingleInstance(FileStream lockFile, string root)
    {
        _lock = lockFile;
        try
        {
            _watcher = new FileSystemWatcher(root, ShowFileName) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime };
            _watcher.Changed += (_, _) => ShowRequested?.Invoke();
            _watcher.Created += (_, _) => ShowRequested?.Invoke();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            // Bringing the window forward is a courtesy; the lock is what matters.
            _watcher = null;
        }
    }

    /// <summary>Raised (on a thread-pool thread) when another copy was started and asks this one to come forward.</summary>
    public event Action? ShowRequested;

    /// <summary>
    /// Takes the lock for <paramref name="root"/>. Returns null when another copy holds it, after asking that copy to
    /// come to the front.
    /// </summary>
    public static SingleInstance? TryAcquire(string root)
    {
        Directory.CreateDirectory(root);
        try
        {
            var f = new FileStream(Path.Combine(root, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new SingleInstance(f, root);
        }
        catch (IOException)
        {
            try
            {
                File.WriteAllText(Path.Combine(root, ShowFileName), DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The first copy simply stays where it is.
            }
            return null;
        }
    }

    /// <summary>Releases the lock, so the next copy (after an update, say) can start.</summary>
    public void Dispose()
    {
        _watcher?.Dispose();
        _lock.Dispose();
    }
}
