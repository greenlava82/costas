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
using Avalonia.LogicalTree;
using Ft8Client.App.ViewModels;
using Ft8Client.App.Views;
using Ft8Client.Data.Database;
using Ft8Client.Data.Settings;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

public class SettingsWindowTests
{
    [Fact]
    public void Matches_EveryWordSomewhere_True()
    {
        var e = new SettingsSearchEntry("Sync", "Logbooks", ["Read my logbook and upload new contacts", "minutes"], new TabItem(), null);
        e.Matches("upload logbooks").Should().BeTrue("one word is in the text, the other is the page");
        e.Matches("UPLOAD").Should().BeTrue();
        e.Matches("upload tqsl").Should().BeFalse();
    }

    [Fact]
    public void Display_LabelDoesNotMatch_ShowsTheMatchingLine()
    {
        var e = new SettingsSearchEntry("Sync", "Logbooks", ["Read my logbook and upload new contacts", "minutes"], new TabItem(), null);
        e.Display("upload").Should().Be("Read my logbook and upload new contacts");
        e.Display("sync").Should().Be("Sync");
    }

    [Fact]
    public async Task SettingsWindow_SidebarAndSearch_ReachEveryPageAndOpenLogbookSections()
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var settings = new AppSettings { SetupComplete = true };
        settings.Updates.CheckEnabled = false;
        settings.PskReporter.FeedEnabled = false;
        settings.Usage.Enabled = false;
        settings.Dxpeditions.Enabled = false;
        new SettingsStore(Path.Combine(dir, "settings.json")).Save(settings);
        SqliteDatabase? database = null;
        try
        {
            await using var host = await AppHost.StartAsync(TestHost.Args(dir));
            database = host.Database;
            var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SettingsWindowTests).Assembly);
            await session.Dispatch(() =>
            {
                var vm = new SettingsViewModel(host);
                var window = new SettingsWindow { DataContext = vm };
                window.Show();
                try
                {
                    var pages = window.FindControl<TabControl>("Pages")!;
                    var nav = window.FindControl<StackPanel>("Nav")!;
                    var search = window.FindControl<TextBox>("Search")!;
                    var headers = pages.Items.OfType<TabItem>().Select(t => (string)t.Header!).ToList();

                    // The page numbers the view model jumps to name the right pages.
                    headers[SettingsViewModel.ContactsPage].Should().Be("Contacts");
                    headers[SettingsViewModel.KeyboardPage].Should().Be("PSK and RTTY");
                    headers[SettingsViewModel.WhoToCallPage].Should().Be("Who to call");
                    headers[SettingsViewModel.MobilePage].Should().Be("Mobile connection");

                    // Every visible page is in the sidebar once, under a group heading.
                    var visible = pages.Items.OfType<TabItem>().Where(t => t.IsVisible).Select(t => (string)t.Header!);
                    NavTitles(nav).Should().BeEquivalentTo(visible);
                    nav.Children.OfType<TextBlock>().Select(t => t.Text).Should().Contain("CONNECTIONS");

                    // Searching finds a setting inside a closed logbook section, and choosing it opens that section.
                    search.Text = "tqsl";
                    var hit = nav.Children.OfType<Button>().Should().NotBeEmpty().And.Subject.First();
                    hit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((TabItem)pages.SelectedItem!).Header.Should().Be("Logbooks");
                    var lotw = ((TabItem)pages.SelectedItem!).GetLogicalDescendants().OfType<Expander>()
                        .Single(e => e.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "LoTW"));
                    lotw.IsExpanded.Should().BeTrue();

                    search.Text = "zzz no such setting";
                    nav.Children.OfType<Button>().Should().BeEmpty();
                    search.Text = string.Empty;
                    NavTitles(nav).Should().Contain("Logbooks");
                }
                finally
                {
                    window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            if (database is not null)
            {
                using var connection = database.Open();
                SqliteConnection.ClearPool(connection);
            }
            AppearanceTests.DeleteWhenSettled(dir);
        }
    }

    private static IEnumerable<string?> NavTitles(StackPanel nav) =>
        nav.Children.OfType<Button>().Select(b => (b.Content as TextBlock)?.Text);
}
