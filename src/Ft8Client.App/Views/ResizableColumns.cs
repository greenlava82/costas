// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

namespace Ft8Client.App.Views;

/// <summary>
/// Lets the Operate tables' columns be resized. Every header and row grid of a table names the same layout
/// (<c>v:ResizableColumns.Layout="Calls"</c>) so they keep lining up; a header grid also gets
/// <c>v:ResizableColumns.IsHeader="True"</c>, which puts a drag grip on each column edge.
/// </summary>
public static class ResizableColumns
{
    /// <summary>The name of the shared layout this grid follows.</summary>
    public static readonly AttachedProperty<string?> LayoutProperty =
        AvaloniaProperty.RegisterAttached<Grid, string?>("Layout", typeof(ResizableColumns));

    /// <summary>True on a table's header grid, which carries the drag grips.</summary>
    public static readonly AttachedProperty<bool> IsHeaderProperty =
        AvaloniaProperty.RegisterAttached<Grid, bool>("IsHeader", typeof(ResizableColumns));

    private static readonly AttachedProperty<Action?> UnhookProperty =
        AvaloniaProperty.RegisterAttached<Grid, Action?>("Unhook", typeof(ResizableColumns));

    static ResizableColumns()
    {
        LayoutProperty.Changed.AddClassHandler<Grid>((grid, _) =>
        {
            grid.AttachedToVisualTree -= OnAttached;
            grid.DetachedFromVisualTree -= OnDetached;
            grid.AttachedToVisualTree += OnAttached;
            grid.DetachedFromVisualTree += OnDetached;
            if (grid.IsAttachedToVisualTree()) Hook(grid);
        });
    }

    /// <summary>Gets the layout name.</summary>
    public static string? GetLayout(Grid grid) => grid.GetValue(LayoutProperty);

    /// <summary>Sets the layout name.</summary>
    public static void SetLayout(Grid grid, string? value) => grid.SetValue(LayoutProperty, value);

    /// <summary>Gets whether the grid is a header.</summary>
    public static bool GetIsHeader(Grid grid) => grid.GetValue(IsHeaderProperty);

    /// <summary>Sets whether the grid is a header.</summary>
    public static void SetIsHeader(Grid grid, bool value) => grid.SetValue(IsHeaderProperty, value);

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => Hook((Grid)sender!);

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Unhook((Grid)sender!);

