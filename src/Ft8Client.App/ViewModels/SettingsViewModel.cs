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
using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.App.Services.Notifications;
using Ft8Client.Core.Bands;
using Ft8Client.Core.Contacts;
using Ft8Client.Core.Logbook;
using Ft8Client.Core.Ranking;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;
using Ft8Client.Integrations.Crash;
using Ft8Client.Modems.Rtty;

namespace Ft8Client.App.ViewModels;

/// <summary>Settings. Changes are saved together with Save.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppHost _host;
    private string _loadedTxGain;
    private string _loadedProfile;
    private ProfileDraft? _shownProfile;

    /// <summary>Creates the view model from current settings.</summary>
    public SettingsViewModel(AppHost host, bool rankingOnly = false)
    {
        _host = host;
        SelectedPage = rankingOnly ? WhoToCallPage : 0;
        var s = host.Settings.Current;
        foreach (var p in s.Profiles)
        {
            Profiles.Add(new ProfileDraft
            {
                Id = p.Id, Name = p.Name, Callsign = p.Callsign, Grid = p.Grid, Licence = SettingsMapper.Licence(p, s.Operating), Power = p.PowerWatts.ToString(CultureInfo.InvariantCulture),
                OperatorName = p.OperatorName, Qth = p.Qth, RigDescription = p.RigDescription, Antenna = p.Antenna,
                MaxPower = p.Rig.MaxPowerWatts?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                LogbookKey = host.Secrets.Get(SecretNames.QrzLogbookKey(p.Id)) ?? string.Empty,
                EqslUser = host.Secrets.Get(SecretNames.EqslUsername(p.Id)) ?? string.Empty,
                EqslPassword = host.Secrets.Get(SecretNames.EqslPassword(p.Id)) ?? string.Empty,
                EqslSync = p.Eqsl.SyncEnabled, EqslUpload = p.Eqsl.UploadOnComplete, EqslQth = p.Eqsl.QthNickname,
                CloudlogUrl = p.Cloudlog.Url, CloudlogStationId = p.Cloudlog.StationId, CloudlogUpload = p.Cloudlog.UploadOnComplete, CloudlogDownload = p.Cloudlog.DownloadForWorked,
                CloudlogKey = host.Secrets.Get(SecretNames.CloudlogKey(p.Id)) ?? string.Empty,
                ClubLogEmail = p.ClubLog.Email, ClubLogUpload = p.ClubLog.UploadOnComplete,
                ClubLogPassword = host.Secrets.Get(SecretNames.ClubLogPassword(p.Id)) ?? string.Empty,
                ClubLogPasswordSaved = host.Secrets.Get(SecretNames.ClubLogPassword(p.Id)) ?? string.Empty,
                ClubLogStop = p.ClubLog.Stop,
                LotwEnabled = p.Lotw.Enabled, LotwUser = p.Lotw.Username ?? string.Empty,
                LotwPassword = host.Secrets.Get(SecretNames.LotwPassword(p.Id)) ?? string.Empty,
                LotwStationLocation = p.Lotw.StationLocation ?? string.Empty, LotwUpload = p.Lotw.UploadOnComplete,
            });
        }
        _loadedProfile = s.Profile.Id;
        var active = Profiles.First(d => d.Id == s.Profile.Id);
        (Callsign, Grid, Power, MaxPower, LogbookKey) = (active.Callsign, active.Grid, active.Power, active.MaxPower, active.LogbookKey);
        LicenceChoice = LicenceName(active.Licence);
        _shownProfile = active;
        SelectedProfile = active;
        TxGain = _loadedTxGain = TxLevelController.LevelFor(s.Profile.Audio, s.Operating.Band, s.Operating.Mode).ToString("0.0", CultureInfo.InvariantCulture);
        TxGainLabel = $"Audio level on {s.Operating.Band} (dB)";
        AutoTxLevel = s.Profile.Audio.AutoTxLevel;
        StopOnHighSwr = s.Profile.Audio.StopOnHighSwr;
        Watchdog = s.Operating.WatchdogMinutes.ToString(CultureInfo.InvariantCulture);
        RetryLimit = s.Operating.RetryLimit.ToString(CultureInfo.InvariantCulture);
        Signoff = s.Operating.Signoff;
        CqModifier = s.Operating.CqModifier;
        GridInReply = s.Operating.GridInReply;
        AnswerSignoff = s.Operating.AnswerSignoff == "RR73" ? "RR73" : "73";
        FreeTextAfter = s.Operating.FreeTextAfter;
        AutoLog = s.Operating.AutoLog;
        ChimeOnCaller = s.Operating.ChimeOnCaller;
        AnswerAllInTurn = s.Operating.AnswerAllInTurn;
        KeepCallingWhenBusy = s.Operating.KeepCallingWhenBusy;
        var rtty = SettingsMapper.Rtty(s);
        RttySpeed = rtty.Baud.ToString("0.##", CultureInfo.InvariantCulture);
        RttyShift = rtty.ShiftHz.ToString(CultureInfo.InvariantCulture);
        RttyReverse = rtty.Reverse;
        RttyUnshiftOnSpace = s.Operating.RttyUnshiftOnSpace;
        RttyPowerPercent = s.Operating.RttyPowerPercent.ToString(CultureInfo.InvariantCulture);
        LoadMacros(SettingsMapper.Macros(s.Operating));
        CqParity = s.Operating.CqParity;
        ClockBlock = s.Operating.ClockBlockSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        CommandStartLimit = (Math.Clamp(s.Operating.CommandStartLimitMs, 0, SettingsMapper.MaxCommandStartLimitMs) / 1000.0)
            .ToString("0.0", CultureInfo.InvariantCulture);
        DoubleClickSwitches = s.Operating.DoubleClickSwitches;
        CorrectClock = s.Operating.CorrectClock;
        foreach (var t in SettingsMapper.ToRanking(s.Ranking).TierOrder) TierOrder.Add(new TierItem(t, StationText.Need(t)));
        PreferHearsMe = s.Ranking.PreferHearsMe;
        OnlyCallingCq = s.Ranking.OnlyCallingCq;
        HideWorked = s.Ranking.HideWorked;
        SkipLongShots = s.Ranking.SkipLongShots;
        ConfirmedOnly = s.Ranking.ConfirmedOnly;
        ConfirmedByLotw = !string.Equals(s.Ranking.ConfirmedBy, "any", StringComparison.OrdinalIgnoreCase);
        LotwUserList = s.Lotw.UserList;
        TqslPath = s.Lotw.TqslPath ?? string.Empty;
        TqslFound = host.Lotw.TqslPath is { } tq ? $"TQSL found: {tq}" : "TQSL not found. Install it from ARRL, or type where tqsl.exe is.";
        foreach (var t in HuntService.ToTargets(s.Hunt.Targets)) HuntRows.Add(new HuntRowViewModel(t, t.Describe(host.Countries)));
        HuntEmpty = HuntRows.Count == 0;
        HuntAllBands = s.Hunt.WatchAllBands;
        HuntSound = s.Hunt.Sound;
        NotifyEnabled = s.Notifications.Enabled;
        NotifyHunt = s.Notifications.Hunt;
        NotifyCallers = s.Notifications.Callers;
        NotifyOnlyInBackground = s.Notifications.OnlyInBackground;
        NotifyBackend = host.Notifications.Backend is NullDesktopNotifier
            ? "This computer has no notification centre Costas can use, so none are shown. The Hunt bar, mini mode and the chime still tell you."
            : "Windows Focus Assist and your desktop's Do Not Disturb hold them back, as they do for any program.";
        var km = s.Hunt.NearbyKm;
        HuntNearby = (s.Appearance.Units == "km" ? km : (int)Math.Round(GreatCircleMiles(km))).ToString(CultureInfo.InvariantCulture);
        HuntNearbyUnit = s.Appearance.Units == "km" ? "km" : "miles";
        HuntBand = HuntBands[0];
        HuntKind = HuntKinds[0];
        KeepCqCallers = KeepChoiceFor(s.Ranking.KeepCqCallersMinutes);
        LogbookEnabled = s.Qrz.LogbookEnabled;
        UploadOnComplete = s.Qrz.UploadOnComplete;
        SyncMinutes = s.Qrz.SyncMinutes.ToString(CultureInfo.InvariantCulture);
        QrzUser = host.Secrets.Get(SecretNames.QrzUsername) ?? string.Empty;
        QrzPassword = host.Secrets.Get(SecretNames.QrzPassword) ?? string.Empty;
        ShowEqsl(active);
        RefreshEqslInbox();
        FeedEnabled = s.PskReporter.FeedEnabled;
        PotaSpots = s.Pota.SpotsEnabled;
        DxpeditionList = s.Dxpeditions.Enabled;
        VerifySuperFox = s.Dxpeditions.VerifySuperFox;
        PreferNewParks = s.Ranking.PreferNewParks;
        CompareNeighbours = s.PskReporter.CompareNeighbours;
        BandActivity = s.PskReporter.BandActivity;
        UploadSpots = s.PskReporter.UploadSpots;
        RbnEnabled = s.ReverseBeacon.Enabled;
        ClusterEnabled = s.DxCluster.Enabled;
        ClusterHost = s.DxCluster.Host;
        ClusterPort = s.DxCluster.Port.ToString(CultureInfo.InvariantCulture);
        UdpEnabled = s.Udp.Enabled;
        UdpAddress = s.Udp.Address;
        UdpPort = s.Udp.Port.ToString(CultureInfo.InvariantCulture);
        RemoteEnabled = s.Remote.Enabled;
        RemotePort = s.Remote.Port.ToString(CultureInfo.InvariantCulture);
        RemoteSignIn = s.Remote.RequireSignIn;
        RemoteProxyAddress = s.Remote.TrustedProxy ?? string.Empty;
        foreach (var u in s.Remote.Users)
            RemoteUsers.Add(RemoteUserRow(u.Id, u.Username, u.Enabled, host.Remote.Control.Users?.HasPassword(u.Id) == true, u.ProfileId));
        RefreshRemote();
        host.Remote.Changed += OnRemoteChanged;
        _remoteLoaded = true;
        if (host.LogCopy is { } copy) copy.StatusChanged += OnLogCopyChanged;
        RefreshLogCopy();
        SaveWav = s.Files.SaveWav;
        Retention = s.Files.RetentionDays.ToString(CultureInfo.InvariantCulture);
        Theme = s.Appearance.Theme;
        Skin = AppSkin.Find(s.Appearance.Skin);
        Units = s.Appearance.Units;
        Layout = Layouts.Contains(s.Appearance.Layout) ? s.Appearance.Layout : "automatic";
        Jt9Path = s.Paths.Jt9 ?? string.Empty;
        HamlibDir = s.Paths.HamlibDir ?? string.Empty;
        CheckUpdates = s.Updates.CheckEnabled;
        UsageEnabled = s.Usage.Enabled;
        ShowDeveloper = s.Developer.Enabled;
        DeveloperMode = s.Developer.Enabled;
        ReplaySamples = s.Developer.ReplaySamples;
        SimulatedPartner = s.Developer.SimulatedPartner;
        DevelopmentBuilds = s.Developer.DevelopmentBuilds;
        SecretsNote = host.Secrets.IsPersistent
            ? "The API key and password are kept in the system credential store, not in the settings file."
            : "No system credential store is available here: the API key and password are kept only until the app closes.";
    }

    /// <summary>Raised when the window should close.</summary>
    public event Action? Close;

    // Page numbers follow the order of the pages in SettingsWindow.axaml.

    /// <summary>Index of the Contacts page (what is sent, when Costas gives up, logging).</summary>
    public const int ContactsPage = 2;

    /// <summary>Index of the PSK and RTTY page.</summary>
    public const int KeyboardPage = 3;

    /// <summary>Index of the Who to call page (after PSK and RTTY).</summary>
    public const int WhoToCallPage = 4;

    /// <summary>The page shown when the window opens (Who to call when opened from "Change").</summary>
    [ObservableProperty]
    public partial int SelectedPage { get; set; }

    /// <summary>Result of "Test key".</summary>
    [ObservableProperty]
    public partial string KeyStatus { get; set; } = string.Empty;

    /// <summary>Tests the logbook API key with QRZ.</summary>
    [RelayCommand]
    private async Task TestKey()
    {
        if (string.IsNullOrWhiteSpace(LogbookKey))
        {
            KeyStatus = "Paste the key first.";
            return;
        }
        KeyStatus = "Testing…";
        var (_, msg) = await _host.Qrz.TestKeyAsync(LogbookKey.Trim(), CancellationToken.None);
        KeyStatus = msg;
    }

    /// <summary>Result of "Test login".</summary>
    [ObservableProperty]
    public partial string LookupStatus { get; set; } = string.Empty;

    /// <summary>Tests the QRZ username and password used for callsign lookups.</summary>
    [RelayCommand]
    private async Task TestLookup()
    {
        if (string.IsNullOrWhiteSpace(QrzUser) || string.IsNullOrEmpty(QrzPassword))
        {
            LookupStatus = "Enter your QRZ username and password first.";
            return;
        }
        LookupStatus = "Testing…";
        var (_, msg) = await _host.Qrz.TestLookupAsync(QrzUser.Trim(), QrzPassword, CancellationToken.None);
        LookupStatus = msg;
    }

    /// <summary>Opens the QRZ Logbook, where the API key is shown.</summary>
    [RelayCommand]
    private static void OpenQrz() => Services.Browser.Open(Services.Browser.QrzLogbook);

    /// <summary>Message line: why Save or Apply stopped.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>Said after Apply stored everything, until the next change is applied or refused.</summary>
    [ObservableProperty]
    public partial string AppliedNote { get; set; } = string.Empty;

    /// <summary>Station profiles as edited here.</summary>
    public ObservableCollection<ProfileDraft> Profiles { get; } = [];

    /// <summary>The profile the Station page shows, which becomes the active one on Save.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    public partial ProfileDraft? SelectedProfile { get; set; }

    partial void OnSelectedProfileChanged(ProfileDraft? value)
    {
        // The list can briefly clear its selection while items change; keep showing the last profile.
        if (value is null || ReferenceEquals(value, _shownProfile)) return;
        StoreFields();
        (Callsign, Grid, Power, MaxPower, LogbookKey) = (value.Callsign, value.Grid, value.Power, value.MaxPower, value.LogbookKey);
        LicenceChoice = LicenceName(value.Licence);
        ShowEqsl(value);
        KeyStatus = string.Empty;
        _shownProfile = value;
    }

    /// <summary>Adds a profile, a copy of the one shown (same radio and audio), and shows it.</summary>
    [RelayCommand]
    private void NewProfile()
    {
        StoreFields();
        var from = SelectedProfile ?? Profiles[0];
        var draft = new ProfileDraft
        {
            Id = "p" + Guid.NewGuid().ToString("N")[..8], CopyOf = from.Id, Name = NewProfileName(Profiles.Select(p => p.Name)),
            Callsign = from.Callsign, Grid = from.Grid, Licence = from.Licence, Power = from.Power, MaxPower = from.MaxPower,
            OperatorName = from.OperatorName, Qth = from.Qth, RigDescription = from.RigDescription, Antenna = from.Antenna,
        };
        Profiles.Add(draft);
        SelectedProfile = draft;
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Adds a profile for another operator at this station and shows it: the same radio, audio, power, grid, QTH, rig and
    /// antenna as the one shown, but no callsign, name or logbook accounts, so nothing of the first operator's goes with
    /// it. Save asks for the callsign.
    /// </summary>
    [RelayCommand]
    private void NewOperatorProfile()
    {
        StoreFields();
        var from = SelectedProfile ?? Profiles[0];
        var draft = new ProfileDraft
        {
            Id = "p" + Guid.NewGuid().ToString("N")[..8], CopyOf = from.Id, ForAnotherOperator = true,
            Name = NewProfileName(Profiles.Select(p => p.Name), "Operator"),
            // Their licence class is theirs to choose: the check stays off until they pick one.
            Licence = UsLicenceClass.NotUs,
            Grid = from.Grid, Power = from.Power, MaxPower = from.MaxPower,
            Qth = from.Qth, RigDescription = from.RigDescription, Antenna = from.Antenna,
        };
        Profiles.Add(draft);
        SelectedProfile = draft;
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Removes the profile shown. The last profile cannot be removed.</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile()
    {
        if (SelectedProfile is not { } gone || Profiles.Count < 2) return;
        _shownProfile = null;
        Profiles.Remove(gone);
        SelectedProfile = Profiles[0];
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    private bool CanDeleteProfile() => Profiles.Count > 1;

    /// <summary>"Portable", then "Portable 2", "Portable 3"… (or another base name) avoiding names in use.</summary>
    public static string NewProfileName(IEnumerable<string> taken, string baseName = "Portable")
    {
        var names = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        for (var i = 2; ; i++)
        {
            var n = $"{baseName} {i}";
            if (!names.Contains(n)) return n;
        }
    }

    private void StoreFields()
    {
        if (_shownProfile is null) return;
        _shownProfile.Callsign = Callsign;
        _shownProfile.Grid = Grid;
        _shownProfile.Licence = LicenceOf(LicenceChoice);
        _shownProfile.Power = Power;
        _shownProfile.MaxPower = MaxPower;
        _shownProfile.OperatorName = OperatorName;
        _shownProfile.Qth = Qth;
        _shownProfile.RigDescription = RigDescription;
        _shownProfile.Antenna = Antenna;
        _shownProfile.LogbookKey = LogbookKey;
        _shownProfile.EqslUser = EqslUser;
        _shownProfile.EqslPassword = EqslPassword;
        _shownProfile.EqslSync = EqslSyncEnabled;
        _shownProfile.EqslUpload = EqslUploadOnComplete;
        _shownProfile.EqslQth = EqslQthNickname;
        _shownProfile.CloudlogUrl = CloudlogUrl;
        _shownProfile.CloudlogKey = CloudlogKey;
        _shownProfile.CloudlogStationId = CloudlogStationId;
        _shownProfile.CloudlogUpload = CloudlogUploadOnComplete;
        _shownProfile.CloudlogDownload = CloudlogDownloadForWorked;
        _shownProfile.ClubLogEmail = ClubLogEmail;
        _shownProfile.ClubLogPassword = ClubLogPassword;
        _shownProfile.ClubLogUpload = ClubLogUploadOnComplete;
        _shownProfile.LotwEnabled = LotwEnabled;
        _shownProfile.LotwUser = LotwUser;
        _shownProfile.LotwPassword = LotwPassword;
        _shownProfile.LotwStationLocation = StationLocation;
        _shownProfile.LotwUpload = LotwUploadOnComplete;
    }

    private void ShowEqsl(ProfileDraft d)
    {
        (EqslUser, EqslPassword, EqslSyncEnabled, EqslUploadOnComplete, EqslQthNickname) = (d.EqslUser, d.EqslPassword, d.EqslSync, d.EqslUpload, d.EqslQth);
        (CloudlogUrl, CloudlogKey, CloudlogStationId, CloudlogUploadOnComplete, CloudlogDownloadForWorked) = (d.CloudlogUrl, d.CloudlogKey, d.CloudlogStationId, d.CloudlogUpload, d.CloudlogDownload);
        CloudlogStations.Clear();
        CloudlogStatus = string.Empty;
        (ClubLogEmail, ClubLogPassword, ClubLogUploadOnComplete) = (d.ClubLogEmail, d.ClubLogPassword, d.ClubLogUpload);
        ClubLogStopText = d.ClubLogStop is { } stop && !d.ClubLogResume ? Ft8Client.Integrations.ClubLog.ClubLogUploadService.Describe(stop) : string.Empty;
        (LotwEnabled, LotwUser, LotwPassword, StationLocation, LotwUploadOnComplete) = (d.LotwEnabled, d.LotwUser, d.LotwPassword, d.LotwStationLocation, d.LotwUpload);
        (OperatorName, Qth, RigDescription, Antenna) = (d.OperatorName, d.Qth, d.RigDescription, d.Antenna);
    }

    /// <summary>
    /// Writes the edited profiles into settings: removed ones are dropped, new ones copied from their source, names, calls,
    /// grids and power set, and <paramref name="active"/> made the active profile.
    /// </summary>
    public static void ApplyProfiles(AppSettings s, IReadOnlyList<ProfileDraft> drafts, string active)
    {
        static int Watts(string text, int fallback) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 1, 2000) : fallback;

        var old = s.Profiles.ToList();
        var result = new List<StationProfile>();
        foreach (var d in drafts)
        {
            var p = old.FirstOrDefault(x => x.Id == d.Id);
            if (p is null)
            {
                var source = old.FirstOrDefault(x => x.Id == d.CopyOf) ?? old.FirstOrDefault() ?? new StationProfile();
                p = source.CopyAs(d.Id, d.Name);
                if (d.ForAnotherOperator) p.ContestExchanges = [];
            }
            p.Name = string.IsNullOrWhiteSpace(d.Name) ? p.Name : d.Name.Trim();
            p.Callsign = d.Callsign.Trim().ToUpperInvariant();
            p.Grid = d.Grid.Trim().ToUpperInvariant();
            p.OperatorName = d.OperatorName.Trim();
            p.Qth = d.Qth.Trim();
            p.RigDescription = d.RigDescription.Trim();
            p.Antenna = d.Antenna.Trim();
            p.LicenceClass = SettingsMapper.LicenceKey(d.Licence);
            p.PowerWatts = Watts(d.Power, p.PowerWatts);
            p.Rig.MaxPowerWatts = string.IsNullOrWhiteSpace(d.MaxPower) ? null : Watts(d.MaxPower, p.Rig.MaxPowerWatts ?? 100);
            p.Eqsl.SyncEnabled = d.EqslSync;
            p.Eqsl.UploadOnComplete = d.EqslUpload;
            p.Eqsl.QthNickname = d.EqslQth.Trim();
            p.Cloudlog.Url = d.CloudlogUrl.Trim();
            p.Cloudlog.StationId = d.CloudlogStationId.Trim();
            p.Cloudlog.UploadOnComplete = d.CloudlogUpload;
            p.Cloudlog.DownloadForWorked = d.CloudlogDownload;
            if (ClubLogStopEnds(p.ClubLog.Stop, d)) p.ClubLog.Stop = null;
            p.ClubLog.Email = d.ClubLogEmail.Trim();
            p.ClubLog.UploadOnComplete = d.ClubLogUpload;
            p.Lotw.Enabled = d.LotwEnabled;
            p.Lotw.Username = string.IsNullOrWhiteSpace(d.LotwUser) ? null : d.LotwUser.Trim();
            p.Lotw.StationLocation = string.IsNullOrWhiteSpace(d.LotwStationLocation) ? null : d.LotwStationLocation.Trim();
            p.Lotw.UploadOnComplete = d.LotwUpload;
            result.Add(p);
        }
        s.Profiles = result;
        s.ActiveProfile = active;
    }

    /// <summary>
    /// True when saving this draft ends a Club Log login stop: the operator resumed uploads, or changed the email,
    /// Application Password or callsign Club Log refused. Club Log asks for no more requests until then.
    /// </summary>
    public static bool ClubLogStopEnds(ClubLogStop? stop, ProfileDraft d) =>
        stop is not null
        && (d.ClubLogResume
            || !string.Equals(d.ClubLogPassword.Trim(), d.ClubLogPasswordSaved.Trim(), StringComparison.Ordinal)
            || !stop.Covers(d.ClubLogEmail, d.Callsign));

    /// <summary>Call.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessagePreview), nameof(MessageProblem))]
    public partial string Callsign { get; set; }

    /// <summary>Grid.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessagePreview), nameof(MessageProblem))]
    public partial string Grid { get; set; }

    /// <summary>Power in watts.</summary>
    [ObservableProperty]
    public partial string Power { get; set; }

    /// <summary>The radio's top power in watts; empty to use what Hamlib reports.</summary>
    [ObservableProperty]
    public partial string MaxPower { get; set; }

    /// <summary>Tx gain dBFS on the current band.</summary>
    [ObservableProperty]
    public partial string TxGain { get; set; }

    /// <summary>"Audio level on 20m (dB)".</summary>
    public string TxGainLabel { get; }

    /// <summary>Adjust the transmit level from the radio's ALC.</summary>
    [ObservableProperty]
    public partial bool AutoTxLevel { get; set; }

    /// <summary>Stop a transmission when the SWR stays at 3.0 or above.</summary>
    [ObservableProperty]
    public partial bool StopOnHighSwr { get; set; }

    /// <summary>Watchdog minutes.</summary>
    [ObservableProperty]
    public partial string Watchdog { get; set; }

    /// <summary>Retry limit.</summary>
    [ObservableProperty]
    public partial string RetryLimit { get; set; }

    /// <summary>Sign-offs.</summary>
    public IReadOnlyList<string> Signoffs { get; } = ["RR73", "RRR"];

    /// <summary>Sign-off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessagePreview), nameof(MessageProblem))]
    public partial string Signoff { get; set; }

    /// <summary>CQ modifier (DX, POTA, ...); empty for a plain CQ.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessagePreview), nameof(MessageProblem))]
    public partial string CqModifier { get; set; }

    /// <summary>Send my grid when answering a CQ.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessagePreview), nameof(MessageProblem))]
    public partial bool GridInReply { get; set; }

    /// <summary>Choices for my last message when I answered a CQ.</summary>
    public IReadOnlyList<string> AnswerSignoffs { get; } = ["73", "RR73"];

    /// <summary>My last message when I answered a CQ.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessagePreview), nameof(MessageProblem))]
    public partial string AnswerSignoff { get; set; }

    /// <summary>Free text sent after my last message; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessagePreview), nameof(MessageProblem))]
    public partial string FreeTextAfter { get; set; }

    /// <summary>The messages as they will be sent with the settings on screen, one per line.</summary>
    public string MessagePreview
    {
        get
        {
            var s = MessageSettings();
            var me = Callsign.Trim().ToUpperInvariant();
            if (me.Length == 0) me = "MYCALL";
            var grid = Core.Messages.Grid.Grid4(Grid.Trim().ToUpperInvariant()) ?? string.Empty;
            const string dx = "THEIRCALL";
            var lines = new List<string>
            {
                $"Calling CQ:        {OutgoingMessages.Cq(me, grid, s)}",
                $"Report:            {OutgoingMessages.Report(dx, me, "-10")}",
                $"Sign-off:          {OutgoingMessages.Signoff(dx, me, s.Signoff)}",
                string.Empty,
                $"Answering a CQ:    {OutgoingMessages.Reply(dx, me, grid, s)}",
                $"Roger and report:  {OutgoingMessages.Report(dx, me, "R-10")}",
                $"Last message:      {OutgoingMessages.Final(dx, me, s)}",
            };
            if (OutgoingMessages.FreeTextAfter(s) is { } free) lines.Add($"Then:              {free}");
            return string.Join('\n', lines);
        }
    }

    /// <summary>Why the message settings cannot be used, or empty.</summary>
    public string MessageProblem => OutgoingMessages.Problem(MessageSettings(), Callsign, Grid) ?? string.Empty;

    private ContactSettings MessageSettings() => new()
    {
        Signoff = Signoff == "RRR" ? "RRR" : "RR73",
        CqModifier = CqModifier.Trim().ToUpperInvariant(),
        GridInReply = GridInReply,
        AnswerSignoff = AnswerSignoff == "RR73" ? "RR73" : "73",
        FreeTextAfter = FreeText(),
    };

    private string FreeText() => string.Join(' ', FreeTextAfter.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Auto log.</summary>
    [ObservableProperty]
    public partial bool AutoLog { get; set; }

    /// <summary>Chime when a new station calls me.</summary>
    [ObservableProperty]
    public partial bool ChimeOnCaller { get; set; }

    /// <summary>Answer everyone waiting for me, one after another, after each logged contact.</summary>
    [ObservableProperty]
    public partial bool AnswerAllInTurn { get; set; }

    /// <summary>Keep calling a station that is working someone else instead of stopping.</summary>
    [ObservableProperty]
    public partial bool KeepCallingWhenBusy { get; set; }

    /// <summary>RTTY speeds offered, in baud.</summary>
    public IReadOnlyList<string> RttySpeeds { get; } = [.. RttyFormat.Speeds.Select(b => b.ToString("0.##", CultureInfo.InvariantCulture))];

    /// <summary>RTTY speed.</summary>
    [ObservableProperty]
    public partial string RttySpeed { get; set; }

    /// <summary>RTTY shifts offered, in hertz.</summary>
    public IReadOnlyList<string> RttyShifts { get; } = [.. RttyFormat.Shifts.Select(h => h.ToString(CultureInfo.InvariantCulture))];

    /// <summary>RTTY shift.</summary>
    [ObservableProperty]
    public partial string RttyShift { get; set; }

    /// <summary>Read RTTY reversed.</summary>
    [ObservableProperty]
    public partial bool RttyReverse { get; set; }

    /// <summary>RTTY unshift-on-space.</summary>
    [ObservableProperty]
    public partial bool RttyUnshiftOnSpace { get; set; }

    /// <summary>RTTY power as a share of the radio's setting, in percent.</summary>
    [ObservableProperty]
    public partial string RttyPowerPercent { get; set; }

    /// <summary>My first name, for {MYNAME}.</summary>
    [ObservableProperty]
    public partial string OperatorName { get; set; } = string.Empty;

    /// <summary>Where I am, for {MYQTH}.</summary>
    [ObservableProperty]
    public partial string Qth { get; set; } = string.Empty;

    /// <summary>My radio, for {MYRIG}.</summary>
    [ObservableProperty]
    public partial string RigDescription { get; set; } = string.Empty;

    /// <summary>My antenna, for {MYANT}.</summary>
    [ObservableProperty]
    public partial string Antenna { get; set; } = string.Empty;

    /// <summary>The PSK and RTTY macros, F1 up.</summary>
    public ObservableCollection<MacroEditViewModel> Macros { get; } = [];

    /// <summary>The fields a macro can hold, for the help line.</summary>
    public string MacroFields { get; } = string.Join(" ", Core.Keyboard.MacroText.Fields.Select(f => "{" + f + "}"));

    private void LoadMacros(IReadOnlyList<Core.Keyboard.Macro> macros)
    {
        Macros.Clear();
        // F1 to F5 always have a row, so a default can be put back by typing it; more keys can be added below.
        for (var key = 1; key <= Math.Max(5, macros.Count == 0 ? 0 : macros.Max(m => m.Key)); key++)
        {
            var m = macros.FirstOrDefault(x => x.Key == key);
            Macros.Add(new MacroEditViewModel(key, m?.Label ?? string.Empty, m?.Text ?? string.Empty));
        }
    }

    /// <summary>Puts the default macros back.</summary>
    [RelayCommand]
    private void RestoreMacros() => LoadMacros(Core.Keyboard.Macro.Defaults);

    /// <summary>Adds a row for the next function key, up to F12.</summary>
    [RelayCommand]
    private void AddMacro()
    {
        if (Macros.Count < 12) Macros.Add(new MacroEditViewModel(Macros.Count + 1, string.Empty, string.Empty));
    }

    /// <summary>
    /// Removes a key's macro: the last row above F5 goes, any other row is emptied (a key with no text has no macro once
    /// saved).
    /// </summary>
    [RelayCommand]
    private void RemoveMacro(MacroEditViewModel? row)
    {
        if (row is null) return;
        if (row.Key > 5 && ReferenceEquals(row, Macros[^1]))
        {
            Macros.Remove(row);
            return;
        }
        row.Label = string.Empty;
        row.Text = string.Empty;
    }

    // The last warning about the macros, which a second Save accepts.
    private string? _macroWarning;

    /// <summary>
    /// A problem with the macros as typed, or null. Blocking problems stop the save: a name in braces that is not a
    /// field, or a character neither PSK nor RTTY can send. Warnings stop only the first Save: a character RTTY cannot
    /// send (that key is then refused in RTTY), and every macro empty (the defaults come back).
    /// </summary>
    internal static (string Text, bool Blocking)? MacroCheck(IEnumerable<MacroEditViewModel> rows)
    {
        var used = rows.Where(m => !string.IsNullOrWhiteSpace(m.Text)).ToList();
        if (used.Count == 0)
            return ("Every macro is empty, so the default macros will come back. Press Save again to use the defaults, or type at least one macro.", false);
        var all = Core.Keyboard.MacroText.Fields.ToDictionary(f => f, _ => (string?)"X", StringComparer.Ordinal);
        static string Name(MacroEditViewModel m) => string.IsNullOrWhiteSpace(m.Label) ? m.KeyName : $"{m.KeyName} {m.Label.Trim()}";
        foreach (var m in used)
        {
            var text = Core.Keyboard.MacroText.Expand(m.Text, all, out var unknown);
            if (unknown.Count > 0)
            {
                return ($"{Name(m)}: {string.Join(" ", unknown.Select(u => "{" + u + "}"))} {(unknown.Count == 1 ? "is not a field" : "are not fields")}. "
                    + $"A macro can hold {string.Join(" ", all.Keys.Select(f => "{" + f + "}"))}.", true);
            }
            var never = KeyboardCharacters.Unsendable(false, text);
            if (never.Count > 0) return ($"{Name(m)}: PSK and RTTY cannot send {KeyboardCharacters.Show(never)}. Remove {(never.Count == 1 ? "it" : "them")}.", true);
        }
        foreach (var m in used)
        {
            var rtty = KeyboardCharacters.Unsendable(true, Core.Keyboard.MacroText.Expand(m.Text, all, out _));
            if (rtty.Count > 0)
            {
                return ($"{Name(m)}: RTTY cannot send {KeyboardCharacters.Show(rtty)}, so in RTTY that key will be refused. "
                    + $"Press Save again to keep it, or remove {(rtty.Count == 1 ? "it" : "them")}.", false);
            }
        }
        return null;
    }

    /// <summary>Parities.</summary>
    public IReadOnlyList<string> Parities { get; } = ["even", "odd"];

    /// <summary>CQ parity.</summary>
    [ObservableProperty]
    public partial string CqParity { get; set; }

    /// <summary>Correct Costas's clock from internet time.</summary>
    [ObservableProperty]
    public partial bool CorrectClock { get; set; }

    /// <summary>The licence classes offered on the Station page, in <see cref="SettingsMapper.LicenceKeys"/> order.</summary>
    public IReadOnlyList<string> LicenceChoices { get; } = [.. SettingsMapper.LicenceKeys.Select(k => LicenceName(k.Class))];

    /// <summary>The shown profile's licence class, as named in <see cref="LicenceChoices"/>.</summary>
    [ObservableProperty]
    public partial string LicenceChoice { get; set; } = string.Empty;

    /// <summary>"Amateur Extra", "General"… or "Not US / don't check".</summary>
    public static string LicenceName(UsLicenceClass c) => c == UsLicenceClass.NotUs ? "Not US / don't check" : UsDataSegments.Name(c);

    /// <summary>The class a <see cref="LicenceChoices"/> name stands for; Extra for anything else.</summary>
    public static UsLicenceClass LicenceOf(string? name) =>
        SettingsMapper.LicenceKeys.Select(k => k.Class).FirstOrDefault(c => LicenceName(c) == name, UsLicenceClass.AmateurExtra);

    /// <summary>Clock block seconds.</summary>
    [ObservableProperty]
    public partial string ClockBlock { get; set; }

    /// <summary>Latest start for a call or CQ the operator clicks, in seconds after the slot's Tx start.</summary>
    [ObservableProperty]
    public partial string CommandStartLimit { get; set; }

    /// <summary>Double-clicking another station during a contact abandons the contact and calls it (FT8 and FT4).</summary>
    [ObservableProperty]
    public partial bool DoubleClickSwitches { get; set; }

    /// <summary>Tier order.</summary>
    public ObservableCollection<TierItem> TierOrder { get; } = [];

    /// <summary>Prefer stations that hear me.</summary>
    [ObservableProperty]
    public partial bool PreferHearsMe { get; set; }

    /// <summary>Only stations calling CQ.</summary>
    [ObservableProperty]
    public partial bool OnlyCallingCq { get; set; }

    /// <summary>Hide worked.</summary>
    [ObservableProperty]
    public partial bool HideWorked { get; set; }

    /// <summary>Skip long shots.</summary>
    [ObservableProperty]
    public partial bool SkipLongShots { get; set; }

    /// <summary>Confirmed only.</summary>
    [ObservableProperty]
    public partial bool ConfirmedOnly { get; set; }

    /// <summary>Count only LoTW confirmations (what DXCC and WAS credit); otherwise QRZ, eQSL and cards count too.</summary>
    [ObservableProperty]
    public partial bool ConfirmedByLotw { get; set; }

    // ------------------------------------------------------------ LoTW

    /// <summary>Download LoTW confirmations.</summary>
    [ObservableProperty]
    public partial bool LotwEnabled { get; set; }

    /// <summary>LoTW login name.</summary>
    [ObservableProperty]
    public partial string LotwUser { get; set; } = string.Empty;

    /// <summary>LoTW website password (kept in the credential store).</summary>
    [ObservableProperty]
    public partial string LotwPassword { get; set; } = string.Empty;

    /// <summary>Upload each contact through TQSL when it is logged.</summary>
    [ObservableProperty]
    public partial bool LotwUploadOnComplete { get; set; }

    /// <summary>Download ARRL's list of LoTW users.</summary>
    [ObservableProperty]
    public partial bool LotwUserList { get; set; }

    /// <summary>Path to tqsl.exe, blank to look in the usual place.</summary>
    [ObservableProperty]
    public partial string TqslPath { get; set; } = string.Empty;

    /// <summary>TQSL Station Location name.</summary>
    [ObservableProperty]
    public partial string StationLocation { get; set; } = string.Empty;

    /// <summary>Where TQSL was found, or that it was not.</summary>
    public string TqslFound { get; }

    // ------------------------------------------------------------ Hunt list

    /// <summary>The Hunt list.</summary>
    public ObservableCollection<HuntRowViewModel> HuntRows { get; } = [];

    /// <summary>The Hunt list is empty.</summary>
    [ObservableProperty]
    public partial bool HuntEmpty { get; set; }

    /// <summary>Kinds of entry, as shown.</summary>
    public IReadOnlyList<string> HuntKinds { get; } = ["Country", "Call", "US state", "Grid", "Any new country"];

    /// <summary>Kind of the entry being added.</summary>
    [ObservableProperty]
    public partial string HuntKind { get; set; } = "Country";

    /// <summary>What is typed for the entry being added.</summary>
    [ObservableProperty]
    public partial string HuntValue { get; set; } = string.Empty;

    /// <summary>The text box is shown (not for Any new country).</summary>
    public bool HuntNeedsValue => HuntKind != "Any new country";

    partial void OnHuntKindChanged(string value) => OnPropertyChanged(nameof(HuntNeedsValue));

    /// <summary>Band choices: Any band, then the bands.</summary>
    public IReadOnlyList<string> HuntBands { get; } =
        ["Any band", .. Core.Bands.BandPlan.Bands.Select(b => Core.Bands.BandPlan.Display(b.Band))];

    /// <summary>Band of the entry being added.</summary>
    [ObservableProperty]
    public partial string HuntBand { get; set; } = "Any band";

    /// <summary>Why the entry could not be added, or blank.</summary>
    [ObservableProperty]
    public partial string HuntError { get; set; } = string.Empty;

    /// <summary>Watch every band through PSK Reporter.</summary>
    [ObservableProperty]
    public partial bool HuntAllBands { get; set; }

    /// <summary>Chime and taskbar flash with an alert.</summary>
    [ObservableProperty]
    public partial bool HuntSound { get; set; }

    /// <summary>Desktop notifications at all.</summary>
    [ObservableProperty]
    public partial bool NotifyEnabled { get; set; }

    /// <summary>A notification for a Hunt list alert.</summary>
    [ObservableProperty]
    public partial bool NotifyHunt { get; set; }

    /// <summary>A notification when a station calls me.</summary>
    [ObservableProperty]
    public partial bool NotifyCallers { get; set; }

    /// <summary>Only while Costas is minimized, in mini mode or behind another window.</summary>
    [ObservableProperty]
    public partial bool NotifyOnlyInBackground { get; set; }

    /// <summary>What the notifications page says about this computer's notification centre.</summary>
    public string NotifyBackend { get; }

    /// <summary>How near (in the display unit) a PSK Reporter receiver must be.</summary>
    [ObservableProperty]
    public partial string HuntNearby { get; set; } = string.Empty;

    /// <summary>"miles" or "km".</summary>
    public string HuntNearbyUnit { get; } = "miles";

    /// <summary>Adds the typed entry to the Hunt list.</summary>
    [RelayCommand]
    private void AddHunt()
    {
        var kind = HuntKind switch
        {
            "Call" => Core.Hunting.HuntTargetKind.Call,
            "US state" => Core.Hunting.HuntTargetKind.State,
            "Grid" => Core.Hunting.HuntTargetKind.Grid,
            "Any new country" => Core.Hunting.HuntTargetKind.NewCountry,
            _ => Core.Hunting.HuntTargetKind.Country,
        };
        var band = HuntBand == "Any band" ? null : HuntBand;
        var t = Core.Hunting.HuntTarget.Create(kind, HuntValue, band, _host.Countries, out var error);
        if (t is null)
        {
            HuntError = error;
            return;
        }
        if (HuntRows.Any(r => r.Target == t))
        {
            HuntError = $"{t.Describe(_host.Countries)} is already on the list.";
            return;
        }
        HuntRows.Add(new HuntRowViewModel(t, t.Describe(_host.Countries)));
        HuntEmpty = false;
        HuntError = string.Empty;
        HuntValue = string.Empty;
    }

    /// <summary>Removes an entry from the Hunt list.</summary>
    [RelayCommand]
    private void RemoveHunt(HuntRowViewModel? row)
    {
        if (row is not null) HuntRows.Remove(row);
        HuntEmpty = HuntRows.Count == 0;
    }

    private static double GreatCircleMiles(int km) => Core.Geo.GreatCircle.ToMiles(km);

    /// <summary>Choices for how long a CQ caller stays in Next in line after it was last heard, with their minutes.</summary>
    public static readonly IReadOnlyList<(string Text, int Minutes)> KeepCqChoices =
    [
        ("Only while heard every slot", 0), ("1 minute", 1), ("2 minutes", 2), ("3 minutes", 3), ("5 minutes", 5),
        ("10 minutes", 10), ("15 minutes", 15),
    ];

    /// <summary>The keep-listed choices as shown.</summary>
    public IReadOnlyList<string> KeepCqCallersChoices { get; } = KeepCqChoices.Select(c => c.Text).ToList();

    /// <summary>How long a CQ caller stays in Next in line after it was last heard.</summary>
    [ObservableProperty]
    public partial string KeepCqCallers { get; set; }

    /// <summary>The choice for a stored number of minutes (the nearest one not shorter, or the longest).</summary>
    public static string KeepChoiceFor(int minutes)
    {
        foreach (var (text, m) in KeepCqChoices) if (m >= minutes) return text;
        return KeepCqChoices[^1].Text;
    }

    /// <summary>Minutes for a shown choice; 2 when it is not one of the choices.</summary>
    public static int KeepMinutesFor(string? choice)
    {
        foreach (var (text, m) in KeepCqChoices) if (text == choice) return m;
        return 2;
    }

    /// <summary>QRZ logbook.</summary>
    [ObservableProperty]
    public partial bool LogbookEnabled { get; set; }


    /// <summary>Upload on completion.</summary>
    [ObservableProperty]
    public partial bool UploadOnComplete { get; set; }

    /// <summary>Sync minutes.</summary>
    [ObservableProperty]
    public partial string SyncMinutes { get; set; }

    /// <summary>Logbook key.</summary>
    [ObservableProperty]
    public partial string LogbookKey { get; set; }

    /// <summary>QRZ username, for callsign lookups.</summary>
    [ObservableProperty]
    public partial string QrzUser { get; set; }

    /// <summary>QRZ password, for callsign lookups.</summary>
    [ObservableProperty]
    public partial string QrzPassword { get; set; }

    /// <summary>Where secrets are kept.</summary>
    public string SecretsNote { get; }

    /// <summary>Read the eQSL inbox automatically.</summary>
    [ObservableProperty]
    public partial bool EqslSyncEnabled { get; set; }

    /// <summary>Last manual inbox-sync result.</summary>
    [ObservableProperty]
    public partial string EqslSyncStatus { get; set; } = string.Empty;

    /// <summary>Most recent incoming cards, including unmatched cards.</summary>
    public ObservableCollection<string> EqslInboxCards { get; } = [];

    /// <summary>Wavelog/Cloudlog site address.</summary>
    [ObservableProperty]
    public partial string CloudlogUrl { get; set; } = string.Empty;

    /// <summary>Wavelog/Cloudlog API key.</summary>
    [ObservableProperty]
    public partial string CloudlogKey { get; set; } = string.Empty;

    /// <summary>Which API the key will be used with, shown under it; empty while no key is typed.</summary>
    public string CloudlogApiText => string.IsNullOrWhiteSpace(CloudlogKey) ? string.Empty
        : Ft8Client.Integrations.Cloudlog.CloudlogClient.ApiFor(CloudlogKey) == Ft8Client.Integrations.Cloudlog.WavelogApi.V2
            ? "Using the Wavelog v2 API (token)."
            : "Using the v1 API (Wavelog or Cloudlog key).";

    /// <summary>True when the key is a Wavelog v2 token, the only kind the download works with.</summary>
    public bool CloudlogCanDownload =>
        Ft8Client.Integrations.Cloudlog.CloudlogClient.ApiFor(CloudlogKey) == Ft8Client.Integrations.Cloudlog.WavelogApi.V2;

    partial void OnCloudlogKeyChanged(string value)
    {
        OnPropertyChanged(nameof(CloudlogApiText));
        OnPropertyChanged(nameof(CloudlogCanDownload));
    }

    /// <summary>Download the Wavelog log for worked-before (v2 tokens only).</summary>
    [ObservableProperty]
    public partial bool CloudlogDownloadForWorked { get; set; }

    /// <summary>Wavelog/Cloudlog station location id.</summary>
    [ObservableProperty]
    public partial string CloudlogStationId { get; set; } = string.Empty;

    /// <summary>Club Log account email.</summary>
    [ObservableProperty]
    public partial string ClubLogEmail { get; set; } = string.Empty;

    /// <summary>Club Log Application Password.</summary>
    [ObservableProperty]
    public partial string ClubLogPassword { get; set; } = string.Empty;

    /// <summary>Upload each new contact to Club Log.</summary>
    [ObservableProperty]
    public partial bool ClubLogUploadOnComplete { get; set; }

    /// <summary>Why Club Log uploads are stopped (it refused the login), or blank.</summary>
    [ObservableProperty]
    public partial string ClubLogStopText { get; set; } = string.Empty;

    /// <summary>
    /// Resumes Club Log uploads after a refused login, once saved. Only for a login fixed on the Club Log site: Club Log
    /// firewalls an address that keeps sending a login it refused.
    /// </summary>
    [RelayCommand]
    private void ResumeClubLog()
    {
        if (_shownProfile is null) return;
        _shownProfile.ClubLogResume = true;
        ClubLogStopText = string.Empty;
    }

    /// <summary>Why Club Log uploads cannot run in this build, or blank.</summary>
    public string ClubLogBuildNote => AppHost.ClubLogApiKey is null
        ? "This build of Costas has no Club Log application key, so it cannot upload to Club Log. Release builds have one."
        : string.Empty;

    /// <summary>Upload each new contact to Wavelog/Cloudlog.</summary>
    [ObservableProperty]
    public partial bool CloudlogUploadOnComplete { get; set; }

    /// <summary>Result of the last Test.</summary>
    [ObservableProperty]
    public partial string CloudlogStatus { get; set; } = string.Empty;

    /// <summary>Station locations found by Test, to pick from.</summary>
    public ObservableCollection<CloudlogStationChoice> CloudlogStations { get; } = [];

    /// <summary>The station location picked from <see cref="CloudlogStations"/>.</summary>
    [ObservableProperty]
    public partial CloudlogStationChoice? CloudlogStation { get; set; }

    partial void OnCloudlogStationChanged(CloudlogStationChoice? value)
    {
        if (value is not null) CloudlogStationId = value.Id;
    }

    /// <summary>Checks the address and key and lists the account's station locations.</summary>
    [RelayCommand]
    private async Task TestCloudlog()
    {
        CloudlogStatus = "Asking the logbook…";
        var (message, stations) = await _host.Cloudlog.TestAsync(CloudlogUrl, CloudlogKey, CancellationToken.None, CloudlogUploadOnComplete, CloudlogDownloadForWorked);
        CloudlogStatus = message;
        CloudlogStations.Clear();
        foreach (var st in stations) CloudlogStations.Add(new CloudlogStationChoice(st.Id, st.Name, st.Callsign, st.Active));
        CloudlogStation = CloudlogStations.FirstOrDefault(c => c.Id == CloudlogStationId.Trim())
            ?? CloudlogStations.FirstOrDefault(c => string.Equals(c.Callsign, Callsign.Trim(), StringComparison.OrdinalIgnoreCase) && c.Active)
            ?? CloudlogStations.FirstOrDefault(c => string.Equals(c.Callsign, Callsign.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshEqslInbox()
    {
        EqslInboxCards.Clear();
        foreach (var card in _host.EqslInbox.Read(_host.Settings.Current.Profile.Id).Take(100))
            EqslInboxCards.Add($"{card["qso_date"]} {card["time_on"]} UTC · {card["call"]} · {card["band"]} · {card["submode"] ?? card["mode"]}");
    }

    /// <summary>Tests the account by syncing the inbox and matching confirmations.</summary>
    [RelayCommand]
    private async Task SyncEqsl()
    {
        EqslSyncStatus = "Reading eQSL inbox…";
        EqslSyncStatus = await _host.Eqsl.SyncNowAsync(EqslUser.Trim(), EqslPassword, EqslQthNickname.Trim(), CancellationToken.None);
        RefreshEqslInbox();
    }

    /// <summary>Upload newly logged contacts to eQSL.</summary>
    [ObservableProperty]
    public partial bool EqslUploadOnComplete { get; set; }

    /// <summary>eQSL account username.</summary>
    [ObservableProperty]
    public partial string EqslUser { get; set; } = string.Empty;

    /// <summary>eQSL account password, saved only in the secret store.</summary>
    [ObservableProperty]
    public partial string EqslPassword { get; set; } = string.Empty;

    /// <summary>eQSL QTH nickname for this station profile.</summary>
    [ObservableProperty]
    public partial string EqslQthNickname { get; set; } = string.Empty;

    /// <summary>PSK feed.</summary>
    [ObservableProperty]
    public partial bool FeedEnabled { get; set; }

    /// <summary>Read the POTA spot feed to show parks under Why.</summary>
    [ObservableProperty]
    public partial bool PotaSpots { get; set; }

    /// <summary>Read NG3K's announced DXpeditions.</summary>
    [ObservableProperty]
    public partial bool DxpeditionList { get; set; }

    /// <summary>Connect to a DX cluster node and show its spots.</summary>
    [ObservableProperty]
    public partial bool ClusterEnabled { get; set; }

    /// <summary>The DX cluster node's host name.</summary>
    [ObservableProperty]
    public partial string ClusterHost { get; set; } = string.Empty;

    /// <summary>The DX cluster node's telnet port.</summary>
    [ObservableProperty]
    public partial string ClusterPort { get; set; } = "7300";

    /// <summary>
    /// Well-known public nodes offered as suggestions, none endorsed or preferred (from the CC User manual's list, checked
    /// September 2026). Picking one fills the host and port.
    /// </summary>
    public IReadOnlyList<string> ClusterSuggestions { get; } = ["dxspots.com:7300", "dxc.n4zkf.com:7373", "ea4ure.com:7300"];

    /// <summary>The suggestion picked (it fills <see cref="ClusterHost"/> and <see cref="ClusterPort"/>).</summary>
    [ObservableProperty]
    public partial string? ClusterSuggestion { get; set; }

    partial void OnClusterSuggestionChanged(string? value)
    {
        if (value is null || value.LastIndexOf(':') is var colon && colon <= 0) return;
        ClusterHost = value[..colon];
        ClusterPort = value[(colon + 1)..];
    }

    /// <summary>Check SuperFox signatures online.</summary>
    [ObservableProperty]
    public partial bool VerifySuperFox { get; set; }

    /// <summary>Rank activators at parks not in the log higher.</summary>
    [ObservableProperty]
    public partial bool PreferNewParks { get; set; }

    /// <summary>Compare my signal with stations near me.</summary>
    [ObservableProperty]
    public partial bool CompareNeighbours { get; set; }

    /// <summary>Show in the band menu where stations near me are heard on every band.</summary>
    [ObservableProperty]
    public partial bool BandActivity { get; set; }

    /// <summary>Upload spots.</summary>
    [ObservableProperty]
    public partial bool UploadSpots { get; set; }

    /// <summary>Follow Reverse Beacon Network spots of my callsign.</summary>
    [ObservableProperty]
    public partial bool RbnEnabled { get; set; }

    /// <summary>UDP enabled.</summary>
    [ObservableProperty]
    public partial bool UdpEnabled { get; set; }

    /// <summary>UDP address.</summary>
    [ObservableProperty]
    public partial string UdpAddress { get; set; }

    /// <summary>UDP port.</summary>
    [ObservableProperty]
    public partial string UdpPort { get; set; }

    /// <summary>Serve the remote page.</summary>
    [ObservableProperty]
    public partial bool RemoteEnabled { get; set; }

    /// <summary>Remote page port.</summary>
    [ObservableProperty]
    public partial string RemotePort { get; set; }

    /// <summary>Every device must sign in with a username and password.</summary>
    [ObservableProperty]
    public partial bool RemoteSignIn { get; set; }

    /// <summary>The address of a reverse proxy on another computer; empty for one on this computer or none.</summary>
    [ObservableProperty]
    public partial string RemoteProxyAddress { get; set; } = string.Empty;

    /// <summary>Said when a request came from this computer itself while sign-in is off; empty otherwise.</summary>
    [ObservableProperty]
    public partial string RemoteLoopbackWarning { get; set; } = string.Empty;

    /// <summary>Why the proxy address cannot be used, or null.</summary>
    internal static string? ProxyAddressCheck(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!System.Net.IPAddress.TryParse(text.Trim(), out var a))
            return $"The proxy address {text.Trim()} is not an IP address such as 192.168.1.10 (Mobile connection page).";
        if (!Core.Remote.LocalNetwork.Contains(a) || System.Net.IPAddress.IsLoopback(a))
            return "The proxy address must be another computer on your home network; a proxy on this computer needs no address.";
        return null;
    }

    /// <summary>Who may sign in to the remote page.</summary>
    public ObservableCollection<RemoteUserEditViewModel> RemoteUsers { get; } = [];

    /// <summary>Index of the Mobile connection page.</summary>
    public const int MobilePage = 8;

    [RelayCommand]
    private void AddRemoteUser()
    {
        // A new user opens ready to fill in.
        var row = RemoteUserRow(Guid.NewGuid().ToString("N"), string.Empty, true, false);
        RemoteUsers.Add(row);
        row.EditCommand.Execute(null);
    }

    private RemoteUserEditViewModel RemoteUserRow(string id, string username, bool enabled, bool hasPassword, string? profileId = null)
    {
        var row = new RemoteUserEditViewModel(id, username, enabled, hasPassword, profileId)
        {
            OnRemove = r => RemoteUsers.Remove(r),
            OnSignOut = SignOutRemoteUser,
            // One editor open at a time keeps the list short.
            OnEdit = r =>
            {
                foreach (var other in RemoteUsers.Where(o => !ReferenceEquals(o, r))) other.IsEditing = false;
                r.SetProfileChoices(ProfileChoicesNow());
            },
        };
        row.SetProfileChoices(ProfileChoicesNow());
        return row;
    }

    // What a remote user can operate as: whichever profile is in use, or one of the profiles (as edited so far).
    private List<ProfileChoice> ProfileChoicesNow()
    {
        string CallOf(ProfileDraft d) => ((ReferenceEquals(d, _shownProfile) ? Callsign : d.Callsign) ?? string.Empty).Trim().ToUpperInvariant();
        return [new ProfileChoice(null, ProfileChoice.NoneLabel), .. Profiles.Select(d => new ProfileChoice(d.Id,
            CallOf(d).Length > 0 ? $"{d.Name} ({CallOf(d)})" : d.Name))];
    }

    // Takes effect at once: a lost or borrowed phone must be cut off without waiting for Save.
    private void SignOutRemoteUser(RemoteUserEditViewModel row)
    {
        if (_host.Remote.Control.Users is not { } users) return;
        try
        {
            users.SignOutEverywhere(row.Id);
            row.Status = "Signed out on every device.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            row.Status = SecretStoreProblem.Describe(ex.Message);
        }
    }

    /// <summary>Why the mobile users cannot be saved as they are, or null.</summary>
    internal static string? RemoteUsersCheck(bool signIn, IEnumerable<RemoteUserEditViewModel> rows)
    {
        var list = rows.ToList();
        if (list.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Username)) is not null)
            return "Enter a username for every mobile user, or remove the row (Mobile connection page).";
        if (list.FirstOrDefault(r => r.Username.Trim().Length > 64) is { } longName)
            return $"The username {longName.Username.Trim()[..20]}… is too long: 64 characters at most.";
        if (list.GroupBy(r => r.Username.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice)
            return $"Two mobile users are both called {twice.Key}.";
        foreach (var r in list)
        {
            if (r.NewPassword.Length > 0 && Core.Remote.PasswordHash.Problem(r.NewPassword) is { } why)
                return $"Password for {r.Username.Trim()}: {why}";
            if (r.NewPassword.Length == 0 && !r.HasPassword && r.Enabled)
                return $"Set a password for {r.Username.Trim()}.";
        }
        if (signIn && !list.Any(r => r.Enabled && (r.HasPassword || r.NewPassword.Length > 0)))
            return "Add a user with a password before requiring sign-in: otherwise no phone could connect.";
        return null;
    }

    /// <summary>Where to point the phone, or why the page is not served.</summary>
    [ObservableProperty]
    public partial string RemoteStatus { get; set; } = string.Empty;

    /// <summary>What "Check why a phone can't connect" found, one finding per line; empty before a check.</summary>
    [ObservableProperty]
    public partial string PhoneCheck { get; set; } = string.Empty;

    /// <summary>True while the phone connection check runs.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckPhoneCommand))]
    public partial bool PhoneChecking { get; set; }

    /// <summary>Looks for why a phone cannot reach the page (saved settings, this computer's firewall). Changes nothing.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckPhone))]
    private async Task CheckPhoneAsync()
    {
        PhoneChecking = true;
        PhoneCheck = "Checking…";
        try
        {
            var r = _host.Settings.Current.Remote;
            var lines = await Ft8Client.App.Remote.RemoteDiagnostics.RunAsync(_host.Remote, r.Enabled, r.Port, CancellationToken.None);
            PhoneCheck = string.Join(Environment.NewLine + Environment.NewLine, lines);
        }
        finally
        {
            PhoneChecking = false;
        }
    }

    private bool CanCheckPhone() => !PhoneChecking;

    /// <summary>The address to open on the phone, shown large.</summary>
    [ObservableProperty]
    public partial string PhoneUrl { get; set; } = string.Empty;

    /// <summary>The other addresses of this computer, in case the first is a virtual adapter; blank when there are none.</summary>
    [ObservableProperty]
    public partial string PhoneOtherUrls { get; set; } = string.Empty;

    /// <summary>True while the page is served.</summary>
    [ObservableProperty]
    public partial bool RemoteRunning { get; set; }

    /// <summary>The Tailscale address to open on the phone away from home; blank when this computer has none.</summary>
    [ObservableProperty]
    public partial string AwayUrl { get; set; } = string.Empty;

    /// <summary>
    /// The Tailscale command that lets the home address work away from home too (a subnet route to this computer
    /// alone), so one home-screen icon works everywhere; blank unless this computer has a home and a Tailscale address.
    /// </summary>
    [ObservableProperty]
    public partial string OneIconCommand { get; set; } = string.Empty;

    private bool _remoteLoaded;

    // Turning the mobile connection on or off takes effect at once, so the address appears without saving and
    // reopening Settings.
    partial void OnRemoteEnabledChanged(bool value)
    {
        if (!_remoteLoaded) return;
        var port = int.TryParse(RemotePort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? Math.Clamp(p, 1024, 65535) : 8073;
        _host.Settings.Update(s =>
        {
            s.Remote.Enabled = value;
            s.Remote.Port = port;
        });
        RefreshRemote();
    }

    private void OnRemoteChanged() => MainViewModel.Dispatch(RefreshRemote);

    /// <summary>Called when the window closes: this view model stops following the mobile connection and the log copy.</summary>
    public void Detach()
    {
        _host.Remote.Changed -= OnRemoteChanged;
        if (_host.LogCopy is { } copy) copy.StatusChanged -= OnLogCopyChanged;
    }

    /// <summary>The backups folder, which holds the daily copies and the ADIF log copy.</summary>
    public string BackupsFolder => _host.Paths.Backups;

    /// <summary>The ADIF log copy's file names.</summary>
    [ObservableProperty]
    public partial string LogCopyFiles { get; set; } = string.Empty;

    /// <summary>When the ADIF log copy was last written, or why it could not be.</summary>
    [ObservableProperty]
    public partial string LogCopyWritten { get; set; } = string.Empty;

    /// <summary>True when the last write of the ADIF log copy failed.</summary>
    [ObservableProperty]
    public partial bool LogCopyFailed { get; set; }

    /// <summary>The newest daily database copy.</summary>
    [ObservableProperty]
    public partial string DailyBackup { get; set; } = string.Empty;

    /// <summary>Opens the backups folder in the file manager.</summary>
    [RelayCommand]
    private void OpenBackupsFolder()
    {
        if (!FolderOpener.Open(BackupsFolder)) Message = $"Could not open {BackupsFolder}.";
    }

    private void OnLogCopyChanged(LogCopyStatus _) => MainViewModel.Dispatch(RefreshLogCopy);

    private void RefreshLogCopy()
    {
        var st = _host.LogCopy?.Status ?? new LogCopyStatus(BackupsFolder, [], null, null, null);
        LogCopyFiles = LogCopyText.Files(st);
        LogCopyWritten = LogCopyText.Written(st);
        LogCopyFailed = st.Error is not null;
        (string, DateTime)? newest = null;
        try
        {
            newest = _host.Backup?.Newest();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Shown as no copy yet.
        }
        DailyBackup = LogCopyText.Daily(newest, _host.Simulating);
    }

    private void RefreshRemote()
    {
        var r = _host.Remote;
        RemoteRunning = r.Running;
        var urls = r.Urls();
        AwayUrl = r.AwayUrls() is [var away, ..] ? away : string.Empty;
        OneIconCommand = AwayUrl.Length > 0 && urls.Count > 0 && Uri.TryCreate(urls[0], UriKind.Absolute, out var home)
            ? $"tailscale set --advertise-routes={home.Host}/32"
            : string.Empty;
        PhoneUrl = urls.Count > 0 ? urls[0] : $"this computer's address, port {_host.Settings.Current.Remote.Port}";
        PhoneOtherUrls = urls.Count > 1 ? "If that does not open, try " + string.Join(" or ", urls.Skip(1)) : string.Empty;
        RemoteLoopbackWarning = r.LoopbackWarning
            ? "A connection came from this computer itself while sign-in is off. If that was a reverse proxy such as nginx, "
              + "turn on \"Require a username and password\" now: the station may be reachable from the internet with no password. "
              + "If it was a browser on this computer, all is well."
            : string.Empty;
        RemoteStatus = r.Problem is { } p ? p
            : !r.Running ? (RemoteEnabled ? "Starting…" : "Off.")
            : urls.Count == 0 ? "On, but this computer has no address on a home network."
            : $"On. Phones open {string.Join(" or ", urls)}" + (r.Connected > 0 ? $" · {r.Connected} connected now" : string.Empty);
    }

    /// <summary>Check for newer builds.</summary>
    [ObservableProperty]
    public partial bool CheckUpdates { get; set; }

    /// <summary>Send anonymous usage statistics.</summary>
    [ObservableProperty]
    public partial bool UsageEnabled { get; set; }

    /// <summary>Says when this build sends less than the text above says (a build made without the statistics key or the crash report address, such as a local one).</summary>
    public string UsageBuildNote => (_host.Usage.Available, AppHost.CrashDsn is not null) switch
    {
        (true, true) => string.Empty,
        (true, false) => "This build was made without crash reports, so it sends usage statistics only.",
        (false, true) => "This build was made without usage statistics, so it sends crash reports only.",
        _ => "This build was made without statistics or crash reports, so it sends nothing whatever this is set to.",
    };

    /// <summary>Result of "Send a test report".</summary>
    [ObservableProperty]
    public partial string TestReportStatus { get; set; } = string.Empty;

    /// <summary>
    /// Sends a test error report to Sentry, so anyone can check that crash reports get through. Follows the switch above
    /// (as shown, before Save) and the same scrubbing and offline rules as a real report.
    /// </summary>
    [RelayCommand]
    private async Task SendTestReport()
    {
        if (AppHost.CrashDsn is null)
        {
            TestReportStatus = "This build has no crash reports (only releases and development builds made by GitHub do), so there is nothing to test.";
            return;
        }
        if (!UsageEnabled)
        {
            TestReportStatus = "Tick the box above first: with it off, nothing is sent.";
            return;
        }
        if (_host.Simulating)
        {
            TestReportStatus = "Nothing is sent while Costas replays recordings.";
            return;
        }
        TestReportStatus = "Sending…";
        var (outcome, id) = await CrashReporter.SendTestAsync(TimeSpan.FromSeconds(10));
        var shortId = id is { Length: >= 8 } ? id[..8] : id;
        TestReportStatus = outcome switch
        {
            CrashReportTestOutcome.Sent => $"Sent (report {shortId}). In Sentry it shows under Issues as CrashReportTestException.",
            CrashReportTestOutcome.Waiting => $"This computer is offline: the test report ({shortId}) waits and goes out when it is back online.",
            CrashReportTestOutcome.Off => "Not sent: crash reports are switched off.",
            _ => "Crash reports start when Costas opens with the box ticked. Save, restart Costas, then try again.",
        };
    }

    /// <summary>Result of "Check now".</summary>
    [ObservableProperty]
    public partial string UpdateStatus { get; set; } = string.Empty;

    /// <summary>Checks for a newer build now (also offers a build that was skipped before).</summary>
    [RelayCommand]
    private async Task CheckNow()
    {
        UpdateStatus = "Checking…";
        _host.Settings.Update(s =>
        {
            s.Updates.CheckEnabled = true;
            s.Updates.SkippedTag = null;
        });
        CheckUpdates = true;
        var r = await _host.Updates.CheckAsync(CancellationToken.None);
        UpdateStatus = r is not null ? $"Costas {r.Version} is available: see the bar at the top of the main window."
            : _host.Updates.LastError is { } e ? $"Could not check: {e}"
            : $"You have the latest build ({Ft8Client.Core.AppInfo.Version}).";
    }

    /// <summary>Developer section shown (developer mode was on when the window opened).</summary>
    public bool ShowDeveloper { get; }

    /// <summary>Developer mode.</summary>
    [ObservableProperty]
    public partial bool DeveloperMode { get; set; }

    /// <summary>Replay the sample recordings instead of the sound card.</summary>
    [ObservableProperty]
    public partial bool ReplaySamples { get; set; }

    /// <summary>A simulated station answers my calls while replaying.</summary>
    [ObservableProperty]
    public partial bool SimulatedPartner { get; set; }

    /// <summary>Update to development builds (each green build of main) as well as releases.</summary>
    [ObservableProperty]
    public partial bool DevelopmentBuilds { get; set; }

    /// <summary>Says which kind of build is running, beside the development builds switch.</summary>
    public string DevelopmentBuildNote => _host.Updates?.RunningDevelopmentBuild == true
        ? $"This is development build {Ft8Client.Core.AppInfo.Version}. Turning this off (or developer mode) offers the latest release, even though it is older."
        : "Development builds are untested beyond CI and can break on air. Turn this off at any time to go back to the latest release.";

    /// <summary>WAV choices.</summary>
    public IReadOnlyList<string> WavChoices { get; } = ["none", "decoded", "all"];

    /// <summary>WAV saving.</summary>
    [ObservableProperty]
    public partial string SaveWav { get; set; }

    /// <summary>Retention days.</summary>
    [ObservableProperty]
    public partial string Retention { get; set; }

    /// <summary>Themes.</summary>
    public IReadOnlyList<string> Themes { get; } = ["system", "light", "dark"];

    /// <summary>Theme.</summary>
    [ObservableProperty]
    public partial string Theme { get; set; }

    /// <summary>Desktop skin choices, each with light and dark palettes.</summary>
    public IReadOnlyList<AppSkin> Skins => AppSkin.All;

    /// <summary>The skin to apply when settings are saved.</summary>
    [ObservableProperty]
    public partial AppSkin Skin { get; set; }

    /// <summary>Unit choices.</summary>
    public IReadOnlyList<string> UnitChoices { get; } = ["miles", "km"];

    /// <summary>Units.</summary>
    [ObservableProperty]
    public partial string Units { get; set; }

    /// <summary>Operate layout choices.</summary>
    public IReadOnlyList<string> Layouts => OperateViewModel.Layouts;

    /// <summary>Operate layout: automatic, roomy or compact.</summary>
    [ObservableProperty]
    public partial string Layout { get; set; } = "automatic";

    /// <summary>jt9 path.</summary>
    [ObservableProperty]
    public partial string Jt9Path { get; set; }

    /// <summary>Hamlib folder.</summary>
    [ObservableProperty]
    public partial string HamlibDir { get; set; }

    /// <summary>Moves a tier up.</summary>
    [RelayCommand]
    private void TierUp(TierItem t)
    {
        var i = TierOrder.IndexOf(t);
        if (i > 0) TierOrder.Move(i, i - 1);
    }

    /// <summary>Moves a tier down.</summary>
    [RelayCommand]
    private void TierDown(TierItem t)
    {
        var i = TierOrder.IndexOf(t);
        if (i >= 0 && i < TierOrder.Count - 1) TierOrder.Move(i, i + 1);
    }

    /// <summary>Saves and closes.</summary>
    [RelayCommand]
    private void Save()
    {
        if (Commit()) Close?.Invoke();
    }

    /// <summary>Saves and stays open, so more changes can follow.</summary>
    [RelayCommand]
    private void Apply()
    {
        AppliedNote = string.Empty;
        if (!Commit()) return;
        Message = string.Empty;
        AppliedNote = $"Applied at {DateTime.Now:HH:mm:ss}.";
    }

    // Stores everything shown. False when something was refused (the reason is in Message, on its page) or a secret could
    // not be kept; the window then stays open either way.
    private bool Commit()
    {
        static int Int(string s, int min, int max, int fallback) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, min, max) : fallback;
        static double Dbl(string s, double min, double max, double fallback) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, min, max) : fallback;
        // A message that cannot be sent exactly as shown is not saved: the operator is shown why on its page.
        if (MessageProblem is { Length: > 0 } problem)
        {
            Message = problem;
            SelectedPage = ContactsPage;
            return false;
        }
        var oldHamlibDir = _host.Settings.Current.Paths.HamlibDir;

        StoreFields();
        if (Profiles.FirstOrDefault(d => !Core.Messages.Callsign.IsValid(d.Callsign.Trim().ToUpperInvariant())) is { } bad)
        {
            Message = Profiles.Count > 1 ? $"Enter a valid callsign for the {bad.Name} profile (Station page)." : "Enter a valid callsign (Station page).";
            SelectedProfile = bad;
            SelectedPage = 0;
            return false;
        }
        if (MacroCheck(Macros) is { } check && (check.Blocking || check.Text != _macroWarning))
        {
            Message = check.Text;
            _macroWarning = check.Blocking ? null : check.Text;
            SelectedPage = KeyboardPage;
            return false;
        }
        if ((RemoteUsersCheck(RemoteSignIn, RemoteUsers) ?? ProxyAddressCheck(RemoteProxyAddress)) is { } remoteProblem)
        {
            Message = remoteProblem;
            SelectedPage = MobilePage;
            return false;
        }
        // A different profile, or a new callsign or grid for the one in use, changes what goes out: only when idle (D25,
        // owner decisions 2026-11-17). Checked before anything is stored.
        var chosen = SelectedProfile ?? Profiles[0];
        var current = _host.Settings.Current.Profile;
        // A phone may have switched profile since this window opened (D25): leaving the choice as it was keeps the
        // profile now in use rather than switching back to the one shown at the start.
        var inUse = chosen.Id == _loadedProfile ? Profiles.FirstOrDefault(d => d.Id == current.Id) ?? chosen : chosen;
        var activeId = inUse.Id;
        var what = activeId != current.Id ? "the station profile"
            : !string.Equals(inUse.Callsign.Trim().ToUpperInvariant(), current.Callsign, StringComparison.Ordinal) ? "your callsign"
            : !string.Equals(inUse.Grid.Trim().ToUpperInvariant(), current.Grid.Trim().ToUpperInvariant(), StringComparison.Ordinal) ? "your grid square"
            : null;
        if (what is not null && ProfileSwitch.Refusal(_host.Session.Snapshot, what) is { } busy)
        {
            Message = busy;
            SelectedPage = 0;
            return false;
        }
        // Passwords first, so sign-in never comes on before the password that lets a phone in is stored.
        var remoteUsers = _host.Remote.Control.Users;
        foreach (var r in RemoteUsers.Where(r => r.NewPassword.Length > 0))
        {
            try
            {
                remoteUsers?.SetPassword(r.Id, r.NewPassword);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Message = SecretStoreProblem.Describe(ex.Message);
                SelectedPage = MobilePage;
                return false;
            }
        }
        var removedRemote = _host.Settings.Current.Remote.Users.Select(u => u.Id).Where(id => RemoteUsers.All(r => r.Id != id)).ToList();
        var removed = _host.Settings.Current.Profiles.Select(p => p.Id).Where(id => Profiles.All(d => d.Id != id)).ToList();
        _host.Settings.Update(s =>
        {
            ApplyProfiles(s, Profiles, activeId);
            s.Ranking.TierOrder = SettingsMapper.TierNames(TierOrder.Select(t => t.Tag));
            s.Ranking.PreferHearsMe = PreferHearsMe;
            s.Ranking.OnlyCallingCq = OnlyCallingCq;
            s.Ranking.HideWorked = HideWorked;
            s.Ranking.SkipLongShots = SkipLongShots;
            s.Ranking.ConfirmedOnly = ConfirmedOnly;
            s.Ranking.ConfirmedBy = ConfirmedByLotw ? "lotw" : "any";
            s.Lotw.UserList = LotwUserList;
            s.Lotw.TqslPath = string.IsNullOrWhiteSpace(TqslPath) ? null : TqslPath.Trim();
            s.Hunt.Targets = HuntRows.Select(r => HuntService.ToEntry(r.Target)).ToList();
            s.Hunt.WatchAllBands = HuntAllBands;
            s.Hunt.Sound = HuntSound;
            s.Notifications.Enabled = NotifyEnabled;
            s.Notifications.Hunt = NotifyHunt;
            s.Notifications.Callers = NotifyCallers;
            s.Notifications.OnlyInBackground = NotifyOnlyInBackground;
            var near = Int(HuntNearby, 50, 5000, 0);
            if (near > 0) s.Hunt.NearbyKm = HuntNearbyUnit == "km" ? near : (int)Math.Round(near * Core.Geo.GreatCircle.KmPerMile);
            s.Ranking.KeepCqCallersMinutes = KeepMinutesFor(KeepCqCallers);
            // Only a level typed here replaces the band's level, so one automatic level set meanwhile is kept.
            if (TxGain.Trim() != _loadedTxGain)
            {
                var gain = Dbl(TxGain, -40, 0, s.Profile.Audio.TxGainDb);
                s.Profile.Audio.TxGainDb = gain;
                s.Profile.Audio.TxGainByBand[TxLevelController.LevelKey(s.Operating.Band, s.Operating.Mode)] = gain;
            }
            s.Profile.Audio.AutoTxLevel = AutoTxLevel;
            s.Profile.Audio.StopOnHighSwr = StopOnHighSwr;
            s.Operating.WatchdogMinutes = Int(Watchdog, 1, 60, 6);
            s.Operating.RetryLimit = Int(RetryLimit, 1, 20, 4);
            s.Operating.Signoff = Signoff;
            s.Operating.CqModifier = CqModifier.Trim().ToUpperInvariant();
            s.Operating.GridInReply = GridInReply;
            s.Operating.AnswerSignoff = AnswerSignoff == "RR73" ? "RR73" : "73";
            s.Operating.FreeTextAfter = FreeText();
            s.Operating.AutoLog = AutoLog;
            s.Operating.ChimeOnCaller = ChimeOnCaller;
            s.Operating.AnswerAllInTurn = AnswerAllInTurn;
            s.Operating.KeepCallingWhenBusy = KeepCallingWhenBusy;
            s.Operating.RttyBaud = Dbl(RttySpeed, 45, 75, RttyFormat.Standard.Baud);
            s.Operating.RttyShiftHz = Int(RttyShift, 170, 850, RttyFormat.Standard.ShiftHz);
            s.Operating.RttyReverse = RttyReverse;
            s.Operating.RttyUnshiftOnSpace = RttyUnshiftOnSpace;
            s.Operating.RttyPowerPercent = Int(RttyPowerPercent, 10, 100, 50);
            s.Operating.Macros = [.. Macros.Where(m => !string.IsNullOrWhiteSpace(m.Text))
                .Select(m => new MacroOptions { Key = m.Key, Label = m.Label.Trim(), Text = m.Text.Trim() })];
            s.Operating.CqParity = CqParity;
            s.Operating.ClockBlockSeconds = Dbl(ClockBlock, 0.5, 10, 2);
            s.Operating.CommandStartLimitMs = (int)Math.Round(Dbl(CommandStartLimit, 0, SettingsMapper.MaxCommandStartLimitMs / 1000.0, 3) * 1000);
            s.Operating.DoubleClickSwitches = DoubleClickSwitches;
            s.Operating.CorrectClock = CorrectClock;
            s.Qrz.LogbookEnabled = LogbookEnabled;
            s.Qrz.LookupEnabled = !string.IsNullOrWhiteSpace(QrzUser) && !string.IsNullOrEmpty(QrzPassword);
            s.Qrz.UploadOnComplete = UploadOnComplete;
            s.Qrz.SyncMinutes = Int(SyncMinutes, 5, 1440, 15);
            s.PskReporter.FeedEnabled = FeedEnabled;
            s.Pota.SpotsEnabled = PotaSpots;
            s.Dxpeditions.Enabled = DxpeditionList;
            s.Dxpeditions.VerifySuperFox = VerifySuperFox;
            s.Ranking.PreferNewParks = PreferNewParks;
            s.PskReporter.CompareNeighbours = CompareNeighbours;
            s.PskReporter.BandActivity = BandActivity;
            s.PskReporter.UploadSpots = UploadSpots;
            s.ReverseBeacon.Enabled = RbnEnabled;
            s.DxCluster.Enabled = ClusterEnabled;
            s.DxCluster.Host = ClusterHost.Trim();
            s.DxCluster.Port = Int(ClusterPort, 1, 65535, 7300);
            s.Udp.Enabled = UdpEnabled;
            s.Udp.Address = UdpAddress.Trim();
            s.Udp.Port = Int(UdpPort, 1, 65535, 2237);
            s.Remote.Enabled = RemoteEnabled;
            s.Remote.Port = Int(RemotePort, 1024, 65535, 8073);
            s.Remote.RequireSignIn = RemoteSignIn;
            s.Remote.TrustedProxy = string.IsNullOrWhiteSpace(RemoteProxyAddress) ? null : RemoteProxyAddress.Trim();
            // A link to a profile deleted here goes with it: that user then operates as whichever profile is in use.
            s.Remote.Users = [.. RemoteUsers.Select(r => new RemoteUser
            {
                Id = r.Id, Username = r.Username.Trim(), Enabled = r.Enabled,
                ProfileId = r.ProfileId is { } p && Profiles.Any(d => d.Id == p) ? p : null,
            })];
            s.Files.SaveWav = SaveWav;
            s.Files.RetentionDays = Int(Retention, 1, 3650, 30);
            s.Appearance.Theme = Theme;
            s.Appearance.Skin = Skin.Id;
            s.Appearance.Units = Units;
            s.Appearance.Layout = Layout;
            s.Paths.Jt9 = string.IsNullOrWhiteSpace(Jt9Path) ? null : Jt9Path.Trim();
            s.Paths.HamlibDir = string.IsNullOrWhiteSpace(HamlibDir) ? null : HamlibDir.Trim();
            s.Updates.CheckEnabled = CheckUpdates;
            s.Usage.Enabled = UsageEnabled;
            if (ShowDeveloper)
            {
                s.Developer.Enabled = DeveloperMode;
                s.Developer.ReplaySamples = ReplaySamples;
                s.Developer.SimulatedPartner = SimulatedPartner;
                s.Developer.DevelopmentBuilds = DevelopmentBuilds;
            }
        });
        foreach (var d in Profiles)
        {
            Set(SecretNames.QrzLogbookKey(d.Id), d.LogbookKey.Trim());
            Set(SecretNames.EqslUsername(d.Id), d.EqslUser.Trim());
            Set(SecretNames.EqslPassword(d.Id), d.EqslPassword);
            Set(SecretNames.CloudlogKey(d.Id), d.CloudlogKey.Trim());
            Set(SecretNames.ClubLogPassword(d.Id), d.ClubLogPassword.Trim());
            Set(SecretNames.LotwPassword(d.Id), d.LotwPassword);
        }
        foreach (var id in removedRemote)
        {
            try
            {
                remoteUsers?.Forget(id);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _secretProblem ??= ex.Message;
            }
        }
        foreach (var id in removed)
        {
            _host.Secrets.Delete(SecretNames.QrzLogbookKey(id));
            _host.Secrets.Delete(SecretNames.EqslUsername(id));
            _host.Secrets.Delete(SecretNames.EqslPassword(id));
            _host.Secrets.Delete(SecretNames.CloudlogKey(id));
            _host.Secrets.Delete(SecretNames.ClubLogPassword(id));
            _host.Secrets.Delete(SecretNames.LotwPassword(id));
        }
        Set(SecretNames.QrzUsername, QrzUser.Trim());
        Set(SecretNames.QrzPassword, QrzPassword);
        _host.TxLevel.Apply();
        _host.RebuildLogIndex();
        // Another profile may use another radio or audio device. A new Hamlib folder is where rigctld is found: Setup
        // tells the operator to set it here when rigctld is missing, so it must take effect without a restart.
        if (activeId != _loadedProfile || !string.Equals(oldHamlibDir, _host.Settings.Current.Paths.HamlibDir, StringComparison.Ordinal))
            _ = _host.ReopenDevicesAsync();
        // What is now saved becomes what the next Apply compares with: the same profile does not reopen the radio again,
        // a level left as it was does not overwrite one automatic level set meanwhile, and a remote password is not set
        // a second time (which would sign that user out everywhere again).
        _loadedProfile = activeId;
        _loadedTxGain = TxGain.Trim();
        foreach (var r in RemoteUsers) r.PasswordSaved();
        // Everything else is saved; the window stays open on the reason so the operator knows the key is not kept.
        if (_secretProblem is { } why)
        {
            _secretProblem = null;
            Message = SecretStoreProblem.Describe(why);
            return false;
        }
        return true;
    }

    /// <summary>Closes without saving.</summary>
    [RelayCommand]
    private void Cancel() => Close?.Invoke();

    // A keyring that refuses (none running on this Linux desktop, or its unlock prompt cancelled) used to throw out of
    // Save: the window stayed open with a generic "Something went wrong" on the main window's status line.
    private string? _secretProblem;

    private void Set(string name, string value)
    {
        try
        {
            if (string.IsNullOrEmpty(value)) _host.Secrets.Delete(name);
            else _host.Secrets.Set(name, value);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _secretProblem ??= ex.Message;
        }
    }
}
