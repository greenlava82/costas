// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Ft8Client.Core.Spectrum;

namespace Ft8Client.App.Views;

/// <summary>
/// Receive history with a clickable transmit-channel marker. The compact strip stretches its newest 32 rows; a taller
/// view shows more time at about 1.6 pixels a row, up to <see cref="MaxRows"/>, with faint lines at slot boundaries.
/// FT8 and FT4 callsign tags are kept with the row that was newest when their decode arrived, so they scroll down with
/// the signal they belong to (<see cref="AddCallTags"/>).
/// </summary>
public sealed class WaterfallView : Control
{
    /// <summary>Rows of history kept (about 80 seconds at five rows a second).</summary>
    public const int MaxRows = 400;
    private const int CompactRows = 32;
    private const double PixelsPerRow = 1.6;
    private const double ScaleHeight = 18;
    private const double CallTagFont = 10;
    private const int CallTagCacheLimit = 512;

    private static readonly IBrush TagBackground = new SolidColorBrush(Color.FromArgb(170, 7, 18, 30));
    private static readonly IBrush ContactTag = new SolidColorBrush(Color.FromRgb(196, 43, 28));
    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#07121E"));
    private static readonly IPen SlotPen = new Pen(new SolidColorBrush(Color.FromArgb(56, 200, 215, 230)), 1);
    private static readonly IPen MarkerPen = new Pen(Brushes.OrangeRed, 1.5);
    private static readonly IBrush FilteredOut = new SolidColorBrush(Color.FromArgb(150, 7, 18, 30));
    private static readonly IPen FilterEdgePen = new Pen(new SolidColorBrush(Color.FromArgb(200, 230, 200, 120)), 1, DashStyle.Dash);

    // Newest row first in display order; stored as a ring of BGRA pixels, with each bin's level relative to the noise
    // floor alongside so a change of scale recolours the history.
    private int[] _pixels = [];
    private float[] _levels = [];
    private readonly NoiseFloorTracker _floor = new();
    private WaterfallLevels _scale = WaterfallLevels.Default;
    private string _palette = WaterfallPalette.Wsjtx;
    private int[] _colours = WaterfallPalette.Colours(WaterfallPalette.Wsjtx);
    private readonly bool[] _slotStart = new bool[MaxRows];
    private int _bins;
    private int _head;
    private int _count;
    private WriteableBitmap? _bitmap;
    private int _bitmapRows = -1;
    private bool _dirty;
    private int _offset = 1500;
    private IReadOnlyList<(int Hz, string Label, bool InContact)> _tags = [];
    private int? _passband;
    private readonly WaterfallTag[]?[] _rowTags = new WaterfallTag[]?[MaxRows];
    private bool _showCallTags = true;
    private readonly Dictionary<(string, WaterfallTagKind), FormattedText> _callTagText = [];
    private static readonly Typeface CallTagFace = new("Inter", FontStyle.Normal, FontWeight.SemiBold);

    // Colours for the tags over the always-dark waterfall, the same in every theme; hues follow the station list.
    private static readonly IBrush NormalText = Brushes.White;
    private static readonly IBrush WorkedText = new SolidColorBrush(Color.Parse("#9AA6B2"));
    private static readonly IBrush NewBandText = new SolidColorBrush(Color.Parse("#8CC4FF"));
    private static readonly IBrush NewCountryText = new SolidColorBrush(Color.Parse("#FFB65C"));

