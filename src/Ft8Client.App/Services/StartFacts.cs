// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
namespace Ft8Client.App.Services;

/// <summary>
/// Facts about this install sent with "app_started" besides the settings: how it was installed, its update channel, and
/// coarse adoption figures from <see cref="UsageHistory"/>.
/// </summary>
/// <param name="Install">velopack, classic, deb, appimage or folder.</param>
/// <param name="Channel">release or dev.</param>
/// <param name="AgeDays">Days since the first run with statistics on.</param>
/// <param name="ActiveDays30">Days with a run in the last 30.</param>
/// <param name="FeaturesTried">How many tracked features have been used at least once.</param>
public sealed record StartFacts(string Install, string Channel, int AgeDays, int ActiveDays30, int FeaturesTried);
