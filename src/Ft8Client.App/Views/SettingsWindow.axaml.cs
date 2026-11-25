// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Ft8Client.App.ViewModels;

namespace Ft8Client.App.Views;

/// <summary>Settings: a grouped, searchable page list on the left and the selected page on the right.</summary>
public partial class SettingsWindow : Window
{
    // The sidebar's groups, in order. Each name is a page's tab header; SettingsViewModel's page numbers follow the XAML.
    private static readonly (string Group, string[] Pages)[] Groups =
    [
        ("STATION", ["Station", "Transmitting", "Contacts", "PSK and RTTY"]),
        ("FINDING STATIONS", ["Who to call", "Hunt list", "Blocked calls", "Notifications"]),
        ("CONNECTIONS", ["Logbooks", "PSK Reporter and RBN", "Mobile connection", "Other programs", "MQTT"]),
        ("COSTAS", ["Appearance", "Updates", "Privacy", "Advanced", "Developer"]),
    ];

    private readonly List<SettingsSearchEntry> _index = [];

    /// <summary>Creates the window.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsViewModel vm) vm.Close += Close;
        };
        Closed += (_, _) => (DataContext as SettingsViewModel)?.Detach();
        Pages.SelectionChanged += (_, _) => BuildNav();
        Search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) BuildNav();
        };
        Opened += (_, _) =>
        {
            BuildIndex();
            BuildNav();
        };
        BuildNav();
    }

    private TabItem? Page(string header) =>
        Pages.Items.OfType<TabItem>().FirstOrDefault(t => string.Equals(t.Header as string, header, StringComparison.Ordinal));

    private void BuildNav()
    {
        Nav.Children.Clear();
        var query = Search.Text?.Trim() ?? string.Empty;
        if (query.Length > 0)
        {
            BuildResults(query);
            return;
        }
        foreach (var (group, pages) in Groups)
        {
            var items = pages.Select(Page).Where(t => t is { IsVisible: true }).ToList();
            if (items.Count == 0) continue;
            Nav.Children.Add(new TextBlock { Text = group, Classes = { "navgroup" } });
            foreach (var t in items) Nav.Children.Add(NavButton((string)t!.Header!, null, t, null));
        }
    }

    private void BuildResults(string query)
    {
        var hits = _index.Where(e => e.Page.IsVisible && e.Matches(query)).Take(40).ToList();
        if (hits.Count == 0)
        {
            Nav.Children.Add(new TextBlock { Text = "Nothing matches.", Classes = { "navdetail" }, Margin = new(14, 10, 0, 0) });
            return;
        }
        foreach (var h in hits) Nav.Children.Add(NavButton(h.Display(query), h.PageTitle, h.Page, h.Target));
    }

    private Button NavButton(string title, string? detail, TabItem page, Control? target)
    {
        var selected = detail is null && ReferenceEquals(Pages.SelectedItem, page);
        Control content = detail is null
            ? new TextBlock { Text = title }
            : new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new TextBlock { Text = detail, Classes = { "navdetail" } },
                },
            };
        var b = new Button { Content = content, Classes = { "nav" } };
        if (selected) b.Classes.Add("selected");
        b.Click += (_, _) => Reveal(page, target);
        return b;
    }

    // Shows a search result: its page, its logbook section opened, and the setting scrolled into view.
    private void Reveal(TabItem page, Control? target)
    {
        Pages.SelectedItem = page;
        if (target is null) return;
        foreach (var e in target.GetSelfAndLogicalAncestors().OfType<Expander>()) e.IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => target.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);
    }

    // Every setting on every page: its label, the text beside it (help, check boxes), and the control to scroll to.
    private void BuildIndex()
    {
        _index.Clear();
        foreach (var page in Pages.Items.OfType<TabItem>())
        {
            var pageTitle = page.Header as string ?? string.Empty;
            _index.Add(new SettingsSearchEntry(pageTitle, pageTitle, [], page, null));
            // A logbook section matches on its name ("eQSL") and opens when chosen.
            foreach (var section in page.GetLogicalDescendants().OfType<Expander>())
            {
                var words = SectionWords(section);
                if (words.Count > 0) _index.Add(new SettingsSearchEntry(words[0], pageTitle, words.Skip(1).ToList(), page, section));
            }
            var fields = page.GetLogicalDescendants().OfType<Grid>().Where(g => g.Classes.Contains("field")).ToList();
            foreach (var field in fields)
            {
                var texts = field.GetLogicalDescendants().Select(Text).Where(t => t.Length > 0 && !t.StartsWith('{')).ToList();
                var label = field.Children.OfType<TextBlock>().FirstOrDefault(t => t.Classes.Contains("label"))?.Text;
                if (string.IsNullOrWhiteSpace(label) || label.StartsWith('{')) label = texts.FirstOrDefault() ?? pageTitle;
                _index.Add(new SettingsSearchEntry(label, Where(pageTitle, field), texts, page, field));
            }
            // Check boxes and buttons that sit on the page outside a labelled field (eQSL, Club Log and others).
            foreach (var loose in page.GetLogicalDescendants().OfType<ContentControl>()
                         .Where(c => c is CheckBox or Button && c.Content is string && !fields.Any(f => f.IsLogicalAncestorOf(c))))
                _index.Add(new SettingsSearchEntry((string)loose.Content!, Where(pageTitle, loose), [], page, loose));
        }
    }

    // "Logbooks › LoTW" for a setting inside a logbook section, otherwise the page's name.
    private static string Where(string pageTitle, Control c) =>
        c.GetLogicalAncestors().OfType<Expander>().FirstOrDefault() is { } section && SectionWords(section) is [var name, ..]
            ? $"{pageTitle} › {name}"
            : pageTitle;

    // A section header's lines: its name first, then its description.
    private static List<string> SectionWords(Expander section) =>
        section.Header is Control h ? h.GetSelfAndLogicalDescendants().Select(Text).Where(t => t.Length > 0).ToList() : [];

    private static string Text(ILogical c) => c switch
    {
        TextBlock t => t.Text ?? string.Empty,
        ContentControl { Content: string s } => s,
        TextBox { PlaceholderText: string w } => w,
        _ => string.Empty,
    };
}