    /// <summary>
    /// True to draw callsigns on the waterfall: FT8 and FT4 decodes over their signals, and the keyboard-mode tags.
    /// The tags are kept while hidden, so switching back on shows those still in view.
    /// </summary>
    public bool ShowCallTags
    {
        get => _showCallTags;
        set
        {
            if (value == _showCallTags) return;
            _showCallTags = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Puts FT8 or FT4 callsign tags on the newest row, which then scrolls down with the signals the decodes came from.
    /// Ignored before the first row of audio, when there is nothing to put them on.
    /// </summary>
    public void AddCallTags(IReadOnlyList<WaterfallTag> tags)
    {
        if (tags.Count == 0 || _count == 0) return;
        var row = _rowTags[_head];
        _rowTags[_head] = row is null ? [.. tags] : [.. row, .. tags];
        if (_showCallTags) InvalidateVisual();
    }

    /// <summary>The callsign tags on each visible row, newest row first; for tests.</summary>
    public IReadOnlyList<IReadOnlyList<WaterfallTag>> CallTagRows(int rows) =>
        Enumerable.Range(0, Math.Min(rows, _count)).Select(i => (IReadOnlyList<WaterfallTag>)(_rowTags[(_head + i) % MaxRows] ?? [])).ToList();

    /// <summary>
    /// The radio's receive filter width in hertz (Aurora comparison, owner request 2026-11-07). Audio above it is dimmed
    /// with a dashed edge, since the radio filters it out; null, or a width of 3000 Hz or more, draws nothing.
    /// </summary>
    public void SetPassband(int? hz)
    {
        if (hz == _passband) return;
        _passband = hz;
        InvalidateVisual();
    }

    /// <summary>The filter edge to draw in hertz, or null when the filter is wider than the waterfall.</summary>
    public static int? FilterEdge(int? passbandHz) => passbandHz is > 0 and < 3000 ? passbandHz : null;

    /// <summary>
    /// Callsign tags drawn above the signals they belong to (keyboard modes, docs/09-keyboard-modes.md section 10): "?"
    /// for a signal with no callsign yet, and the station in contact in red.
    /// </summary>
    public void SetTags(IReadOnlyList<(int Hz, string Label, bool InContact)> tags)
    {
        if (tags.SequenceEqual(_tags)) return;
        _tags = tags;
        InvalidateVisual();
    }

    /// <summary>Raised for an explicit click in the selectable 200–2900 Hz range.</summary>
    public event Action<int>? ChannelSelected;

    /// <summary>Space kept clear of frequency labels at the right edge, for a button laid over the scale.</summary>
    public double LabelInset { get; set; }

    /// <summary>Updates the marker and optionally adds a row of real receive audio.</summary>
    /// <param name="row">Levels in dBFS across 0–3000 Hz, or null for no new audio.</param>
    /// <param name="offset">Transmit offset in hertz.</param>
    /// <param name="slotStart">True when this row is the first of a new receive slot.</param>
    public void Update(float[]? row, int offset, bool slotStart = false)
    {
        _offset = offset;
        if (row is not null && row.Length > 0)
        {
            if (row.Length != _bins)
            {
                _bins = row.Length;
                _pixels = new int[_bins * MaxRows];
                _levels = new float[_bins * MaxRows];
                _head = 0;
                _count = 0;
                _floor.Reset();
                Array.Clear(_rowTags);
                _bitmap?.Dispose();
                _bitmap = null;
            }
            _head = (_head + MaxRows - 1) % MaxRows;
            _rowTags[_head] = null;
            var start = _head * _bins;
            var (first, last) = NoiseRange(_bins, _passband);
            var floor = _floor.Update(row, first, last);
            for (var bin = 0; bin < _bins; bin++)
            {
                var level = (float)(row[bin] - floor);
                _levels[start + bin] = level;
                _pixels[start + bin] = WaterfallPalette.For(_colours, _scale, level);
            }
            _slotStart[_head] = slotStart;
            _count = Math.Min(MaxRows, _count + 1);
            _dirty = true;
        }
        InvalidateVisual();
    }

    /// <summary>The colour scale in use.</summary>
    public WaterfallLevels Levels => _scale;

    /// <summary>Sets the colour scale and recolours the history with it.</summary>
    public void SetLevels(WaterfallLevels levels)
    {
        levels = levels.Clamped();
        if (levels == _scale) return;
        _scale = levels;
        Recolour();
    }

    /// <summary>The palette in use (<see cref="WaterfallPalette.Choices"/>).</summary>
    public string Palette => _palette;

    /// <summary>Sets the palette and recolours the history with it.</summary>
    public void SetPalette(string? palette)
    {
        palette = WaterfallPalette.Known(palette);
        if (palette == _palette) return;
        _palette = palette;
        _colours = WaterfallPalette.Colours(palette);
        Recolour();
    }

    private void Recolour()
    {
        for (var i = 0; i < _levels.Length; i++) _pixels[i] = WaterfallPalette.For(_colours, _scale, _levels[i]);
        _dirty = true;
        InvalidateVisual();
    }

    /// <summary>
    /// The bins the noise floor is taken from: 200 to 2900 Hz, the receive range, stopping 100 Hz short of a narrower
    /// radio filter, whose skirt would pull the floor down.
    /// </summary>
    public static (int First, int Last) NoiseRange(int bins, int? passbandHz)
    {
        var topHz = FilterEdge(passbandHz) is { } edge ? Math.Max(300, Math.Min(2900, edge - 100)) : 2900;
        return (200 * bins / 3000, Math.Max(200 * bins / 3000, topHz * bins / 3000 - 1));
    }

    /// <summary>Clears history when changing bands or receive devices.</summary>
    public void Clear()
    {
        _floor.Reset();
        _count = 0;
        Array.Clear(_rowTags);
        _dirty = true;
        InvalidateVisual();
    }

    /// <summary>Copies another view's history, so a newly shown waterfall starts full.</summary>
    public void CopyFrom(WaterfallView other)
    {
        _bins = other._bins;
        _levels = (float[])other._levels.Clone();
        _pixels = new int[_levels.Length];
        _scale = other._scale;
        _palette = other._palette;
        _colours = other._colours;
        for (var i = 0; i < _levels.Length; i++) _pixels[i] = WaterfallPalette.For(_colours, _scale, _levels[i]);
        _floor.CopyFrom(other._floor);
        Array.Copy(other._slotStart, _slotStart, MaxRows);
        Array.Copy(other._rowTags, _rowTags, MaxRows);
        _showCallTags = other._showCallTags;
        _head = other._head;
        _count = other._count;
        _offset = other._offset;
        _bitmap?.Dispose();
        _bitmap = null;
        _dirty = true;
        InvalidateVisual();
    }

    /// <summary>Rows shown for a plot of this height: the compact strip keeps today's 32, taller shows more time.</summary>
    public static int VisibleRows(double plotHeight) =>
        Math.Clamp((int)Math.Round(plotHeight / PixelsPerRow), CompactRows, MaxRows);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 20) return;
        context.FillRectangle(Background, new Rect(Bounds.Size));
        var plotHeight = height - ScaleHeight;
        var rows = VisibleRows(plotHeight);
        var shown = Math.Min(rows, _count);
        if (shown > 0 && _bins > 0)
        {
            var bitmap = Fill(rows, shown);
            var rowHeight = plotHeight / rows;
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                context.DrawImage(bitmap, new Rect(0, 0, _bins, shown), new Rect(0, ScaleHeight, width, rowHeight * shown));
            if (rows > CompactRows)
                for (var i = 0; i < shown; i++)
                {
                    if (!_slotStart[(_head + i) % MaxRows]) continue;
                    // The first row of a slot sits below the rows that came after it; the boundary is its top edge.
                    var y = ScaleHeight + (i + 1) * rowHeight;
                    context.DrawLine(SlotPen, new Point(0, y), new Point(width, y));
                }
            if (_showCallTags)
                using (context.PushClip(new Rect(0, ScaleHeight, width, plotHeight)))
                    DrawCallTags(context, width, plotHeight, rowHeight, shown);
        }
        var right = Math.Max(2, width - LabelInset);
        for (var hz = 0; hz <= 3000; hz += 500)
        {
            var text = new FormattedText(hz == 3000 ? "3000 Hz" : hz.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 10, Brushes.LightGray);
            var x = Math.Clamp(hz / 3000.0 * width - text.Width / 2, 2, Math.Max(2, right - text.Width - 2));
            // A label pushed left into its neighbour by the inset is dropped rather than overlapping it.
            if (hz == 3000 && LabelInset > 0 && x < 2500 / 3000.0 * width + 30) continue;
            context.DrawText(text, new Point(x, 1));
        }
        var lastRight = double.MinValue;
        foreach (var (hz, label, inContact) in (_showCallTags ? _tags : []).OrderBy(t => t.Hz))
        {
            var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                11, Brushes.White);
            var tx = Math.Clamp(hz / 3000.0 * width - text.Width / 2, 2, Math.Max(2, width - text.Width - 2));
            // Neighbours too close to label side by side take turns in two rows.
            var ty = tx < lastRight + 4 ? ScaleHeight + 16 : ScaleHeight + 2;
            if (ty == ScaleHeight + 2) lastRight = tx + text.Width;
            context.FillRectangle(inContact ? ContactTag : TagBackground, new Rect(tx - 2, ty - 1, text.Width + 4, text.Height + 2));
            context.DrawText(text, new Point(tx, ty));
        }
        if (FilterEdge(_passband) is { } edgeHz)
        {
            var edge = edgeHz / 3000.0 * width;
            context.FillRectangle(FilteredOut, new Rect(edge, ScaleHeight, Math.Max(0, width - edge), plotHeight));
            context.DrawLine(FilterEdgePen, new Point(edge, ScaleHeight), new Point(edge, height));
            var label = new FormattedText($"Filter {edgeHz.ToString(CultureInfo.InvariantCulture)} Hz", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Inter"), 10, Brushes.LightGray);
            var lx = edge + 4 + label.Width < width ? edge + 4 : Math.Max(2, edge - label.Width - 4);
            context.DrawText(label, new Point(lx, height - label.Height - 2));
        }
        var marker = _offset / 3000.0 * width;
        context.DrawLine(MarkerPen, new Point(marker, ScaleHeight), new Point(marker, height));
        if (_count == 0 && _tags.Count == 0)
        {
            var text = new FormattedText("Waiting for receive audio", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Inter"), 11, Brushes.LightGray);
            context.DrawText(text, new Point(Math.Max(4, (width - text.Width) / 2), 22));
        }
    }

    private void DrawCallTags(DrawingContext context, double width, double plotHeight, double rowHeight, int shown)
    {
        List<CallTagBox>? boxes = null;
        List<(FormattedText Text, WaterfallTagKind Kind)>? texts = null;
        for (var i = 0; i < shown; i++)
        {
            if (_rowTags[(_head + i) % MaxRows] is not { } row) continue;
            boxes ??= [];
            texts ??= [];
            foreach (var tag in row)
            {
                var text = CallTagText(tag.Label, tag.Kind);
                boxes.Add(new CallTagBox(tag.Hz / 3000.0 * width, ScaleHeight + i * rowHeight, text.Width, text.Height));
                texts.Add((text, tag.Kind));
            }
        }
        if (boxes is null || texts is null) return;
        var placed = LayoutCallTags(boxes, width, ScaleHeight + plotHeight, CallTagLines(plotHeight));
        for (var i = 0; i < placed.Count; i++)
        {
            if (placed[i] is not { } r) continue;
            var (text, kind) = texts[i];
            context.FillRectangle(kind == WaterfallTagKind.You ? ContactTag : TagBackground, r, 2);
            context.DrawText(text, new Point(r.X + 2, r.Y + 1));
        }
    }

    private FormattedText CallTagText(string label, WaterfallTagKind kind)
    {
        if (_callTagText.TryGetValue((label, kind), out var text)) return text;
        if (_callTagText.Count >= CallTagCacheLimit) _callTagText.Clear();
        var brush = kind switch
        {
            WaterfallTagKind.Worked => WorkedText,
            WaterfallTagKind.NewBand => NewBandText,
            WaterfallTagKind.NewCountry => NewCountryText,
            _ => NormalText,
        };
        text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, CallTagFace, CallTagFont, brush);
        _callTagText[(label, kind)] = text;
        return text;
    }