    private static void Hook(Grid grid)
    {
        Unhook(grid);
        if (GetLayout(grid) is not { } name || grid.FindAncestorOfType<OperateView>() is not { } view) return;
        var layout = view.ColumnsFor(name, grid);
        // On a window too narrow for every column the table gives way (ColumnLayout.Fit) so the Call buttons in
        // the last column stay on screen; a hidden column's cells and grip hide with it.
        void Apply()
        {
            var widths = layout.Fit(grid.Bounds.Width);
            var count = Math.Min(layout.Count, grid.ColumnDefinitions.Count);
            for (var i = 0; i < count; i++)
                if (layout.IsFixed(i) && grid.ColumnDefinitions[i].Width.Value != widths[i])
                    grid.ColumnDefinitions[i].Width = new GridLength(widths[i]);
            foreach (var child in grid.Children)
            {
                var column = child is Thumb { Tag: int target } ? target : Grid.GetColumn(child);
                var shown = column >= count || !layout.IsFixed(column) || widths[column] > 0;
                if (child.IsVisible != shown) child.IsVisible = shown;
            }
        }
        void OnSize(object? sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged) Apply();
        }
        // Auto-size: after each layout pass the grid says how wide its text is in the measured columns (Where, Why,
        // Doing now), so the layout can give cut-off columns the room Doing now does not use. A hidden grid (Repeats
        // turned off, a folded table) says nothing.
        void OnLayoutUpdated(object? sender, EventArgs e)
        {
            var columns = layout.MeasuredColumns;
            if (columns.Count == 0) return;
            if (!grid.IsEffectivelyVisible)
            {
                layout.ReportNeeds(grid, null);
                return;
            }
            layout.ReportNeeds(grid, MeasureColumns(grid, columns, layout.Count));
        }
        var grips = GetIsHeader(grid) ? AddGrips(grid, layout, () => view.SaveColumns(name)) : [];
        Apply();
        layout.Changed += Apply;
        grid.SizeChanged += OnSize;
        grid.LayoutUpdated += OnLayoutUpdated;
        grid.SetValue(UnhookProperty, () =>
        {
            layout.Changed -= Apply;
            grid.SizeChanged -= OnSize;
            grid.LayoutUpdated -= OnLayoutUpdated;
            layout.ReportNeeds(grid, null);
            foreach (var grip in grips) grid.Children.Remove(grip);
        });
    }

    private static void Unhook(Grid grid)
    {
        grid.GetValue(UnhookProperty)?.Invoke();
        grid.SetValue(UnhookProperty, null);
    }

    // One grip on each edge between two columns. Dragging it moves that edge only: the column on one side grows by what
    // the other gives up, so the rest of the table stays put (owner request 2026-11-14; before, every drag took its room
    // from Doing now, and with Doing now already squeezed to nothing on a narrow window no column could grow). Beside
    // the star column (Doing now) the star column gives or takes the room. The last column (the Call buttons) is not
    // resized, so the edge before it has no grip.
    private static List<Thumb> AddGrips(Grid grid, ColumnLayout layout, Action save)
    {
        var grips = new List<Thumb>();
        for (var i = 0; i < layout.Count - 2; i++)
        {
            var left = i;
            if (!layout.IsFixed(left) && !layout.IsFixed(left + 1)) continue;
            var grip = new Thumb
            {
                Classes = { "column-grip" },
                Width = 9,
                Margin = new Thickness(0, -2, -1, -2),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
                Focusable = false,
                ZIndex = 1,
                Tag = left,
            };
            ToolTip.SetTip(grip, "Drag to move this edge: the column on one side grows, the other shrinks. Double-click to put both back.");
            Grid.SetColumn(grip, i);
            Grid.SetRowSpan(grip, Math.Max(1, grid.RowDefinitions.Count));
            // A column auto-size widened is dragged from the width on screen, not the narrower one stored.
            grip.DragStarted += (_, _) =>
            {
                var shown = layout.Fit(grid.Bounds.Width);
                layout.Adopt(shown, left);
                layout.Adopt(shown, left + 1);
            };
            grip.DragDelta += (_, e) => layout.MoveEdge(left, e.Vector.X, grid.Bounds.Width);
            grip.DragCompleted += (_, _) => save();
            grip.DoubleTapped += (_, e) =>
            {
                layout.Reset(left);
                layout.Reset(left + 1);
                save();
                e.Handled = true;
            };
            grid.Children.Add(grip);
            grips.Add(grip);
        }
        return grips;
    }

    /// <summary>The width each of <paramref name="columns"/> needs to show its cells' text in full, by column index.</summary>
    internal static double[] MeasureColumns(Grid grid, IReadOnlyList<int> columns, int count)
    {
        var needs = new double[count];
        foreach (var child in grid.Children)
        {
            if (child is Thumb || Grid.GetColumnSpan(child) != 1) continue;
            var column = Grid.GetColumn(child);
            if (column < count && columns.Contains(column)) needs[column] = Math.Max(needs[column], NaturalWidth(child));
        }
        return needs;
    }

    /// <summary>
    /// How wide a cell would be with no limit: text is measured on its own (a trimmed TextBlock's desired size is only
    /// as wide as its column), panels add up or take the widest of what they hold, and anything else keeps its desired
    /// width. Hidden content takes no room.
    /// </summary>
    internal static double NaturalWidth(Control c)
    {
        if (!c.IsVisible) return 0;
        var margin = c.Margin.Left + c.Margin.Right;
        switch (c)
        {
            case TextBlock t:
                var padding = margin + t.Padding.Left + t.Padding.Right;
                return string.IsNullOrEmpty(t.Text) ? padding
                    : padding + TextWidth(t.Text, new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize);
            case StackPanel panel:
                var shown = panel.Children.Where(x => x.IsVisible).Select(NaturalWidth).ToList();
                if (shown.Count == 0) return margin;
                return margin + (panel.Orientation == Orientation.Horizontal ? shown.Sum() + panel.Spacing * (shown.Count - 1) : shown.Max());
            case Border { Child: Control child } border:
                return margin + border.Padding.Left + border.Padding.Right + border.BorderThickness.Left + border.BorderThickness.Right + NaturalWidth(child);
            default:
                return double.IsNaN(c.Width) ? c.DesiredSize.Width : c.Width + margin;
        }
    }

    private static readonly Dictionary<(string Text, Typeface Face, double Size), double> TextWidths = [];

    // Rows re-measure after every layout pass, so widths are kept: the same place names and states come round again.
    private static double TextWidth(string text, Typeface face, double size)
    {
        if (TextWidths.TryGetValue((text, face, size), out var width)) return width;
        if (TextWidths.Count > 4096) TextWidths.Clear();
        using var layout = new TextLayout(text, face, size, null);
        width = Math.Ceiling(layout.WidthIncludingTrailingWhitespace);
        TextWidths[(text, face, size)] = width;
        return width;
    }
}
