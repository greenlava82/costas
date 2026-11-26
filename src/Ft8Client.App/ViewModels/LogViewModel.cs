// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ft8Client.Core.Bands;
using Ft8Client.Core.Geo;
using Ft8Client.Core.Logbook;
using Ft8Client.Data.Adif;
using Ft8Client.Data.Log;

namespace Ft8Client.App.ViewModels;

/// <summary>The Log view: every contact, search and filters, edit, delete, import, export, sync.</summary>
public sealed partial class LogViewModel : ObservableObject
{
    /// <summary>The band choice that shows every band.</summary>
    public const string AllBands = "All bands";

    /// <summary>The mode choice that shows every mode.</summary>
    public const string AllModes = ModeGroups.AllModes;

    /// <summary>The mode choice that shows every digital mode (the default).</summary>
    public const string AllDigital = ModeGroups.AllDigital;

    /// <summary>The date choice that shows every date.</summary>
    public const string AllTime = "All time";

    /// <summary>Contacts since 00:00 UTC.</summary>
    public const string Today = "Today (UTC)";

    /// <summary>Contacts in the last 7 UTC days, today included.</summary>
    public const string Last7Days = "Last 7 days";

    /// <summary>Contacts in the last 30 UTC days, today included.</summary>
    public const string Last30Days = "Last 30 days";

    /// <summary>Contacts since 1 January.</summary>
    public const string ThisYear = "This year";

    /// <summary>Contacts between two UTC times the operator types (<see cref="RangeFrom"/>, <see cref="RangeTo"/>).</summary>
    public const string CustomRange = "Custom range (UTC)";

    /// <summary>The confirmation choice that shows every contact.</summary>
    public const string AnyConfirmation = "Confirmed or not";

    /// <summary>Confirmed contacts only.</summary>
    public const string ConfirmedOnly = "Confirmed";

    /// <summary>Unconfirmed contacts only.</summary>
    public const string NotConfirmed = "Not confirmed";

    /// <summary>Every station profile's contacts, whatever callsign they were made as.</summary>
    public const string AllStations = "All stations";

    /// <summary>The first station choice while the profile has no callsign yet (then nothing is filtered).</summary>
    public const string ThisStation = "This station";

    private bool _stationUpdating;

    private const int MaxRows = 5000;

    private readonly QsoRepository _qsos;
    private readonly CountryFile _countries;
    private readonly Func<(string ProfileId, string Callsign)> _profile;
    private readonly Func<CancellationToken, Task<string>> _sync;
    private readonly Func<DateTime?> _lastSync;
    private readonly Action _changed;

    /// <summary>Creates the view model.</summary>
    public LogViewModel(QsoRepository qsos, CountryFile countries, Func<(string, string)> profile, Func<CancellationToken, Task<string>> sync,
                        Func<DateTime?> lastSync, Action changed)
    {
        _qsos = qsos;
        _countries = countries;
        _profile = profile;
        _sync = sync;
        _lastSync = lastSync;
        _changed = changed;
    }

    /// <summary>Downloads LoTW confirmations; returns a message. Null when LoTW is not available.</summary>
    public Func<CancellationToken, Task<string>>? LotwDownload { get; init; }

    /// <summary>Signs and uploads new contacts to LoTW through TQSL; returns a message.</summary>
    public Func<CancellationToken, Task<string>>? LotwUpload { get; init; }

    /// <summary>Downloads what is new in the Wavelog log now; returns a message. Null when not available.</summary>
    public Func<CancellationToken, Task<string>>? WavelogDownload { get; init; }

    /// <summary>Sends the contacts waiting for Wavelog/Cloudlog now; returns a message.</summary>
    public Func<CancellationToken, Task<string>>? WavelogUpload { get; init; }

    /// <summary>The active profile's Wavelog/Cloudlog set-up: set up at all, download on, upload on.</summary>
    public Func<(bool Configured, bool CanDownload, bool CanUpload)>? WavelogState { get; init; }