    /// <summary>
    /// Lines of tags allowed below a row whose neighbours crowd it: one on the compact strip, where a second would cover
    /// most of it, and up to three on a taller waterfall.
    /// </summary>
    public static int CallTagLines(double plotHeight) => plotHeight < 80 ? 1 : plotHeight < 160 ? 2 : 3;

    /// <summary>
    /// Places callsign tags so none overlaps another. Each goes centred on its frequency at the top of its row, kept
    /// inside the edges; one that would overlap a tag already placed moves down a line at a time, up to
    /// <paramref name="lines"/> lines, and is left out when there is no room (the station list still shows it). Earlier
    /// boxes win, so pass the newest row first and each row's tags from the lowest frequency.
    /// </summary>
    /// <returns>The background rectangle of each tag in input order (text inset 2 px across and 1 px down), or null for one left out.</returns>
    public static IReadOnlyList<Rect?> LayoutCallTags(IReadOnlyList<CallTagBox> boxes, double width, double bottom, int lines)
    {
        var placed = new List<Rect>(boxes.Count);
        var result = new Rect?[boxes.Count];
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            var w = b.Width + 4;
            var h = b.Height + 2;
            var x = Math.Clamp(b.CenterX - w / 2, 0, Math.Max(0, width - w));
            for (var line = 0; line < Math.Max(1, lines); line++)
            {
                var r = new Rect(x, b.Top + line * (h + 1), w, h);
                // Below the bottom a tag would be cut off; the first line is always tried, as the clip shows its top.
                if (line > 0 && r.Bottom > bottom) break;
                // A 2 px gap is kept between tags side by side.
                if (placed.Any(p => p.Intersects(r.Inflate(new Thickness(2, 0))))) continue;
                placed.Add(r);
                result[i] = r;
                break;
            }
        }
        return result;
    }

    // Copies the newest rows, newest first, into a bitmap one pixel per bin and per row.
    private WriteableBitmap Fill(int rows, int shown)
    {
        if (_bitmap is null || _bitmapRows != rows)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(_bins, rows), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _bitmapRows = rows;
            _dirty = true;
        }
        if (!_dirty) return _bitmap;
        using var frame = _bitmap.Lock();
        for (var i = 0; i < shown; i++)
            Marshal.Copy(_pixels, (_head + i) % MaxRows * _bins, frame.Address + i * frame.RowBytes, _bins);
        _dirty = false;
        return _bitmap;
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Bounds.Width <= 0) return;
        var hz = (int)Math.Round(e.GetPosition(this).X / Bounds.Width * 3000);
        if (hz < 200 || hz > 2900) return;
        ChannelSelected?.Invoke(hz);
        e.Handled = true;
    }
}
