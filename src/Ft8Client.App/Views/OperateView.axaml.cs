// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ft8Client.App.ViewModels;
using Ft8Client.Core.Keyboard;

namespace Ft8Client.App.Views;

/// <summary>The Operate view.</summary>
public partial class OperateView : UserControl
{
    /// <summary>"Show all" while Watching is folded, "Fold" once opened.</summary>
    public static readonly IValueConverter WatchingToggleText = new FuncValueConverter<bool, string>(open => open ? "Fold" : "Show all");

    /// <summary>Creates the view.</summary>
    public OperateView()
    {
        InitializeComponent();
        LineList.PointerEntered += (_, _) => Vm?.PointerEnteredLine();
        LineList.PointerExited += (_, _) => Vm?.PointerExitedLine();
        LineList.AddHandler(PointerPressedEvent, OnRowPressed, RoutingStrategies.Tunnel);
        RepeatList.AddHandler(PointerPressedEvent, OnRepeatPressed, RoutingStrategies.Tunnel);
        RepeatList.PointerEntered += (_, _) => Vm?.PointerEnteredRepeats();
        RepeatList.PointerExited += (_, _) => Vm?.PointerExitedRepeats();
        SizeChanged += (_, e) => Relayout(e.NewSize.Width);
        LeftPane.SizeChanged += (_, _) => FitPinned();
        CallingYou.SizeChanged += (_, _) => FitPinned();
        ContactCard.SizeChanged += (_, _) => FitPinned();
        SummaryCard.SizeChanged += (_, e) =>
        {
            ReflowSummary(e.NewSize.Width);
            FitPinned();
        };
        CallingYou.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty) FitPinned();
        };
        TypeAhead.AddHandler(KeyDownEvent, OnTypeAheadKey, RoutingStrategies.Tunnel);
        TypeAhead.AddHandler(TextInputEvent, OnTypeAheadText, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (Vm is not { } vm) return;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(OperateViewModel.SideCollapsed) or nameof(OperateViewModel.Compact)) Relayout(Bounds.Width);
            };
            FollowWindowSize();
            Relayout(Bounds.Width);
            // Follow new text only while the operator is at the end; scrolled back to read, the box stays put.
            vm.Keyboard.Conversation.CollectionChanged += (_, _) =>
            {
                var s = ConversationScroll;
                if (s.Offset.Y >= s.Extent.Height - s.Viewport.Height - 4) Dispatcher.UIThread.Post(() => s.ScrollToEnd());
            };
            vm.Keyboard.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(KeyboardPanelViewModel.BlockedMacro)) ShowBlocked(vm.Keyboard.BlockedMacro);
                // The arriving line grows character by character: keep it in view the same way.
                if (e.PropertyName == nameof(KeyboardPanelViewModel.LiveText))
                {
                    var s = ConversationScroll;
                    if (s.Offset.Y >= s.Extent.Height - s.Viewport.Height - 24) Dispatcher.UIThread.Post(() => s.ScrollToEnd());
                }
            };
        };
    }

    // PSK and RTTY: Enter sends; on the air, Backspace takes back what has not gone yet.
    private void OnTypeAheadKey(object? sender, KeyEventArgs e)
    {
        if (Vm?.Keyboard is not { } k) return;
        if (e.Key == Key.Enter)
        {
            k.Enter();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && k.OnAir)
        {
            k.Backspace();
            e.Handled = true;
        }
    }

    // The refused macro as it would go out, each field with no value in red (docs/09-keyboard-modes.md section 8).
    private void ShowBlocked(IReadOnlyList<MacroSegment> segments)
    {
        var inlines = BlockedLine.Inlines ??= [];
        inlines.Clear();
        foreach (var s in segments)
        {
            var run = new Run(s.Text);
            if (s.Missing)
            {
                // FindResource does not see the theme dictionaries, so it returned null and the field showed blank.
                run.Foreground = Converters.Brush("CriticalBrush");
                run.FontWeight = FontWeight.SemiBold;
            }
            inlines.Add(run);
        }
    }

    // On the air every key goes straight to the transmitter instead of into the box; a key it cannot take is kept in
    // the box or, if the mode cannot send it, named under the line. Receiving in RTTY, the box shows capitals.
    private void OnTypeAheadText(object? sender, TextInputEventArgs e)
    {
        _spacePending = false;
        if (Vm?.Keyboard is not { } k || string.IsNullOrEmpty(e.Text)) return;
        if (k.OnAir)
        {
            k.TypeOnAir(e.Text);
            TypeAhead.CaretIndex = k.Typed.Length;
            e.Handled = true;
        }
        else if (k.IsRtty)
        {
            e.Text = e.Text.ToUpperInvariant();
        }
    }

    // Space pressed while another control had the keyboard: the type-ahead line now has it. Set until the space arrives
    // as text; a platform that sends no text for a handled key gets the space on key up instead.
    private bool _spacePending;

    /// <summary>
    /// PSK and RTTY: a key pressed while the keyboard is with another control on this view (a button, a row, the
    /// window). Typing belongs to the type-ahead line, so it takes the keyboard. Space must not click the focused button
    /// (which could key the radio again), and Backspace edits the line. Returns true when the key was taken.
    /// </summary>
    public bool TakeKey(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (Vm?.Keyboard is not { Visible: true } k) return false;
        switch (e.Key)
        {
            case Key.Space:
                FocusTypeAhead();
                _spacePending = true;
                return true;
            case Key.Back:
                FocusTypeAhead();
                k.BackspaceAnywhere();
                TypeAhead.CaretIndex = k.Typed.Length;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Key up after <see cref="TakeKey"/>: a space that never arrived as text is typed now.</summary>
    public void TakeKeyUp(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key != Key.Space || !_spacePending) return;
        _spacePending = false;
        TakeText(" ");
    }

    /// <summary>
    /// PSK and RTTY: text typed while another control had the keyboard goes to the type-ahead line (or, on the air, to
    /// the transmitter), and the line takes the keyboard. Returns true when the text was taken.
    /// </summary>
    public bool TakeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _spacePending = false;
        if (Vm?.Keyboard is not { Visible: true } k || text.Length == 0) return false;
        FocusTypeAhead();
        k.TypeAnywhere(text);
        TypeAhead.CaretIndex = k.Typed.Length;
        return true;
    }

    private void FocusTypeAhead()
    {
        if (!TypeAhead.IsFocused) TypeAhead.Focus();
    }

    private readonly Dictionary<string, ColumnLayout> _columns = [];
    private IReadOnlyDictionary<string, List<double>> _savedColumns = new Dictionary<string, List<double>>();

    /// <summary>Raised when the operator finishes resizing a table's columns: the layout name and its fixed widths.</summary>
    public event Action<string, double[]>? ColumnsSaved;

    private OperateViewModel? Vm => DataContext as OperateViewModel;

    /// <summary>Applies column widths remembered from an earlier session, by layout name.</summary>
    public void LoadColumns(IReadOnlyDictionary<string, List<double>> saved)
    {
        _savedColumns = saved;
        foreach (var (name, layout) in _columns)
            if (saved.TryGetValue(name, out var widths)) layout.Load(widths);
    }

    /// <summary>The shared widths for a table, made from the first grid that asks (its widths are the defaults).</summary>
    internal ColumnLayout ColumnsFor(string name, Grid template)
    {
        if (_columns.TryGetValue(name, out var layout)) return layout;
        layout = new ColumnLayout([.. template.ColumnDefinitions.Select(c => c.Width.IsAbsolute ? c.Width.Value : double.NaN)]);
        if (_savedColumns.TryGetValue(name, out var widths)) layout.Load(widths);
        // Next in line and Repeats on a laptop: Chance and Where trim first, then Last worked, Signal, the rank and,
        // only as a last resort, Last heard hide, so the Call button never falls off the right edge.
        if (name == "Calls") layout.GiveWay([(8, 120), (2, 120)], [9, 7, 0, 5]);
        _columns[name] = layout;
        return layout;
    }

    internal void SaveColumns(string name)
    {
        if (_columns.TryGetValue(name, out var layout)) ColumnsSaved?.Invoke(name, layout.ToSaved());
    }

    // Double-click is read from the click count here, not from DoubleTapped: DoubleTapped needs both presses on the same
    // element, and a strip closing can move the rows under the pointer between them. A press in a strip has the details
    // as its data and goes in as no row. The second press is handled so the list does not select the row it landed on
    // in place of the one being called.
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm || !e.GetCurrentPoint(LineList).Properties.IsLeftButtonPressed) return;
        AfterPress(e.ClickCount);
        if (e.ClickCount >= 2 && OnButton(e)) return;
        vm.RowClicked((e.Source as Control)?.DataContext as StationRowViewModel, e.ClickCount, OnCall(e));
        if (e.ClickCount >= 2) e.Handled = true;
    }

    // A double-click on a button in a row (Call, a link in the strip) is the button's: it must not call the station as well.
    private static bool OnButton(PointerPressedEventArgs e) =>
        e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is not null;

    // A click on the call opens the details strip; anywhere else on the row only picks it.
    private static bool OnCall(PointerPressedEventArgs e) => e.Source is Control { Name: "RowCall" };

    // A press in a Repeats row; one in the strip under it has the details as its data and is ignored.
    private void OnRepeatPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm || !e.GetCurrentPoint(RepeatList).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Control)?.DataContext is not RepeatRowViewModel row) return;
        AfterPress(e.ClickCount);
        if (e.ClickCount >= 2 && OnButton(e)) return;
        vm.RepeatClicked(row, e.ClickCount, OnCall(e));
        if (e.ClickCount >= 2) e.Handled = true;
    }

    private DispatcherTimer? _clickTimer;

    // A single click opens or folds a details strip only once the system's double-click time has passed with no second
    // click, so a double-click (which calls) never opens one. The second click stops the wait.
    private void AfterPress(int clickCount)
    {
        if (_clickTimer is null)
        {
            _clickTimer = new DispatcherTimer();
            _clickTimer.Tick += (_, _) =>
            {
                _clickTimer.Stop();
                Vm?.CompleteClick();
            };
        }
        _clickTimer.Stop();
        if (clickCount != 1) return;
        _clickTimer.Interval = this.GetPlatformSettings()?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.FromMilliseconds(500);
        _clickTimer.Start();
    }

    private TopLevel? _window;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this);
        if (_window is not null) _window.SizeChanged += OnWindowSizeChanged;
        FollowWindowSize();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null) _window.SizeChanged -= OnWindowSizeChanged;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) => FollowWindowSize();

    // The compact layout is for small screens (owner request 2026-11-16). It follows the window, not this view, so
    // dragging the waterfall taller or a faceplate skin never flips the layout.
    private void FollowWindowSize()
    {
        if (Vm is not { } vm || _window is not { } w) return;
        vm.FitWindow(w.ClientSize.Width, w.ClientSize.Height);
    }

    // The Heard here, Your signal and Worked this session panels sit in the side column's tabs on a small window and in
    // stacked cards on a big one. One copy of each moves, so the map and its choices are never on screen twice.
    private void PlaceSidePanels(bool compact)
    {
        Move(HeardPanel, compact ? HeardScroll : StackedHeardSlot);
        Move(SignalPanel, compact ? SignalScroll : StackedSignalSlot);
        Move(SessionPanel, compact ? SessionScroll : StackedSessionSlot);

        static void Move(Control panel, Control target)
        {
            if (panel.Parent == target) return;
            switch (panel.Parent)
            {
                case ContentControl c: c.Content = null; break;
                case Decorator d: d.Child = null; break;
            }
            switch (target)
            {
                case ContentControl c: c.Content = panel; break;
                case Decorator d: d.Child = panel; break;
            }
        }
    }

    /// <summary>Width of the folded side column's rail.</summary>
    public const double RailWidth = 36;

    // Below 1100 px the right column drops under the left. On wide screens it grows so the map has room.
    // Folded (small windows only), it is a thin rail at the side (a one-line bar underneath when narrow) and the call list takes the rest.
    private void Relayout(double width)
    {
        var narrow = width < 1100;
        var wide = width >= 1600;
        var compact = Vm?.Compact == true;
        PlaceSidePanels(compact);
        var folded = compact && Vm?.SideCollapsed == true;
        var side = folded ? RailWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) : wide ? "460" : "300";
        Root.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions($"*,{side}");
        Root.RowDefinitions = narrow ? new RowDefinitions("*,Auto") : new RowDefinitions("*");
        Grid.SetColumn(RightColumn, narrow ? 0 : 1);
        Grid.SetRow(RightColumn, narrow ? 1 : 0);
        // Leave room to operate when a faceplate or status banner takes vertical space from a small window.
        RightColumn.MaxHeight = narrow ? Math.Min(320, Math.Max(100, Bounds.Height / 3)) : double.PositiveInfinity;
        // The rail reads bottom to top at the side, and left to right when it lies under the list.
        RailStack.Orientation = narrow ? Avalonia.Layout.Orientation.Horizontal : Avalonia.Layout.Orientation.Vertical;
        var turn = narrow ? null : new RotateTransform(-90);
        RailBandText.LayoutTransform = turn;
        RailSignalText.LayoutTransform = turn;
    }

    // The contact card stays pinned above the call list, which always keeps enough height to show a few rows.
    // On a big window the idle band summary is pinned under the card only while both fit whole. Otherwise it moves to
    // the top of the call list and scrolls with it, so it is never cut off inside a box with a scroll bar of its own.
    // The pinned area itself only scrolls when the contact card alone is taller than the room left.
    private bool _fitQueued;

    // Size changes are raised during the layout pass, while a panel may be arranging its children: moving the summary
    // card out of that panel then threw in StackPanel.ArrangeOverride, and layout stopped for good (the window froze
    // when a contact card appeared; owner report 2026-11-14). So the fit runs once the pass is over, once per pass.
    private void FitPinned()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _fitQueued = false;
            FitPinnedNow();
        }, DispatcherPriority.Loaded);
    }

    private void FitPinnedNow()
    {
        const double listRoom = 220;
        var calling = CallingYou.IsVisible ? CallingYou.Bounds.Height + CallingYou.Margin.Bottom : 0;
        var room = Math.Max(120, LeftPane.Bounds.Height - calling - listRoom);
        PinnedScroll.MaxHeight = room;

        // Hidden (a contact, or the compact layout): leave it where it is until it shows again and has a size to judge by.
        if (!SummaryCard.IsVisible || SummaryCard.Bounds.Height <= 0) return;
        var needed = ContactCard.Bounds.Height + PinnedStack.Spacing + SummaryCard.Bounds.Height + PinnedStack.Margin.Bottom;
        var pinned = SummaryCard.Parent == PinnedStack;
        if (needed <= room && !pinned)
        {
            ListStack.Children.Remove(SummaryCard);
            PinnedStack.Children.Add(SummaryCard);
        }
        else if (needed > room && pinned)
        {
            PinnedStack.Children.Remove(SummaryCard);
            ListStack.Children.Insert(0, SummaryCard);
        }
    }

    // On a narrow card the three summary columns are too tight (continent names collide, the log column wraps
    // word by word), so Heard from moves to a full-width row under the counts and the log.
    private void ReflowSummary(double width)
    {
        var narrow = width < 1000;
        if (narrow == (Grid.GetRow(SummaryHeardFrom) == 1)) return;
        SummaryGrid.ColumnDefinitions = new ColumnDefinitions(narrow ? "*,*" : "*,*,*");
        Grid.SetColumn(SummaryLog, narrow ? 1 : 2);
        Grid.SetColumn(SummaryHeardFrom, narrow ? 0 : 1);
        Grid.SetRow(SummaryHeardFrom, narrow ? 1 : 0);
        Grid.SetColumnSpan(SummaryHeardFrom, narrow ? 2 : 1);
        SummaryHeardFrom.Margin = narrow ? new Thickness(0, 14, 0, 0) : new Thickness(12, 0);
    }
}
