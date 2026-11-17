// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using Avalonia.Controls;
using Ft8Client.Core.Spectrum;

namespace Ft8Client.App.Views;

/// <summary>The waterfall colours: the palette, the darkest level and the range to the brightest.</summary>
public partial class WaterfallLevelsPanel : UserControl
{
    private bool _setting;

    /// <summary>Creates the panel showing the default palette and scale.</summary>
    public WaterfallLevelsPanel()
    {
        InitializeComponent();
        PaletteBox.ItemsSource = WaterfallPalette.Choices.Select(c => c.Name).ToList();
        FloorSlider.Minimum = WaterfallLevels.MinFloorDb;
        FloorSlider.Maximum = WaterfallLevels.MaxFloorDb;
        RangeSlider.Minimum = WaterfallLevels.MinRangeDb;
        RangeSlider.Maximum = WaterfallLevels.MaxRangeDb;
        Show(WaterfallLevels.Default, WaterfallPalette.Wsjtx);
        PaletteBox.SelectionChanged += (_, _) => Changed();
        FloorSlider.ValueChanged += (_, _) => Changed();
        RangeSlider.ValueChanged += (_, _) => Changed();
        ResetButton.Click += (_, _) =>
        {
            Show(WaterfallLevels.Default, Palette);
            Changed();
        };
    }

    /// <summary>Raised as the operator picks a palette, moves a slider or resets.</summary>
    public event Action<WaterfallLevels, string>? LevelsChanged;

    /// <summary>The scale the sliders show.</summary>
    public WaterfallLevels Levels => new WaterfallLevels((int)Math.Round(FloorSlider.Value), (int)Math.Round(RangeSlider.Value)).Clamped();

    /// <summary>The palette picked.</summary>
    public string Palette =>
        PaletteBox.SelectedIndex is var i && i >= 0 && i < WaterfallPalette.Choices.Count ? WaterfallPalette.Choices[i].Id : WaterfallPalette.Wsjtx;

    /// <summary>Shows a scale and palette without raising <see cref="LevelsChanged"/>.</summary>
    public void Show(WaterfallLevels levels, string? palette)
    {
        levels = levels.Clamped();
        palette = WaterfallPalette.Known(palette);
        _setting = true;
        PaletteBox.SelectedIndex = WaterfallPalette.Choices.Select(c => c.Id).ToList().IndexOf(palette);
        FloorSlider.Value = levels.FloorDb;
        RangeSlider.Value = levels.RangeDb;
        _setting = false;
        ShowValues();
    }

    private void Changed()
    {
        ShowValues();
        if (!_setting) LevelsChanged?.Invoke(Levels, Palette);
    }

    private void ShowValues()
    {
        var l = Levels;
        FloorValue.Text = $"{Signed(l.FloorDb)} dB";
        RangeValue.Text = $"{l.RangeDb.ToString(CultureInfo.InvariantCulture)} dB (brightest {Signed(l.FloorDb + l.RangeDb)} dB)";
    }

    private static string Signed(int db) => db > 0 ? "+" + db.ToString(CultureInfo.InvariantCulture) : db.ToString(CultureInfo.InvariantCulture).Replace("-", "−", StringComparison.Ordinal);
}
