// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Core.Notifications;

/// <summary>Something that needs the operator, worded for a desktop notification.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Call">The station it is about, upper case. Clicking the notification selects it.</param>
/// <param name="Title">The notification's title: "VP8PJ on 20 m FT8".</param>
/// <param name="Body">The notification's text: "New country Falkland Islands, heard here at -12 dB."</param>
/// <param name="Short">How it reads in a merged notification: "VP8PJ on 20 m FT8" or "W1ABC".</param>
public sealed record AttentionEvent(AttentionKind Kind, string Call, string Title, string Body, string Short);
