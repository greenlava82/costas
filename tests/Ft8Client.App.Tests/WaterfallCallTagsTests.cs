// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Ft8Client.App.Views;

namespace Ft8Client.App.Tests;

public class WaterfallCallTagsTests
{
    private static readonly WaterfallTag K5we = new(1525, "K5WE", WaterfallTagKind.Normal);
    private static readonly WaterfallTag N8pfk = new(625, "N8PFK", WaterfallTagKind.NewCountry);

    private static float[] Row() => Enumerable.Repeat(-90f, 512).ToArray();

    private static Task Ui(Action test) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(WaterfallCallTagsTests).Assembly).Dispatch(test, CancellationToken.None);

    [Fact]
    public async Task AddCallTags_BeforeAnyAudio_Ignored()
    {
        await Ui(() =>
        {
            var view = new WaterfallView();
            view.AddCallTags([K5we]);
            view.Update(Row(), 1500);

            view.CallTagRows(10).Should().ContainSingle().Which.Should().BeEmpty("there was no row to put the tag on");
        });
    }

    [Fact]
    public async Task AddCallTags_MoreRowsArrive_TagsScrollDownWithTheirRow()
    {
        await Ui(() =>
        {
            var view = new WaterfallView();
            view.Update(Row(), 1500);
            view.AddCallTags([K5we]);
            for (var i = 0; i < 3; i++) view.Update(Row(), 1500);

            var rows = view.CallTagRows(10);
            rows.Should().HaveCount(4);
            rows[3].Should().Equal(K5we);
            rows.Take(3).Should().OnlyContain(r => r.Count == 0);
        });
    }

    [Fact]
    public async Task AddCallTags_NoNewAudio_SameRowCollectsBoth()
    {
        // While transmitting no rows arrive; a late full decode then adds to the row the early pass used.
        await Ui(() =>
        {
            var view = new WaterfallView();
            view.Update(Row(), 1500);
            view.AddCallTags([K5we]);
            view.Update(null, 1500);
            view.AddCallTags([N8pfk]);

            view.CallTagRows(10).Should().ContainSingle().Which.Should().Equal(K5we, N8pfk);
        });
    }

    [Fact]
    public async Task AddCallTags_HistoryWrapsRound_OldTagsDropped()
    {
        await Ui(() =>
        {
            var view = new WaterfallView();
            view.Update(Row(), 1500);
            view.AddCallTags([K5we]);
            for (var i = 0; i < WaterfallView.MaxRows; i++) view.Update(Row(), 1500);

            view.CallTagRows(WaterfallView.MaxRows).Should().OnlyContain(r => r.Count == 0, "the tagged row has scrolled out of the history");
        });
    }

    [Fact]
    public async Task AddCallTags_EmptyList_LeavesRowUntagged()
    {
        await Ui(() =>
        {
            var view = new WaterfallView();
            view.Update(Row(), 1500);
            view.AddCallTags([]);

            view.CallTagRows(1).Single().Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Clear_BandChange_RemovesTags()
    {
        await Ui(() =>
        {
            var view = new WaterfallView();
            view.Update(Row(), 1500);
            view.AddCallTags([K5we]);
            view.Clear();
            view.Update(Row(), 1500);

            view.CallTagRows(10).Should().ContainSingle().Which.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Update_AudioWidthChanges_RemovesTags()
    {
        await Ui(() =>
        {
            var view = new WaterfallView();
            view.Update(Row(), 1500);
            view.AddCallTags([K5we]);
            view.Update(new float[256], 1500);

            view.CallTagRows(10).Should().ContainSingle().Which.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task CopyFrom_PopOut_KeepsTagsAndTheSwitch()
    {
        await Ui(() =>
        {
            var strip = new WaterfallView { ShowCallTags = false };
            strip.Update(Row(), 1500);
            strip.AddCallTags([K5we]);
            strip.Update(Row(), 1500);

            var window = new WaterfallView();
            window.CopyFrom(strip);

            window.CallTagRows(10).Should().HaveCount(2);
            window.CallTagRows(10)[1].Should().Equal(K5we);
            window.ShowCallTags.Should().BeFalse();
            // The copy is independent: new tags on one do not appear on the other.
            window.AddCallTags([N8pfk]);
            strip.CallTagRows(10)[0].Should().BeEmpty();
        });
    }

    [Fact]
    public async Task ShowCallTags_SwitchedOff_KeepsTagsForSwitchingBackOn()
    {
        await Ui(() =>
        {
            var view = new WaterfallView();
            view.Update(Row(), 1500);
            view.ShowCallTags = false;
            view.AddCallTags([K5we]);
            view.ShowCallTags = true;

            view.CallTagRows(1).Single().Should().Equal(K5we);
        });
    }

    [Theory]
    [InlineData(1000, 52, true)]
    [InlineData(1000, 400, true)]
    [InlineData(1000, 400, false)]
    [InlineData(300, 120, true)]
    [InlineData(1000, 21, true)]
    public async Task Render_ManyTagsAnySize_DrawsWithoutError(int width, int height, bool show)
    {
        await Ui(() =>
        {
            var view = new WaterfallView { ShowCallTags = show };
            for (var slot = 0; slot < 6; slot++)
            {
                for (var i = 0; i < 75; i++) view.Update(Row(), 1500, slotStart: i == 0);
                view.AddCallTags([.. Enumerable.Range(0, 40).Select(n => new WaterfallTag(200 + n * 65, $"K{n}ABC", (WaterfallTagKind)(n % 5)))]);
            }
            view.SetTags([(1200, "?", false)]);
            view.Measure(new Size(width, height));
            view.Arrange(new Rect(0, 0, width, height));
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));

            var render = () => bitmap.Render(view);

            render.Should().NotThrow();
        });
    }
}
