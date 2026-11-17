// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using FluentAssertions;
using Ft8Client.App.Views;
using Ft8Client.Core.Spectrum;

namespace Ft8Client.App.Tests;

public class WaterfallLevelsPanelTests
{
    [Fact]
    public async Task Panel_OperatorChanges_RaisedWithPaletteAndScale()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(WaterfallLevelsPanelTests).Assembly);
        await session.Dispatch(() =>
        {
            var panel = new WaterfallLevelsPanel();
            var seen = new List<(WaterfallLevels Levels, string Palette)>();
            panel.LevelsChanged += (l, p) => seen.Add((l, p));

            panel.Show(new WaterfallLevels(0, 20), WaterfallPalette.Costas);
            seen.Should().BeEmpty("showing saved values is not an operator change");
            panel.Palette.Should().Be(WaterfallPalette.Costas);

            panel.FindControl<ComboBox>("PaletteBox")!.SelectedIndex = 0;
            seen.Last().Should().Be((new WaterfallLevels(0, 20), WaterfallPalette.Wsjtx));

            panel.FindControl<Slider>("RangeSlider")!.Value = 40;
            seen.Last().Should().Be((new WaterfallLevels(0, 40), WaterfallPalette.Wsjtx));
            panel.FindControl<TextBlock>("RangeValue")!.Text.Should().Be("40 dB (brightest +40 dB)");

            panel.FindControl<Button>("ResetButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            seen.Last().Should().Be((WaterfallLevels.Default, WaterfallPalette.Wsjtx), "reset keeps the colours picked");
        }, CancellationToken.None);
    }
}
