// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using CommunityToolkit.Mvvm.ComponentModel;
using Ft8Client.Core.Bands;

namespace Ft8Client.App.ViewModels;

/// <summary>A station profile as edited in Settings, applied on Save.</summary>
public sealed partial class ProfileDraft : ObservableObject
{
    /// <summary>Stable id.</summary>
    public required string Id { get; init; }

    /// <summary>For a profile added in this Settings window, the id of the profile it copies (radio and audio); else null.</summary>
    public string? CopyOf { get; init; }

    /// <summary>
    /// Added with New operator: takes only the station (radio, audio and the like) from <see cref="CopyOf"/>, not what
    /// belongs to its operator, such as their contest exchanges.
    /// </summary>
    public bool ForAnotherOperator { get; init; }

    /// <summary>Display name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Callsign as typed.</summary>
    public string Callsign { get; set; } = string.Empty;

    /// <summary>Grid as typed.</summary>
    public string Grid { get; set; } = string.Empty;

    /// <summary>
    /// The US licence class the transmit guard keeps to, or not US. A new draft starts with the check off: the licence
    /// lockout is turned on only by choosing a class.
    /// </summary>
    public UsLicenceClass Licence { get; set; } = UsLicenceClass.NotUs;

    /// <summary>Power in watts as typed.</summary>
    public string Power { get; set; } = string.Empty;

    /// <summary>The radio's top power in watts as typed; empty to use what Hamlib reports.</summary>
    public string MaxPower { get; set; } = string.Empty;

    /// <summary>The FlexRadio's address for its amplifier and tuner, as typed; empty for none.</summary>
    public string FlexAddress { get; set; } = string.Empty;

    /// <summary>The operator's first name as typed, for <c>{MYNAME}</c>.</summary>
    public string OperatorName { get; set; } = string.Empty;

    /// <summary>Where the station is, in words, for <c>{MYQTH}</c>.</summary>
    public string Qth { get; set; } = string.Empty;

    /// <summary>The radio, for <c>{MYRIG}</c>.</summary>
    public string RigDescription { get; set; } = string.Empty;

    /// <summary>The antenna, for <c>{MYANT}</c>.</summary>
    public string Antenna { get; set; } = string.Empty;

    /// <summary>QRZ logbook API key as typed (kept in the credential store per profile).</summary>
    public string LogbookKey { get; set; } = string.Empty;

    /// <summary>eQSL username as typed (credential store, per profile).</summary>
    public string EqslUser { get; set; } = string.Empty;

    /// <summary>eQSL password as typed (credential store, per profile).</summary>
    public string EqslPassword { get; set; } = string.Empty;

    /// <summary>Read the eQSL inbox for confirmations.</summary>
    public bool EqslSync { get; set; }

    /// <summary>Upload each contact to eQSL when it is logged.</summary>
    public bool EqslUpload { get; set; }

    /// <summary>eQSL QTH nickname.</summary>
    public string EqslQth { get; set; } = string.Empty;

    /// <summary>Wavelog/Cloudlog site address as typed.</summary>
    public string CloudlogUrl { get; set; } = string.Empty;

    /// <summary>Wavelog/Cloudlog API key as typed (credential store, per profile).</summary>
    public string CloudlogKey { get; set; } = string.Empty;

    /// <summary>Wavelog/Cloudlog station location id.</summary>
    public string CloudlogStationId { get; set; } = string.Empty;

    /// <summary>Upload each contact to Wavelog/Cloudlog when it is logged.</summary>
    public bool CloudlogUpload { get; set; }

    /// <summary>Club Log account email as typed.</summary>
    public string ClubLogEmail { get; set; } = string.Empty;

    /// <summary>Club Log Application Password as typed (credential store, per profile).</summary>
    public string ClubLogPassword { get; set; } = string.Empty;

    /// <summary>Upload each contact to Club Log when it is logged.</summary>
    public bool ClubLogUpload { get; set; }

    /// <summary>Download this profile's LoTW confirmations.</summary>
    public bool LotwEnabled { get; set; }

    /// <summary>LoTW login as typed.</summary>
    public string LotwUser { get; set; } = string.Empty;

    /// <summary>LoTW website password as typed (credential store, per profile).</summary>
    public string LotwPassword { get; set; } = string.Empty;

    /// <summary>TQSL Station Location as typed.</summary>
    public string LotwStationLocation { get; set; } = string.Empty;

    /// <summary>Sign and upload each contact through TQSL when it is logged.</summary>
    public bool LotwUpload { get; set; }

    /// <summary>The Club Log Application Password as stored when the window opened, to notice a new one on save.</summary>
    public string ClubLogPasswordSaved { get; init; } = string.Empty;

    /// <summary>The stop in force when the window opened (Club Log refused the login), or null.</summary>
    public Ft8Client.Data.Settings.ClubLogStop? ClubLogStop { get; init; }

    /// <summary>True once the operator chose to resume Club Log uploads; saving clears the stop.</summary>
    public bool ClubLogResume { get; set; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
