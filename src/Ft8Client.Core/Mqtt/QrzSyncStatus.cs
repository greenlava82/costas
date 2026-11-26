// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


namespace Ft8Client.Core.Mqtt;

/// <summary>The qrz_sync sensor's state and attributes.</summary>
/// <param name="State">ok, pending, error or disabled.</param>
/// <param name="LastSyncUtc">When the logbook last synced, or null.</param>
/// <param name="PendingCount">Contacts waiting to upload.</param>
/// <param name="LastError">What went wrong, or null.</param>
public sealed record QrzSyncStatus(QrzSyncState State, DateTime? LastSyncUtc, int PendingCount, string? LastError);
