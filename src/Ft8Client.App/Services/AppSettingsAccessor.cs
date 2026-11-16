// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Data.Settings;

namespace Ft8Client.App.Services;

/// <summary>Holds the current settings and saves changes. Readers get a consistent object; writers replace it.</summary>
public sealed class AppSettingsAccessor(SettingsStore store, AppSettings initial)
{
    private string? _loadProblem = store.LoadProblem;

    private readonly object _gate = new();
    private AppSettings _current = initial;

    /// <summary>Raised after settings are saved.</summary>
    public event Action<AppSettings>? Changed;

    /// <summary>The current settings. Do not mutate; use <see cref="Update"/>.</summary>
    public AppSettings Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>
    /// Why the last save failed (the file stayed locked or could not be written), or null after a good save. The
    /// change still applies for this session, and the next save tries again with everything.
    /// </summary>
    public string? SaveProblem
    {
        get { lock (_gate) return _saveProblem; }
    }

    private string? _saveProblem;

    /// <summary>
    /// A settings problem the operator should see: a failed save, or a file that could not be read at start-up
    /// (until the next good save writes a sound file). Null when all is well.
    /// </summary>
    public string? Problem
    {
        get { lock (_gate) return _saveProblem ?? _loadProblem; }
    }

    /// <summary>
    /// Changes and saves settings. A failed save does not throw or lose the change: it stays in use, the failure is
    /// in <see cref="SaveProblem"/>, and the next save writes it.
    /// </summary>
    public void Update(Action<AppSettings> change)
    {
        AppSettings copy;
        lock (_gate)
        {
            copy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(_current, SettingsStore.Json), SettingsStore.Json)!;
            change(copy);
            try
            {
                store.Save(copy);
                _saveProblem = null;
                _loadProblem = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _saveProblem = $"Settings could not be saved ({ex.Message}). Changes apply until Costas closes; it tries again on the next change.";
                Serilog.Log.Warning(ex, "Could not save settings to {Path}", store.Path);
            }
            _current = copy;
        }
        Changed?.Invoke(copy);
    }
}
