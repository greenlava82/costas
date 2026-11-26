// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ft8Client.App.Engine;
using Ft8Client.App.ViewModels;
using Ft8Client.App.Views;
using Ft8Client.Core;
using Ft8Client.Core.Contacts;
using Ft8Client.Core.Ranking;
using Ft8Client.Data.Database;
using Ft8Client.Data.Settings;
using Ft8Client.Rig;
using Ft8Client.Rig.Flex;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

/// <summary>
/// The radio's controls (power, amp and tuner, Tune, transmit offset, Auto-select): in the contact card's Radio row on
/// Operate, in the bottom bar on every other view, one copy on screen at a time, and all of it on screen.
/// </summary>
public class RadioControlsLayoutTests
{
    // The widest the amp/tuner button gets: both devices, the longest states.
    private static readonly FlexAccessories Both = new[]
    {
        "S0|amplifier 0x18E5F0AA model=PowerGeniusXL state=TRANSMIT_A",
        "S0|amplifier 0x2C01D3B4 model=TunerGeniusXL operate=1 bypass=0 tuning=1",
    }.Aggregate(FlexAccessories.Empty, (a, l) => a.With(FlexStatusLine.Parse(l)!));

    private static readonly string[] AlwaysShown = ["TuneButton", "OffsetButton", "AutoSelectButton"];

