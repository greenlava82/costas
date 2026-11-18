// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Core.Services;

namespace Ft8Client.App.Services;

/// <summary>
/// One indicator fed by several parts of a service (QRZ: logbook sync, uploads, lookups). While any part is Down or
/// Degraded the worst of them is shown (the latest among equals), so one part working again does not hide another that
/// is still failing; otherwise the latest status set is shown. Thread-safe.
/// </summary>
public sealed class CombinedServiceStatus
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (ServiceStatus Status, long Order)> _parts = new(StringComparer.Ordinal);
    private long _order;

    /// <summary>The status to show, or null when no part has reported.</summary>
    public ServiceStatus? Current
    {
        get
        {
            lock (_gate) return Combine();
        }
    }

    /// <summary>The last status <paramref name="part"/> reported, or null.</summary>
    public ServiceStatus? Get(string part)
    {
        lock (_gate) return _parts.TryGetValue(part, out var p) ? p.Status : null;
    }

    /// <summary>Records a part's status and returns the status to show.</summary>
    public ServiceStatus Set(string part, ServiceStatus status)
    {
        lock (_gate)
        {
            _parts[part] = (status, ++_order);
            return Combine()!;
        }
    }

    /// <summary>Forgets a part. Returns the status to show now, or null when nothing changed or no part is left.</summary>
    public ServiceStatus? Clear(string part)
    {
        lock (_gate) return _parts.Remove(part) ? Combine() : null;
    }

    private ServiceStatus? Combine()
    {
        if (_parts.Count == 0) return null;
        var failing = _parts.Values.Where(p => p.Status.Health is ServiceHealth.Down or ServiceHealth.Degraded).ToList();
        var pick = failing.Count > 0
            ? failing.OrderByDescending(p => p.Status.Health).ThenByDescending(p => p.Order).First()
            : _parts.Values.MaxBy(p => p.Order);
        return pick.Status;
    }
}