    /// <summary>When the Wavelog log was last downloaded, or null.</summary>
    public Func<DateTime?>? WavelogLastDownload { get; init; }

    /// <summary>True when the active profile uses Wavelog/Cloudlog: the Log view shows its button and column.</summary>
    [ObservableProperty]
    public partial bool ShowWavelog { get; set; }

    /// <summary>True when the active profile downloads its Wavelog log.</summary>
    [ObservableProperty]
    public partial bool WavelogCanDownload { get; set; }

    /// <summary>True when the active profile uploads to Wavelog/Cloudlog.</summary>
    [ObservableProperty]
    public partial bool WavelogCanUpload { get; set; }

    /// <summary>Band filter choices: "All bands" then the bands in the log, in band plan order.</summary>
    public ObservableCollection<string> BandChoices { get; } = [AllBands];

    /// <summary>Mode filter choices: "All digital" (the default), "All modes", then FT8, FT4, PSK and RTTY.</summary>
    public IReadOnlyList<string> ModeChoices { get; } = ModeGroups.Choices;

    /// <summary>Date filter choices.</summary>
    public IReadOnlyList<string> DateChoices { get; } = [AllTime, Today, Last7Days, Last30Days, ThisYear, CustomRange];

    /// <summary>Confirmation filter choices.</summary>
    public IReadOnlyList<string> ConfirmedChoices { get; } = [AnyConfirmation, ConfirmedOnly, NotConfirmed];

    /// <summary>
    /// Station choices: the current profile's callsign (its own log, the default, the same contacts worked-before and
    /// Awards count) and <see cref="AllStations"/>.
    /// </summary>
    public ObservableCollection<string> StationChoices { get; } = [ThisStation, AllStations];

    /// <summary>Selected station choice. Anything but <see cref="AllStations"/> means the current profile's callsign.</summary>
    [ObservableProperty]
    public partial string? StationFilter { get; set; } = ThisStation;

    /// <summary>Selected band filter.</summary>
    [ObservableProperty]
    public partial string BandFilter { get; set; } = AllBands;

    /// <summary>Selected mode filter.</summary>
    [ObservableProperty]
    public partial string ModeFilter { get; set; } = AllDigital;

    /// <summary>Selected date filter.</summary>
    [ObservableProperty]
    public partial string DateFilter { get; set; } = AllTime;

    /// <summary>True when the date choice is <see cref="CustomRange"/>: the From and To boxes show.</summary>
    [ObservableProperty]
    public partial bool IsCustomRange { get; set; }

    /// <summary>
    /// Start of the custom range, UTC, as typed: <c>2026-11-20</c> or <c>2026-11-20 14:30</c>. Blank for no start.
    /// </summary>
    [ObservableProperty]
    public partial string RangeFrom { get; set; } = string.Empty;

    /// <summary>
    /// End of the custom range, UTC, as typed. A date alone includes that whole day; a time includes that minute. Blank
    /// for no end.
    /// </summary>
    [ObservableProperty]
    public partial string RangeTo { get; set; } = string.Empty;

    /// <summary>Why the custom range is not used (a date that cannot be read), or empty.</summary>
    [ObservableProperty]
    public partial string RangeError { get; set; } = string.Empty;

    /// <summary>Export ADIF writes plain ASCII text, for DXKeeper and other loggers that read the file as Windows text.</summary>
    [ObservableProperty]
    public partial bool ExportPlainAscii { get; set; }

    /// <summary>Export ADIF leaves out name, QTH and comment, so the receiving logger fills them from its own lookup.</summary>
    [ObservableProperty]
    public partial bool ExportWithoutNameQthComment { get; set; }

    /// <summary>Called with the two export choices when either changes, to keep them in the settings.</summary>
    public Action<bool, bool>? ExportOptionsChanged { get; init; }

