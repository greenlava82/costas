// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Notifications;

/// <summary>
/// A notification to show. There is at most one per <see cref="Kind"/>: a later one of the same kind replaces it in
/// the notification centre.
/// </summary>
/// <param name="Kind">What it is about; also which earlier notification it replaces.</param>
/// <param name="Title">Title.</param>
/// <param name="Body">Text.</param>
/// <param name="Call">The station clicking it selects.</param>
/// <param name="Popup">
/// True to pop up on screen. False for an update merged into the one already showing: it replaces that one quietly,
/// without popping up again.
/// </param>
public sealed record DesktopNotification(AttentionKind Kind, string Title, string Body, string Call, bool Popup);
