// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Ft8Client.App.Engine;
using Ft8Client.App.ViewModels;
using Ft8Client.App.Views;
using Ft8Client.Core;
using Ft8Client.Core.Contacts;

namespace Ft8Client.App.Tests;

/// <summary>
/// The band summary card moves between the pinned area and the scrolling list as room changes. The move must not happen
/// inside a layout pass: the panel being arranged lost a child mid-loop, layout threw and stopped, and the window froze
/// (owner report 2026-11-14).
/// </summary>
public class PinnedCardLayoutTests
{
    private static readonly ContactView InContact =
        new("K1ABC", "FN42", ContactFlow.CalledCq, ContactOutcome.InProgress, null, null, [], false, false, string.Empty);

    // 640 px high: the summary card fits pinned when wide, and not when a narrow window makes it taller, so it moves
    // while its own panel is arranging it.
    [Theory]
    [InlineData(600)]
    [InlineData(640)]
    [InlineData(720)]
    public async Task OperateView_SummaryCardOutgrowsPinnedRoom_MovesWithoutBreakingLayout(int height)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(PinnedCardLayoutTests).Assembly);
        await session.Dispatch(() =>
        {
            var vm = new OperateViewModel(DispatchProxy.Create<IOperateCommands, ResizableColumnsTests.NoCommands>(), () => DateTime.UtcNow);
            var idle = new SessionSnapshot { Mode = Mode.Ft8 };
            vm.Apply(idle, [], 0);
            var view = new OperateView { DataContext = vm };
            var window = new Window { Width = 1400, Height = height, Content = view };
            var errors = new List<Exception>();
            void Catch(object? s, DispatcherUnhandledExceptionEventArgs e)
            {
                errors.Add(e.Exception);
                e.Handled = true;
            }
            Dispatcher.UIThread.UnhandledException += Catch;
            window.Show();
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    vm.Apply(idle with { Contact = InContact }, [], 0);
                    Settle(window);
                    vm.Apply(idle, [], 0);
                    Settle(window);
                    window.Width = window.Width == 1400 ? 1000 : 1400;
                    Settle(window);
                }

                errors.Should().BeEmpty();
                var summary = view.FindControl<Border>("SummaryCard")!;
                (summary.Parent as Control)?.Name.Should().BeOneOf("PinnedStack", "ListStack");
                view.IsArrangeValid.Should().BeTrue("layout still runs after the card moved");
            }
            finally
            {
                Dispatcher.UIThread.UnhandledException -= Catch;
                window.Close();
            }
        }, CancellationToken.None);
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
