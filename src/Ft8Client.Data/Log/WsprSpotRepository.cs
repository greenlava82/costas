// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Dapper;
using Ft8Client.Data.Database;

namespace Ft8Client.Data.Log;

/// <summary>
/// Stores the WSPR signals heard (docs/10-wspr.md section 4) and the state of their upload to WSPRnet. A signal heard
/// twice on the same frequency in the same slot for the same profile is stored once.
/// </summary>
public sealed class WsprSpotRepository(SqliteDatabase db)
{
    /// <summary>Adds a spot. Returns false when it was already stored.</summary>
    public bool Add(WsprSpotRow s)
    {
        using var c = db.Open();
        var id = c.ExecuteScalar<long?>("""
            INSERT OR IGNORE INTO wspr_spot (profile_callsign, slot_utc, dial_hz, freq_hz, snr, dt, drift, callsign, grid, dbm, is_hashed, distance_km, upload_state)
            VALUES (@ProfileCallsign, @SlotUtc, @DialHz, @FreqHz, @Snr, @Dt, @Drift, @Callsign, @Grid, @Dbm, @IsHashed, @DistanceKm, @UploadState);
            SELECT CASE WHEN changes() > 0 THEN last_insert_rowid() END;
            """, new
        {
            s.ProfileCallsign, s.SlotUtc, s.DialHz, s.FreqHz, s.Snr, s.Dt, s.Drift, s.Callsign, s.Grid, s.Dbm,
            IsHashed = s.IsHashed ? 1 : 0, s.DistanceKm, UploadState = (int)s.UploadState,
        });
        if (id is null) return false;
        s.Id = id.Value;
        return true;
    }

    /// <summary>The newest spots of a profile, newest slot first (then strongest first), at most <paramref name="count"/>.</summary>
    public IReadOnlyList<WsprSpotRow> Recent(string profileCallsign, int count)
    {
        using var c = db.Open();
        return c.Query<WsprSpotRow>(
            "SELECT * FROM wspr_spot WHERE profile_callsign=@profileCallsign ORDER BY slot_utc DESC, snr DESC, id DESC LIMIT @count",
            new { profileCallsign, count }).ToList();
    }

    /// <summary>Spots of a profile waiting to be uploaded, oldest first, at most <paramref name="limit"/>.</summary>
    public IReadOnlyList<WsprSpotRow> Pending(string profileCallsign, int limit)
    {
        using var c = db.Open();
        return c.Query<WsprSpotRow>(
            "SELECT * FROM wspr_spot WHERE profile_callsign=@profileCallsign AND upload_state=1 ORDER BY slot_utc, id LIMIT @limit",
            new { profileCallsign, limit }).ToList();
    }

    /// <summary>Number of spots of a profile waiting to be uploaded.</summary>
    public int PendingCount(string profileCallsign)
    {
        using var c = db.Open();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM wspr_spot WHERE profile_callsign=@profileCallsign AND upload_state=1", new { profileCallsign });
    }

    /// <summary>Sets the upload state of the given spots.</summary>
    public void SetUploadState(IEnumerable<long> ids, WsprUploadState state)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        foreach (var id in ids) c.Execute("UPDATE wspr_spot SET upload_state=@state WHERE id=@id", new { id, state = (int)state }, tx);
        tx.Commit();
    }

    /// <summary>Deletes spots older than <paramref name="keep"/> that are not waiting to be uploaded.</summary>
    public int Prune(DateTime nowUtc, TimeSpan keep)
    {
        using var c = db.Open();
        return c.Execute("DELETE FROM wspr_spot WHERE slot_utc < @cut AND upload_state <> 1", new { cut = nowUtc - keep });
    }
}
