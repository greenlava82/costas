// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.App.Views;

/// <summary>
/// The column widths shared by one or more tables, such as Next in line and Repeats. Fixed columns can be resized;
/// a star column (NaN here) takes what is left and is never stored. With <see cref="AutoSize"/> the tables also report
/// how wide their text is, and room the star column does not need goes to the columns whose text is cut off.
/// </summary>
public sealed class ColumnLayout
{
    /// <summary>No column is narrower than this.</summary>
    public const double MinWidth = 24;

    /// <summary>A star column keeps at least this much room when another column grows.</summary>
    public const double MinStarRoom = 60;

    private readonly double[] _defaults;
    private readonly double[] _widths;
    // Columns the operator has sized by dragging (or loaded from settings with a width of their own): auto-size leaves them be.
    private readonly bool[] _sized;
    private readonly double[] _needs;
    private readonly Dictionary<object, double[]> _reported = new(ReferenceEqualityComparer.Instance);
    private int _star = -1;
    private int[] _grow = [];

    /// <summary>Creates a layout from the widths written in the view; NaN marks a star or auto column.</summary>
    public ColumnLayout(IReadOnlyList<double> defaults)
    {
        _defaults = [.. defaults];
        _widths = [.. defaults];
        _sized = new bool[_widths.Length];
        _needs = new double[_widths.Length];
    }

    /// <summary>Raised whenever a width changes, during a drag too.</summary>
    public event Action? Changed;

    /// <summary>Number of columns.</summary>
    public int Count => _widths.Length;

    /// <summary>Current widths; NaN for a star or auto column.</summary>
    public IReadOnlyList<double> Widths => _widths;

    /// <summary>True when the column has a width that can be dragged.</summary>
    public bool IsFixed(int column) => column >= 0 && column < _widths.Length && !double.IsNaN(_widths[column]);

    /// <summary>True when any width differs from the view's default.</summary>
    public bool IsChanged => !_widths.SequenceEqual(_defaults);

