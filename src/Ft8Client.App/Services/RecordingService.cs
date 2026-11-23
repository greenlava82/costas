// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.App.Engine;
using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Time;
using Ft8Client.Data.Journal;
using Ft8Client.Decoding;
using Serilog;

namespace Ft8Client.App.Services;

/// <summary>
/// Writes the decode journal (and its ALL.TXT copy when that is on) and saves slot audio per the Files setting, pruning
/// old WAV files daily. None of it runs in simulation, so replayed recordings do not mix with what was heard on the air.
/// </summary>
public sealed class RecordingService : IDisposable
{
    private readonly Session _session;
    private readonly AppSettingsAccessor _settings;
    private readonly DecodeJournal _journal;
    private readonly WavArchive _wav;
    private readonly IClock _clock;
    private AllTxtWriter? _allTxt;
    private readonly Timer _prune;
    private bool _warned;
    private bool _allTxtWarned;

    /// <summary>Creates the service.</summary>
    public RecordingService(Session session, AppSettingsAccessor settings, string journalFolder, string wavFolder, IClock clock)
    {
        _session = session;
        _settings = settings;
        _journal = new DecodeJournal(journalFolder);
        _wav = new WavArchive(wavFolder);
        _clock = clock;
        _prune = new Timer(_ => Prune());
    }

    /// <summary>Starts recording.</summary>
    public void Start()
    {
        _session.SlotDecoded += OnSlotDecoded;
        _session.SlotAudioDecoded += OnSlotAudio;
        _session.Transmitting += OnTransmitting;
        _prune.Change(TimeSpan.FromMinutes(1), TimeSpan.FromHours(24));
    }

    private void OnSlotDecoded(DateTime slot, Mode mode, IReadOnlyList<Decode> decodes)
    {
        var dial = _session.Snapshot.DialHz;
        var name = ModeInfo.Name(mode);
        var lines = decodes.Select(d => DecodeJournal.Format(slot, dial, false, name, d.Snr, d.Dt, d.OffsetHz, d.Text)).ToList();
        Guard(() => _journal.Append(slot, lines));
        if (AllTxt() is { } all) Guard(() => all.Append(slot, lines), ref _allTxtWarned);
    }

    private void OnTransmitting(PreparedTx tx, DateTime startedUtc)
    {
        var slot = SlotMath.SlotStart(startedUtc, tx.Mode);
        var line = DecodeJournal.Format(slot, _session.Snapshot.DialHz, true, ModeInfo.Name(tx.Mode), 0, 0, tx.OffsetHz, tx.Plan.Message);
        Guard(() => _journal.Append(slot, [line]));
        if (AllTxt() is { } all) Guard(() => all.Append(slot, [line]), ref _allTxtWarned);
    }

    /// <summary>Where ALL.TXT month folders go when the setting is blank: Documents\Costas.</summary>
    public static string DefaultAllTxtFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Costas");

    /// <summary>The ALL.TXT folder a setting means: the typed folder, or <see cref="DefaultAllTxtFolder"/> when blank.</summary>
    public static string AllTxtFolder(string setting) => string.IsNullOrWhiteSpace(setting) ? DefaultAllTxtFolder() : setting.Trim();

    // The ALL.TXT writer for the folder in settings, or null when ALL.TXT is off.
    private AllTxtWriter? AllTxt()
    {
        var files = _settings.Current.Files;
        if (!files.AllTxt) return null;
        var folder = AllTxtFolder(files.AllTxtFolder);
        var w = _allTxt;
        if (w is null || !string.Equals(w.Folder, folder, StringComparison.Ordinal)) _allTxt = w = new AllTxtWriter(folder);
        return w;
    }

    private void OnSlotAudio(SlotAudio audio, int decodes)
    {
        if (WavArchive.ShouldSave(_settings.Current.Files.SaveWav, decodes)) Guard(() => _wav.Save(audio));
    }

    private void Prune()
    {
        Guard(() =>
        {
            var n = _wav.Prune(_clock.UtcNow, _settings.Current.Files.RetentionDays);
            if (n > 0) Log.Information("Deleted {Count} saved WAV files past retention", n);
        });
    }

    private void Guard(Action a) => Guard(a, ref _warned);

    // Runs a write; logs the first failure of a run of failures, then stays quiet until one succeeds.
    private static void Guard(Action a, ref bool warned)
    {
        try
        {
            a();
            warned = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (warned) return;
            warned = true;
            Log.Warning(ex, "Could not write the journal, ALL.TXT or a WAV file");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _session.SlotDecoded -= OnSlotDecoded;
        _session.SlotAudioDecoded -= OnSlotAudio;
        _session.Transmitting -= OnTransmitting;
        _prune.Dispose();
    }
}
