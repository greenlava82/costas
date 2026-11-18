// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.App.Remote;

/// <summary>A notice for the phone that someone else signed in or took control (<see cref="RemoteNotices"/>).</summary>
/// <param name="Id">Counts up per notice, so the phone remembers which ones were dismissed.</param>
/// <param name="Text">"Ash signed in on another device."</param>
/// <param name="Time">"21:14 UTC".</param>
public sealed record RemoteNotice(long Id, string Text, string Time);