    partial void OnExportPlainAsciiChanged(bool value) => ExportOptionsChanged?.Invoke(value, ExportWithoutNameQthComment);

    partial void OnExportWithoutNameQthCommentChanged(bool value) => ExportOptionsChanged?.Invoke(ExportPlainAscii, value);

    /// <summary>Selected confirmation filter.</summary>
    [ObservableProperty]
    public partial string ConfirmedFilter { get; set; } = AnyConfirmation;

    /// <summary>True when a band, mode, date or confirmation filter is set.</summary>
    [ObservableProperty]
    public partial bool Filtered { get; set; }

    /// <summary>Rows, newest first.</summary>
    public ObservableCollection<LogRowViewModel> Rows { get; } = [];

    /// <summary>Search text.</summary>
    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    /// <summary>Selected row.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResendToLoggersCommand))]
    public partial LogRowViewModel? Selected { get; set; }

    /// <summary>What the list shows: day headings (<see cref="LogDayViewModel"/>) and rows, newest first.</summary>
    public ObservableCollection<object> Items { get; } = [];

    /// <summary>The list's selected item; a day heading cannot be selected.</summary>
    public object? SelectedItem
    {
        get => Selected;
        set
        {
            if (value is LogRowViewModel r) Selected = r;
            else if (value is null) Selected = null;
            OnPropertyChanged();
        }
    }

    partial void OnSelectedChanged(LogRowViewModel? oldValue, LogRowViewModel? newValue) => OnPropertyChanged(nameof(SelectedItem));

    /// <summary>Contacts shown, for the summary tile.</summary>
    [ObservableProperty]
    public partial string ContactsTotal { get; set; } = "0";

    /// <summary>Countries among the contacts shown.</summary>
    [ObservableProperty]
    public partial string CountriesTotal { get; set; } = "0";

    /// <summary>Confirmed contacts shown.</summary>
    [ObservableProperty]
    public partial string ConfirmedTotal { get; set; } = "0";

    /// <summary>"of 3,400" under the contacts tile while filtered; empty otherwise.</summary>
    [ObservableProperty]
    public partial string ContactsOf { get; set; } = string.Empty;

    /// <summary>Line under the list: how many are listed and when QRZ last synced.</summary>
    [ObservableProperty]
    public partial string SyncText { get; set; } = string.Empty;

    /// <summary>Status line.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Result of the last action.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>True after Delete was pressed once; the second press deletes.</summary>
    [ObservableProperty]
    public partial bool ConfirmingDelete { get; set; }

    /// <summary>The contact being edited.</summary>
    [ObservableProperty]
    public partial QsoEditViewModel? Editing { get; set; }

    /// <summary>True while syncing, importing or exporting.</summary>
    [ObservableProperty]
    public partial bool Syncing { get; set; }

    partial void OnSearchChanged(string value) => Refresh();

    partial void OnBandFilterChanged(string value) => Refresh();

    partial void OnModeFilterChanged(string value) => Refresh();

    partial void OnDateFilterChanged(string value)
    {
        IsCustomRange = value == CustomRange;
        Refresh();
    }

    partial void OnRangeFromChanged(string value)
    {
        if (IsCustomRange) Refresh();
    }

    partial void OnRangeToChanged(string value)
    {
        if (IsCustomRange) Refresh();
    }

    partial void OnConfirmedFilterChanged(string value) => Refresh();

    partial void OnStationFilterChanged(string? value)
    {
        if (!_stationUpdating) Refresh();
    }

    // The callsign to show contacts for, or null for every station. The first choice is relabelled when the profile
    // (and so the callsign) changes; replacing a selected item can clear the selection for a moment, so it is set back.
    private string? Station()
    {
        var call = _profile().Callsign.Trim().ToUpperInvariant();
        var label = call.Length == 0 ? ThisStation : call;
        var all = StationFilter == AllStations;
        if (StationChoices[0] != label || StationFilter is null)
        {
            _stationUpdating = true;
            StationChoices[0] = label;
            StationFilter = all ? AllStations : label;
            _stationUpdating = false;
        }
        return all || call.Length == 0 ? null : call;
    }