    /// <summary>Sets one column's width, kept at least <see cref="MinWidth"/> and, given the table width, leaving a star column room.</summary>
    public void SetWidth(int column, double width, double tableWidth = double.PositiveInfinity)
    {
        if (!IsFixed(column)) return;
        var others = 0.0;
        for (var i = 0; i < _widths.Length; i++)
            if (i != column && IsFixed(i)) others += _widths[i];
        // On a table already too narrow for its columns a column can still shrink, but not grow.
        var max = _widths.Any(double.IsNaN) ? Math.Max(_widths[column], tableWidth - others - MinStarRoom) : double.PositiveInfinity;
        var w = Math.Round(Math.Max(MinWidth, Math.Min(width, max)));
        if (w == _widths[column]) return;
        _widths[column] = w;
        _sized[column] = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// Turns on auto-size: <paramref name="star"/> is the column that takes what is left (Doing now), and the
    /// <paramref name="grow"/> columns (Where, Why) widen into the star column's spare room, as far as their text needs,
    /// while the operator has not dragged them. The star column keeps room for its own text first.
    /// </summary>
    public void AutoSize(int star, IReadOnlyList<int> grow)
    {
        _star = star >= 0 && star < _widths.Length && !IsFixed(star) ? star : -1;
        _grow = [.. grow.Where(IsFixed).Distinct()];
        Changed?.Invoke();
    }

    /// <summary>True when the column widens to fit its text: an auto-size column the operator has not dragged.</summary>
    public bool IsAutoSized(int column) => _star >= 0 && _grow.Contains(column) && !_sized[column];

    /// <summary>The columns whose text width the tables report: the auto-size columns, the star column, and any auto column.</summary>
    public IReadOnlyList<int> MeasuredColumns =>
        _star < 0 ? [] : [.. Enumerable.Range(0, _widths.Length).Where(i => !IsFixed(i) || _grow.Contains(i))];

    /// <summary>The widest text reported for each column (0 for a column nobody measures).</summary>
    public IReadOnlyList<double> Needs => _needs;

    /// <summary>
    /// Records how wide one table row's (or header's) text is in each column, by column index; null when the row is gone
    /// or hidden. The layout keeps the widest of all rows, and raises <see cref="Changed"/> only when that changes.
    /// </summary>
    public void ReportNeeds(object source, IReadOnlyList<double>? needs)
    {
        if (needs is null)
        {
            if (!_reported.Remove(source)) return;
        }
        else
        {
            var copy = new double[_widths.Length];
            for (var i = 0; i < copy.Length && i < needs.Count; i++)
                copy[i] = double.IsFinite(needs[i]) && needs[i] > 0 ? Math.Ceiling(needs[i]) : 0;
            if (_reported.TryGetValue(source, out var old) && old.SequenceEqual(copy)) return;
            _reported[source] = copy;
        }
        var changed = false;
        for (var i = 0; i < _needs.Length; i++)
        {
            var max = 0.0;
            foreach (var row in _reported.Values) max = Math.Max(max, row[i]);
            if (max != _needs[i])
            {
                _needs[i] = max;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Before a drag: a column shown wider than its stored width (auto-size widened it) takes the shown width, so the
    /// edge moves from where the operator sees it. Raises nothing, as the table looks the same.
    /// </summary>
    public void Adopt(IReadOnlyList<double> shown, int column)
    {
        if (!IsFixed(column) || column >= shown.Count || !(shown[column] > _widths[column])) return;
        _widths[column] = Math.Round(shown[column]);
    }

    private int[] _hidden = [];

    /// <summary>The columns hidden whatever the window width (PSK and RTTY hide the FT8-only ones).</summary>
    public IReadOnlyList<int> Hidden => _hidden;

    /// <summary>
    /// Hides <paramref name="columns"/> (width 0 in <see cref="Fit"/>) until called again; an empty list shows them all.
    /// Their stored widths are kept, so they come back as they were, and the room they free goes to the star column.
    /// </summary>
    public void Hide(IReadOnlyList<int> columns)
    {
        int[] hidden = [.. columns.Where(c => c >= 0 && c < _widths.Length).Distinct().Order()];
        if (hidden.SequenceEqual(_hidden)) return;
        _hidden = hidden;
        Changed?.Invoke();
    }

    private (int Column, double Min)[] _shrink = [];
    private int[] _drop = [];

    /// <summary>
    /// Says how the table gives way on a window too narrow for its columns: first the <paramref name="shrink"/>
    /// columns narrow, in order, down to their minimum (their text trims), then the <paramref name="drop"/> columns
    /// hide, in order. Columns named in neither, the last one (the Call buttons) included, always keep their width.
    /// </summary>
    public void GiveWay(IReadOnlyList<(int Column, double Min)> shrink, IReadOnlyList<int> drop)
    {
        _shrink = [.. shrink];
        _drop = [.. drop];
        Changed?.Invoke();
    }

    /// <summary>
    /// The widths to show in a table this wide: the operator's widths when they fit, otherwise narrowed and with
    /// columns hidden (width 0) as <see cref="GiveWay"/> says, so the last column stays on screen. When everything fits,
    /// auto-size (<see cref="AutoSize"/>) widens cut-off columns into the star column's spare room. NaN for a star
    /// column. A width of 0 or less means the table has not been laid out yet, and nothing gives way.
    /// </summary>
    public double[] Fit(double tableWidth)
    {
        double[] w = [.. _widths];
        foreach (var column in _hidden)
            if (IsFixed(column)) w[column] = 0;
        if (tableWidth <= 0) return w;
        double Need() => w.Where(x => !double.IsNaN(x)).Sum();
        var narrowed = false;
        foreach (var (column, min) in _shrink)
        {
            if (Need() - tableWidth <= 0) break;
            narrowed = true;
            if (IsFixed(column) && !_hidden.Contains(column)) w[column] = Math.Max(Math.Min(min, w[column]), w[column] - Math.Ceiling(Need() - tableWidth));
        }
        if (!narrowed) return Grow(w, tableWidth);
        foreach (var column in _drop)
        {
            if (Need() <= tableWidth) break;
            if (IsFixed(column)) w[column] = 0;
        }
        // Room a hidden column freed beyond what was needed goes back to the narrowed columns, last narrowed first.
        foreach (var (column, _) in Enumerable.Reverse(_shrink))
        {
            var spare = Math.Floor(tableWidth - Need());
            if (spare <= 0) break;
            if (IsFixed(column) && !_hidden.Contains(column)) w[column] = Math.Min(_widths[column], w[column] + spare);
        }
        return w;
    }

    // Auto-size: what the star column has beyond room for its own text (and any auto column's) goes to the auto-size
    // columns whose text is cut off, shared in proportion to what each is short. A column is never made wider than its
    // text needs, never narrower than its width, and a hidden column (0) stays hidden.
    private double[] Grow(double[] w, double tableWidth)
    {
        if (_star < 0) return w;
        var used = 0.0;
        for (var i = 0; i < w.Length; i++)
            used += IsFixed(i) ? w[i] : i == _star ? 0 : _needs[i];
        var spare = Math.Floor(tableWidth - used - Math.Max(MinStarRoom, _needs[_star]));
        if (spare <= 0) return w;
        var short_ = _grow.Where(c => IsAutoSized(c) && w[c] > 0 && _needs[c] > w[c]).Select(c => (Column: c, By: _needs[c] - w[c])).ToList();
        var total = short_.Sum(x => x.By);
        if (total <= 0) return w;
        foreach (var (column, by) in short_)
            w[column] += total <= spare ? by : Math.Floor(spare * by / total);
        return w;
    }

    /// <summary>
    /// Moves the edge between <paramref name="left"/> and the column after it by <paramref name="delta"/> (positive is
    /// right). Between two fixed columns one grows by what the other gives up, so the table keeps its width and no other
    /// column moves; neither goes below <see cref="MinWidth"/>. Beside the star column only the fixed column changes,
    /// and the star column takes up the difference (as <see cref="SetWidth"/>).
    /// </summary>
    public void MoveEdge(int left, double delta, double tableWidth = double.PositiveInfinity)
    {
        var right = left + 1;
        if (right >= _widths.Length || _hidden.Contains(left) || _hidden.Contains(right)) return;
        var leftFixed = IsFixed(left);
        var rightFixed = IsFixed(right);
        if (leftFixed && rightFixed)
        {
            var d = Math.Round(Math.Clamp(delta, Math.Min(0, MinWidth - _widths[left]), Math.Max(0, _widths[right] - MinWidth)));
            if (d == 0) return;
            _widths[left] += d;
            _widths[right] -= d;
            _sized[left] = _sized[right] = true;
            Changed?.Invoke();
        }
        else if (leftFixed)
        {
            SetWidth(left, _widths[left] + delta, tableWidth);
        }
        else if (rightFixed)
        {
            SetWidth(right, _widths[right] - delta, tableWidth);
        }
    }

    /// <summary>Puts one column back to its default width.</summary>
    public void Reset(int column)
    {
        if (!IsFixed(column)) return;
        var wasSized = _sized[column];
        _sized[column] = false;
        if (_widths[column] == _defaults[column] && !(wasSized && IsAutoSized(column))) return;
        _widths[column] = _defaults[column];
        Changed?.Invoke();
    }

    /// <summary>The fixed widths in column order, for settings; star columns are left out.</summary>
    public double[] ToSaved() => [.. _widths.Where(w => !double.IsNaN(w))];

    /// <summary>
    /// Applies widths saved by <see cref="ToSaved"/>. A list that does not match this table's columns (the view
    /// changed since it was saved) is ignored, so the defaults stay.
    /// </summary>
    public void Load(IReadOnlyList<double>? saved)
    {
        var fixedCount = _defaults.Count(w => !double.IsNaN(w));
        if (saved is null || saved.Count != fixedCount || saved.Any(w => double.IsNaN(w) || double.IsInfinity(w))) return;
        var k = 0;
        for (var i = 0; i < _widths.Length; i++)
            if (!double.IsNaN(_defaults[i]))
            {
                _widths[i] = Math.Max(MinWidth, Math.Round(saved[k++]));
                _sized[i] = _widths[i] != _defaults[i];
            }
        Changed?.Invoke();
    }
}