    private static readonly ContactView InContact =
        new("DL3MIB", "JO31", ContactFlow.CalledCq, ContactOutcome.InProgress, -16, null, [], false, false, string.Empty);

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1920, 1080)]
    public Task RadioControls_OperateView_InContactCardOnlyAndOnScreen(int width, int height) =>
        RunAsync(width, height, (host, vm, window) =>
        {
            host.RadioPower.ShowForTests(new RigPower(100, 100));
            host.Accessories.ShowForTests(Both);
            vm.Tab = 0;
            Settle(window);
            vm.PowerText.Should().Be("100 W");

            InCard(RadioButtons(window), window, width);
            BarRadio(window).IsEffectivelyVisible.Should().BeFalse("the bottom bar does not repeat the contact card's controls");
            Menu(window).IsEffectivelyVisible.Should().BeFalse("the menu button is only for the folded idle line");
            OnScreen(Halt(window), window, width);
        });

    [Theory]
    [InlineData(1440, 900, Mode.Ft8)]
    [InlineData(1280, 720, Mode.Ft8)]
    [InlineData(1100, 700, Mode.Ft8)]
    [InlineData(1440, 900, Mode.Psk31)]
    [InlineData(1100, 700, Mode.Psk31)]
    public Task RadioControls_InContact_FullRowInsideContactCard(int width, int height, Mode mode) =>
        RunAsync(width, height, (host, vm, window) =>
        {
            host.RadioPower.ShowForTests(new RigPower(100, 100));
            host.Accessories.ShowForTests(Both);
            vm.Tab = 0;
            InContactOn(window, mode);

            var buttons = RadioButtons(window);
            InCard(buttons, window, width);
            Menu(window).IsEffectivelyVisible.Should().BeFalse("a contact unfolds the card and the full row shows");
            var row = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "RadioRow");
            row.Bounds.Width.Should().BeGreaterThanOrEqualTo(Converters.RadioRowMinWidth - 0.5, "PSK and RTTY have fewer buttons above, but the row keeps its room");
        });

    // Headless text is wider than real text, so the folded line is checked at the laptop size the fold was made for
    // (1280 x 720) and at a wide window that is compact by height; narrower windows are checked by eye in real fonts.
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1600, 800)]
    public Task RadioControls_FoldedIdleCard_OneMenuButtonOpensThem(int width, int height) =>
        RunAsync(width, height, (host, vm, window) =>
        {
            host.RadioPower.ShowForTests(new RigPower(50, 100));
            host.Accessories.ShowForTests(Both);
            vm.Tab = 0;
            Settle(window);
            vm.Operate.FoldIdleCard.Should().BeTrue("a small window with no contact folds the card to one line");

            Visible(window).Should().BeEmpty("the folded line has no room for the row; the menu button stands for it");
            BarRadio(window).IsEffectivelyVisible.Should().BeFalse("the bottom bar does not take them back on Operate");
            var menu = Menu(window);
            menu.IsEffectivelyVisible.Should().BeTrue();
            OnScreen(menu, window, width);
            var card = Card(window);
            var menuLeft = menu.TranslatePoint(default, window)!.Value.X;
            (menuLeft + menu.Bounds.Width).Should().BeLessThanOrEqualTo(Left(card, window) + card.Bounds.Width + 0.5, "the menu button fits on the folded line");

            menu.Flyout!.ShowAt(menu);
            Settle(window);
            var opened = RadioButtons(window);
            opened.Should().OnlyContain(b => b.FindAncestorOfType<RadioControls>()!.Name == "MenuRadio");
            foreach (var b in opened) OnScreen(b, window, width);
            menu.Flyout.Hide();
        });

    // Reach (tab 2) is left out: its refresh reads the database from snapshots still queued after the app has stopped,
    // and the next UI test on the shared UI thread would run them against a deleted database.
    [Theory]
    [InlineData(1440, 900, 1)]
    [InlineData(1440, 900, 3)]
    [InlineData(1280, 720, 1)]
    [InlineData(1280, 720, 4)]
    public Task RadioControls_OtherView_InBottomBarOnlyAndOnScreen(int width, int height, int tab) =>
        RunAsync(width, height, (host, vm, window) =>
        {
            host.RadioPower.ShowForTests(new RigPower(100, 100));
            host.Accessories.ShowForTests(Both);
            vm.Tab = tab;
            Settle(window);

            foreach (var b in RadioButtons(window))
            {
                InOperate(b).Should().BeFalse($"{Describe(b)} is in the bottom bar off Operate");
                b.FindAncestorOfType<RadioControls>()!.Name.Should().Be("BarRadio");
                OnScreen(b, window, width);
            }

            OnScreen(Halt(window), window, width);
        });

    [Fact]
    public Task RadioControls_BackToOperate_MoveBackToContactCard() =>
        RunAsync(1440, 900, (host, vm, window) =>
        {
            vm.Tab = 3;
            Settle(window);
            RadioButtons(window, expectPowerAndAccessory: false).Should().OnlyContain(b => !InOperate(b));

            vm.Tab = 0;
            Settle(window);
            RadioButtons(window, expectPowerAndAccessory: false).Should().OnlyContain(b => InOperate(b));
        });

    [Fact]
    public Task RadioControls_NoPowerOrAccessoriesReported_OnlyTuneAndOffsetShown() =>
        RunAsync(1440, 900, (host, vm, window) =>
        {
            vm.Tab = 0;
            Settle(window);

            vm.HasPower.Should().BeFalse("simulation reports no power setting");
            Visible(window).Where(b => b.Name == "PowerButton").Should().BeEmpty();
            Visible(window).Where(IsAccessory).Should().BeEmpty("nothing is paired in simulation");
            RadioButtons(window, expectPowerAndAccessory: false).Should().HaveCount(AlwaysShown.Length);
        });

    [Fact]
    public Task RadioControls_PowerReported_MenuButtonShowsWatts() =>
        RunAsync(1280, 720, (host, vm, window) =>
        {
            vm.Tab = 0;
            Settle(window);
            var menu = Menu(window);
            Texts(menu).Should().NotContain("50 W", "no power is reported yet");

            host.RadioPower.ShowForTests(new RigPower(50, 100));
            Settle(window);
            Texts(menu).Should().Contain("50 W");
        });

    [Theory]
    [InlineData(1500, true, true, "1500 Hz")]
    [InlineData(1500, false, true, "1500 Hz · busy")]
    [InlineData(820, true, false, "820 Hz · manual")]
    [InlineData(2950, false, false, "2950 Hz · manual · busy")]
    public void ShortOffset_State_NamesOnlyWhatMatters(int hz, bool clear, bool auto, string expected) =>
        MainViewModel.ShortOffset(hz, clear, auto).Should().Be(expected);

    [Theory]
    [InlineData(0, Converters.RadioRowMinWidth)]
    [InlineData(330, Converters.RadioRowMinWidth)]
    [InlineData(640, 640)]
    public void RadioRowWidth_ButtonRowWidth_NeverUnderMinimum(double buttons, double expected) =>
        Converters.RadioRowWidth.Convert(buttons, typeof(double), null, System.Globalization.CultureInfo.InvariantCulture).Should().Be(expected);

    // Puts a contact in progress on the Operate page: its own view model, so the running app's snapshots do not undo it.
    private static void InContactOn(Window window, Mode mode)
    {
        var page = window.GetVisualDescendants().OfType<OperateView>().Single();
        var operate = new OperateViewModel(DispatchProxy.Create<IOperateCommands, ResizableColumnsTests.NoCommands>(), () => DateTime.UtcNow);
        operate.Apply(new SessionSnapshot { Mode = mode, Contact = InContact, Rank = new RankResult([], [], [], []) }, [], 0);
        page.DataContext = operate;
        Settle(window);
        operate.FoldIdleCard.Should().BeFalse("a contact is in progress");
    }

    private static void InCard(IEnumerable<Button> buttons, Window window, int width)
    {
        var card = Card(window);
        foreach (var b in buttons)
        {
            InOperate(b).Should().BeTrue($"{Describe(b)} sits in the contact card on Operate");
            b.FindAncestorOfType<RadioControls>()!.Name.Should().Be("CardRadio");
            OnScreen(b, window, width);
            (Left(b, window) + b.Bounds.Width).Should().BeLessThanOrEqualTo(Left(card, window) + card.Bounds.Width + 0.5, $"{Describe(b)} fits inside the contact card at {width} px");
        }
    }

    private static Border Card(Window window) => window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ContactCard");

    private static RadioControls BarRadio(Window window) => window.GetVisualDescendants().OfType<RadioControls>().Single(r => r.Name == "BarRadio");

    private static Button Menu(Window window) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RadioMenu");

    private static double Left(Visual v, Window window) => v.TranslatePoint(default, window)!.Value.X;

    private static IEnumerable<string?> Texts(Visual v) =>
        v.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text);

    // Every radio control visible now, checking each is shown exactly once.
    private static List<Button> RadioButtons(Window window, bool expectPowerAndAccessory = true)
    {
        var visible = Visible(window).ToList();
        var names = expectPowerAndAccessory ? AlwaysShown.Append("PowerButton") : AlwaysShown;
        var found = new List<Button>();
        foreach (var name in names)
        {
            var copies = visible.Where(b => b.Name == name).ToList();
            copies.Should().ContainSingle($"{name} is on screen once");
            found.Add(copies[0]);
        }

        if (expectPowerAndAccessory)
        {
            var amp = visible.Where(IsAccessory).ToList();
            amp.Should().ContainSingle("the amp and tuner button is on screen once");
            found.Add(amp[0]);
        }

        return found;
    }

    private static IEnumerable<Button> Visible(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.FindAncestorOfType<RadioControls>() is not null);

    private static bool IsAccessory(Button b) => AutomationProperties.GetName(b)?.StartsWith("Amplifier and tuner", StringComparison.Ordinal) == true;

    private static bool InOperate(Button b) => b.FindAncestorOfType<OperateView>() is not null;

    private static string Describe(Button b) => b.Name ?? AutomationProperties.GetName(b) ?? "button";

    private static Button Halt(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Halt transmit");

    private static void OnScreen(Button b, Window window, int width)
    {
        var left = b.TranslatePoint(default, window)!.Value.X;
        left.Should().BeGreaterThanOrEqualTo(0, $"{Describe(b)} starts on screen at {width} px");
        (left + b.Bounds.Width).Should().BeLessThanOrEqualTo(width + 0.5, $"{Describe(b)} ends on screen at {width} px");
        b.Bounds.Width.Should().BeGreaterThan(0, $"{Describe(b)} has been laid out");
    }

    private static async Task RunAsync(int width, int height, Action<AppHost, MainViewModel, MainWindow> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-radio-row-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var settings = new AppSettings { SetupComplete = true };
        settings.Updates.CheckEnabled = false;
        settings.PskReporter.FeedEnabled = false;
        settings.Usage.Enabled = false;
        settings.Dxpeditions.Enabled = false;
        settings.Window.Width = width;
        settings.Window.Height = height;
        new SettingsStore(Path.Combine(dir, "settings.json")).Save(settings);
        SqliteDatabase? database = null;
        try
        {
            await using var host = await AppHost.StartAsync(TestHost.Args(dir));
            database = host.Database;
            var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RadioControlsLayoutTests).Assembly);
            await session.DispatchThenStop(host, () =>
            {
                var vm = new MainViewModel(host);
                var window = new MainWindow { DataContext = vm, Width = width, Height = height };
                window.Show();
                try
                {
                    Settle(window);
                    test(host, vm, window);
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

    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