    partial void OnSelectedChanged(LogRowViewModel? value) => ConfirmingDelete = false;


    /// <summary>Reloads rows.</summary>
    public void Refresh()
    {
        RefreshBandChoices();
        var station = Station();
        var filter = CurrentFilter(station);
        // "All digital" is the default, so it alone is not a filter to clear.
        Filtered = BandFilter != AllBands || ModeFilter != AllDigital || DateFilter != AllTime || ConfirmedFilter != AnyConfirmation;
        var rows = _qsos.List(filter, MaxRows);
        Rows.Clear();
        foreach (var q in rows) Rows.Add(new LogRowViewModel(q));
        Items.Clear();
        var today = DateTime.UtcNow.Date;
        foreach (var day in Rows.GroupBy(r => r.Record.QsoDateOn.Date))
        {
            var dayRows = day.ToList();
            Items.Add(new LogDayViewModel(day.Key, dayRows, today));
            foreach (var r in dayRows) Items.Add(r);
        }
        var shown = _qsos.Totals(filter);
        var everything = new LogFilter(StationCallsign: station);
        var total = filter == everything ? shown.Contacts : _qsos.Totals(everything).Contacts;
        var last = _lastSync();
        var sync = last is { } t ? $"last synced with QRZ {t:HH:mm} UTC" : "not synced with QRZ";
        var canDownload = RefreshWavelog();
        if (canDownload)
            sync += WavelogLastDownload?.Invoke() is { } w ? $" · last downloaded from Wavelog {w:HH:mm} UTC" : " · not yet downloaded from Wavelog";
        Status = StatusText(shown, total, rows.Count) + " · " + sync;
        var inv = CultureInfo.InvariantCulture;
        ContactsTotal = shown.Contacts.ToString("N0", inv);
        ContactsOf = shown.Contacts == total ? string.Empty : $"of {total.ToString("N0", inv)}";
        CountriesTotal = shown.Countries.ToString("N0", inv);
        ConfirmedTotal = shown.Confirmed.ToString("N0", inv);
        SyncText = (rows.Count < shown.Contacts ? $"Newest {rows.Count.ToString("N0", inv)} listed · " : string.Empty) + char.ToUpperInvariant(sync[0]) + sync[1..];
    }

    // The filter the list shows; also what Export ADIF writes. Sets RangeError for a custom range that cannot be read.
    private LogFilter CurrentFilter(string? station)
    {
        DateTime? from = null, to = null;
        var error = string.Empty;
        if (DateFilter == CustomRange)
        {
            from = ParseRangeTime(RangeFrom, end: false);
            to = ParseRangeTime(RangeTo, end: true);
            if (from is null && !string.IsNullOrWhiteSpace(RangeFrom)) error = $"Cannot read the From date \"{RangeFrom.Trim()}\". Type it as 2026-11-20 or 2026-11-20 14:30 (UTC).";
            else if (to is null && !string.IsNullOrWhiteSpace(RangeTo)) error = $"Cannot read the To date \"{RangeTo.Trim()}\". Type it as 2026-11-20 or 2026-11-20 18:00 (UTC).";
            else if (from is not null && to is not null && to <= from) error = "The To date is before the From date.";
        }
        RangeError = error;
        return BuildFilter(Search, BandFilter, ModeFilter, DateFilter, ConfirmedFilter, DateTime.UtcNow, station, from, to);
    }

    private static readonly string[] RangeDateFormats = ["yyyy-MM-dd", "yyyyMMdd", "yyyy/MM/dd", "dd.MM.yyyy"];

    private static readonly string[] RangeTimeFormats = ["HH:mm", "HHmm", "H:mm", "HH:mm:ss", "HHmmss"];

