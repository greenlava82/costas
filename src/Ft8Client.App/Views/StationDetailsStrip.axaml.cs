// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia.Controls;

namespace Ft8Client.App.Views;

/// <summary>A station's details across the row, opened under the selected row in Next in line.</summary>
public partial class StationDetailsStrip : UserControl
{
    /// <summary>Creates the control.</summary>
    public StationDetailsStrip() => InitializeComponent();
}