    /// <summary>
    /// Reads a custom range time typed as a UTC date, with or without a time: <c>2026-11-20</c>, <c>20261120</c>,
    /// <c>20.11.2026</c>, then optionally <c>14:30</c> or <c>1430</c> (also with seconds, or a <c>T</c> between). For the
    /// range end (<paramref name="end"/>) the result is the first moment after it, since the end is exclusive: a date
    /// alone covers its whole day and a time without seconds covers that minute. Null for blank or unreadable text.
    /// </summary>
    public static DateTime? ParseRangeTime(string? text, bool end)
    {
        var t = (text ?? string.Empty).Trim().Replace('T', ' ');
        if (t.EndsWith('Z') || t.EndsWith('z')) t = t[..^1].TrimEnd();
        if (t.EndsWith("UTC", StringComparison.OrdinalIgnoreCase)) t = t[..^3].TrimEnd();
        if (t.Length == 0) return null;
        var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 2) return null;
        const DateTimeStyles utc = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        if (!DateTime.TryParseExact(parts[0], RangeDateFormats, CultureInfo.InvariantCulture, utc, out var date)) return null;
        date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        if (parts.Length == 1) return end ? date.AddDays(1) : date;
        if (!DateTime.TryParseExact(parts[1], RangeTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
        var at = date + time.TimeOfDay;
        if (!end) return at;
        return parts[1].Length is 6 || parts[1].Count(c => c == ':') == 2 ? at.AddSeconds(1) : at.AddMinutes(1);
    }

    /// <summary>Clears the band, mode, date and confirmation filters (the search text stays).</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        BandFilter = AllBands;
        ModeFilter = AllDigital;
        DateFilter = AllTime;
        ConfirmedFilter = AnyConfirmation;
        RangeFrom = string.Empty;
        RangeTo = string.Empty;
    }

    /// <summary>The filter for the chosen search text and filter choices.</summary>
    /// <remarks>
    /// <paramref name="rangeFrom"/> and <paramref name="rangeTo"/> (exclusive) are used only with <see cref="CustomRange"/>.
    /// </remarks>
    public static LogFilter BuildFilter(string search, string band, string mode, string date, string confirmed, DateTime nowUtc,
                                        string? station = null, DateTime? rangeFrom = null, DateTime? rangeTo = null) => new(
        string.IsNullOrWhiteSpace(search) ? null : search,
        band == AllBands || string.IsNullOrEmpty(band) ? null : band,
        mode == AllModes || string.IsNullOrEmpty(mode) ? null : mode,
        date == CustomRange ? rangeFrom : FromFor(date, nowUtc),
        confirmed switch { ConfirmedOnly => true, NotConfirmed => false, _ => null },
        station,
        date == CustomRange ? rangeTo : null);

    /// <summary>The earliest time a date choice includes, or null for all time. Days are UTC days, as the log shows them.</summary>
    public static DateTime? FromFor(string date, DateTime nowUtc) => date switch
    {
        Today => nowUtc.Date,
        Last7Days => nowUtc.Date.AddDays(-6),
        Last30Days => nowUtc.Date.AddDays(-29),
        ThisYear => new DateTime(nowUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        _ => null,
    };

    /// <summary>
    /// "3,400 contacts · 212 countries · 1,920 confirmed", or when filtered "Showing 120 of 3,400 contacts · 41 countries · 60 confirmed".
    /// </summary>
    public static string StatusText(LogTotals shown, int total, int listed)
    {
        var inv = CultureInfo.InvariantCulture;
        var head = shown.Contacts == total
            ? $"{total.ToString("N0", inv)} {(total == 1 ? "contact" : "contacts")}"
            : $"Showing {shown.Contacts.ToString("N0", inv)} of {total.ToString("N0", inv)} contacts";
        var text = $"{head} · {shown.Countries.ToString("N0", inv)} {(shown.Countries == 1 ? "country" : "countries")} · {shown.Confirmed.ToString("N0", inv)} confirmed";
        if (listed < shown.Contacts) text += $" · newest {listed.ToString("N0", inv)} listed";
        return text;
    }

    private void RefreshBandChoices()
    {
        var bands = _qsos.Bands().OrderBy(BandOrder).ThenBy(b => b, StringComparer.Ordinal).ToList();
        if (BandChoices.Skip(1).SequenceEqual(bands)) return;
        var keep = BandFilter;
        while (BandChoices.Count > 1) BandChoices.RemoveAt(1);
        foreach (var b in bands) BandChoices.Add(b);
        // Removing the selected item clears the selection; put it back if the band is still logged.
        BandFilter = BandChoices.Contains(keep) ? keep : AllBands;
    }

    private static int BandOrder(string band)
    {
        for (var i = 0; i < BandPlan.Bands.Count; i++)
        {
            if (string.Equals(BandPlan.Bands[i].Band, band, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return int.MaxValue;
    }

    /// <summary>Deletes the selected contact locally; asks once first. QRZ deletion is a separate action.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (Selected is null) return;
        if (!ConfirmingDelete)
        {
            ConfirmingDelete = true;
            Message = $"Press Delete again to remove {Selected.Call} on {Selected.Date} from the local log. It stays in QRZ, but syncing will not bring it back.";
            return;
        }
        _qsos.Delete(Selected.Record.Id);
        Message = $"Deleted {Selected.Call}.";
        ConfirmingDelete = false;
        Refresh();
        _changed();
    }

    /// <summary>
    /// Sends a logged contact to other programs again over UDP (WSJT-X QSO Logged and Logged ADIF), for a logger that
    /// was not running when it was logged. Returns the line to show.
    /// </summary>
    public Func<QsoRecord, string>? SendToLoggers { get; init; }

    /// <summary>Sends the selected contact to the loggers listening on UDP again.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ResendToLoggers()
    {
        if (Selected is null) return;
        Message = SendToLoggers is { } send ? send(Selected.Record) : "Sending to other programs is not available.";
    }

    /// <summary>Opens the editor.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (Selected is not null) Editing = new QsoEditViewModel(Selected.Record.Clone());
    }

    /// <summary>Saves the edit.</summary>
    [RelayCommand]
    private void SaveEdit()
    {
        if (Editing is null) return;
        var q = Editing.Apply();
        q.EntityKey = _countries.Lookup(q.Call)?.Entity.Key;
        _qsos.SaveEdit(q);
        Editing = null;
        Message = $"Saved {q.Call}.";
        Refresh();
        _changed();
    }

    /// <summary>Cancels the edit.</summary>
    [RelayCommand]
    private void CancelEdit() => Editing = null;

    /// <summary>
    /// Imports an ADIF file. The file is read and parsed and the contacts written on a worker thread, so Halt and Esc
    /// stay responsive during a large import. A file or database error is reported in <see cref="Message"/> rather than
    /// thrown: an exception reaching the UI thread's unhandled-error handler halts transmit.
    /// </summary>
    public async Task ImportAsync(string path)
    {
        if (Syncing) return;
        var (profileId, call) = _profile();
        Syncing = true;
        Message = $"Importing {Path.GetFileName(path)}…";
        try
        {
            var r = await Task.Run(() => AdifImporter.Import(_qsos, AdifFile.ReadText(path), profileId, call, _countries));
            Message = $"Imported {r.Imported}, merged {r.Merged} duplicates, skipped {r.Skipped}.";
        }
        catch (Exception e) when (IsFileError(e))
        {
            Message = $"Could not import {Path.GetFileName(path)}: {e.Message}";
            return;
        }
        finally
        {
            Syncing = false;
        }
        Refresh();
        _changed();
    }

    /// <summary>
    /// Exports the contacts the list shows to ADIF with QSL state, one file per station callsign
    /// (<see cref="AdifLogExport"/>): the station, band, mode, date (including a custom range) and confirmation filters
    /// and the search text all apply, so a date range exports just those contacts. Every station's contacts are written
    /// when <see cref="AllStations"/> is chosen (another operator's export holds only their own contacts). One callsign
    /// goes to <paramref name="path"/>; with several, each goes beside it with the callsign added to the name
    /// (<c>log-W7LIT.adi</c>), so no file mixes callsigns. <see cref="ExportPlainAscii"/> and
    /// <see cref="ExportWithoutNameQthComment"/> shape the file for loggers such as DXKeeper. The work runs on a worker
    /// thread; a file or database error is reported in <see cref="Message"/> rather than thrown.
    /// </summary>
    public async Task ExportAsync(string path)
    {
        if (Syncing) return;
        var callsign = _profile().Callsign;
        var station = Station();
        var filter = CurrentFilter(station);
        if (RangeError.Length > 0)
        {
            Message = $"Not exported: {RangeError}";
            return;
        }
        var options = new AdifExportOptions(ExportPlainAscii, ExportWithoutNameQthComment);
        var narrowed = filter != new LogFilter(StationCallsign: station);
        var detail = narrowed ? ExportDetail(filter) : null;
        Syncing = true;
        Message = $"Exporting to {Path.GetFileName(path)}…";
        try
        {
            Message = await Task.Run(() =>
            {
                var now = DateTime.UtcNow;
                var groups = AdifLogExport.ByStation(_qsos.List(filter));
                var count = groups.Sum(g => g.Contacts.Count);
                var note = ExportNote(narrowed ? _qsos.Totals(new LogFilter(StationCallsign: station)).Contacts - count : 0, options);
                if (groups.Count <= 1)
                {
                    var only = groups.Count == 1 ? groups[0] : new AdifLogExport.Group(station ?? callsign, []);
                    Ft8Client.Data.Files.AtomicFile.WriteAllText(path, AdifLogExport.Document(only, now, "ADIF export", options, detail));
                    return (station is null
                        ? $"Exported {Contacts(only.Contacts.Count)}."
                        : $"Exported {Contacts(only.Contacts.Count)} made as {station}.") + note;
                }
                var folder = Path.GetDirectoryName(path) ?? ".";
                var stem = Path.GetFileNameWithoutExtension(path);
                var ext = Path.GetExtension(path) is { Length: > 0 } e ? e : ".adi";
                var names = new List<string>();
                foreach (var g in groups)
                {
                    var name = AdifLogExport.FileName(stem, g.StationCallsign, ext);
                    Ft8Client.Data.Files.AtomicFile.WriteAllText(Path.Combine(folder, name), AdifLogExport.Document(g, now, "ADIF export", options, detail));
                    names.Add(name);
                }
                return $"Exported {Contacts(count)} to {names.Count} files, one per callsign: {string.Join(", ", names)}." + note;
            });
        }
        catch (Exception e) when (IsFileError(e))
        {
            Message = $"Could not export to {Path.GetFileName(path)}: {e.Message}";
        }
        finally
        {
            Syncing = false;
        }
    }

    // "1 contact", "1,180 contacts".
    private static string Contacts(int n) => n == 1 ? "1 contact" : $"{n.ToString("N0", CultureInfo.InvariantCulture)} contacts";

    // " The filters left out 1,180 contacts. Plain ASCII text; name, QTH and comment left out." or empty.
    private static string ExportNote(int leftOut, AdifExportOptions options)
    {
        var note = leftOut > 0 ? $" The filters left out {leftOut.ToString("N0", CultureInfo.InvariantCulture)} {(leftOut == 1 ? "contact" : "contacts")}." : string.Empty;
        var how = new List<string>();
        if (options.PlainAscii) how.Add("plain ASCII text");
        if (options.LeaveOutNameQthComment) how.Add("name, QTH and comment left out");
        if (how.Count > 0) note += " " + char.ToUpperInvariant(how[0][0]) + string.Join("; ", how)[1..] + ".";
        return note;
    }

    /// <summary>The header line's account of a filtered export: "Filtered: 20m, FT8, 2026-11-20 00:00 to 2026-11-23 00:00 UTC."</summary>
    public static string ExportDetail(LogFilter f)
    {
        var inv = CultureInfo.InvariantCulture;
        var parts = new List<string>();
        if (f.Search is { } q) parts.Add($"search \"{q.Trim()}\"");
        if (f.Band is { } b) parts.Add(b);
        if (f.ModeKey is { } m) parts.Add(m == AllDigital ? "digital modes" : m);
        if (f.FromUtc is not null || f.ToUtc is not null)
        {
            var fromText = f.FromUtc is { } t1 ? t1.ToString("yyyy-MM-dd HH:mm", inv) : "the start";
            var toText = f.ToUtc is { } t2 ? "before " + t2.ToString("yyyy-MM-dd HH:mm", inv) : "now";
            parts.Add($"from {fromText} to {toText} UTC");
        }
        if (f.Confirmed is { } c) parts.Add(c ? "confirmed" : "not confirmed");
        return parts.Count == 0 ? string.Empty : "Filtered: " + string.Join(", ", parts) + ".";
    }

    /// <summary>
    /// True for the errors an import or export can meet in normal use: the file cannot be read or written, its text
    /// cannot be parsed, or the database refuses the write (full disk, locked file). Anything else is a bug and goes to
    /// the unexpected-error handler.
    /// </summary>
    public static bool IsFileError(Exception e) =>
        e is IOException or UnauthorizedAccessException or FormatException or OverflowException or System.Data.Common.DbException;

    /// <summary>Syncs with QRZ now.</summary>
    [RelayCommand]
    private async Task SyncAsync()
    {
        Syncing = true;
        try
        {
            Message = await _sync(CancellationToken.None);
        }
        finally
        {
            Syncing = false;
            Refresh();
        }
    }

    /// <summary>Downloads new LoTW confirmations now.</summary>
    [RelayCommand]
    private Task LotwDownloadNowAsync() => RunAsync(LotwDownload, "Downloading LoTW confirmations… LoTW can take several minutes to answer.");

    /// <summary>Signs and uploads contacts LoTW does not have yet, through TQSL.</summary>
    [RelayCommand]
    private Task LotwUploadNowAsync() => RunAsync(LotwUpload, "Signing and uploading to LoTW with TQSL…");

    /// <summary>
    /// Shows or hides the Wavelog button and column for the active profile's set-up (after Settings changes, without
    /// reloading the list). Returns whether the download is on.
    /// </summary>
    public bool RefreshWavelog()
    {
        var (configured, canDownload, canUpload) = WavelogState?.Invoke() ?? (false, false, false);
        (ShowWavelog, WavelogCanDownload, WavelogCanUpload) = (configured, canDownload, canUpload);
        return canDownload;
    }

    /// <summary>Downloads what is new in the Wavelog log now.</summary>
    [RelayCommand]
    private Task WavelogDownloadNowAsync() => RunAsync(WavelogDownload, "Downloading new contacts from Wavelog…");

    /// <summary>Sends the contacts waiting for Wavelog/Cloudlog now.</summary>
    [RelayCommand]
    private Task WavelogUploadNowAsync() => RunAsync(WavelogUpload, "Uploading waiting contacts to Wavelog…");

    private async Task RunAsync(Func<CancellationToken, Task<string>>? action, string busy)
    {
        if (action is null) return;
        Syncing = true;
        Message = busy;
        try
        {
            Message = await action(CancellationToken.None);
        }
        finally
        {
            Syncing = false;
            Refresh();
        }
    }

    private bool HasSelection() => Selected is not null;
}
