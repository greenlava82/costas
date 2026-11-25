// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
using Ft8Client.App.Engine;
using Ft8Client.App.Services;
using Ft8Client.Audio.Backends;
using Ft8Client.Audio.Timing;
using Ft8Client.Core;
using Ft8Client.Core.Time;
using Ft8Client.Data.Journal;
using Ft8Client.Data.Reference;
using Ft8Client.Data.Settings;
using Ft8Client.Rig;

namespace Ft8Client.App.Tests;

/// <summary>
/// The decode journal, its ALL.TXT copy and saved slot audio, written by <see cref="RecordingService"/> from a running
/// session: what is heard, what is sent, and a folder that cannot be written.
/// </summary>
public sealed class RecordingServiceTests : IDisposable
{
    private static readonly DateTime CqSlot = new(2026, 11, 24, 2, 54, 0, DateTimeKind.Utc);
    private static readonly DateTime MyFirstSlot = CqSlot.AddSeconds(15);
    private readonly string _dir = Directory.CreateTempSubdirectory("costas-recording-").FullName;

    private sealed class Station
    {
        public readonly ManualClock Clock = new(CqSlot.AddSeconds(-1));
        public readonly ScriptedDecoder Decoder = new();
        public readonly Transmitter Tx;
        public readonly Session Session;
        public readonly SlotClock Slots;
        public readonly RecordingService Recording;
        public readonly string Journal;
        public readonly string Wav;

        public Station(string dir, Action<FilesOptions> files)
        {
            var freqs = ReferenceData.LoadFrequencies();
            Tx = new Transmitter(new SimulatedRig(Clock), new NullAudioOutput { Now = () => Clock.UtcNow }, Clock, freqs);
            Session = new Session(new SessionConfig { MyCall = "W7LIT", MyGrid = "DN40", FixedTxOffsetHz = 1080 }, Decoder, new SilentSource(), Tx, Clock,
                ReferenceData.LoadCountries(), freqs);
            var s = new AppSettings();
            files(s.Files);
            var settings = new AppSettingsAccessor(new SettingsStore(Path.Combine(dir, "settings.json")), s);
            Journal = Path.Combine(dir, "journal");
            Wav = Path.Combine(dir, "wav");
            Recording = new RecordingService(Session, settings, Journal, Wav, Clock);
            Recording.Start();
            Slots = new SlotClock(Clock, Mode.Ft8);
            Slots.Event += Session.OnSlotEvent;
            Slots.Poll();
        }

        public async Task RunUntilAsync(DateTime end)
        {
            while (Clock.UtcNow < end)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(1));
                Slots.Poll();
                Tx.Poll();
                for (var i = 0; Decoder.Calls < Session.DecodesStarted; i++)
                {
                    if (i > 10_000) throw new InvalidOperationException("A decode never reached the decoder.");
                    await Task.Delay(1, TestContext.Current.CancellationToken);
                }
                Decoder.ReleaseDue(Clock.UtcNow);
                for (var i = 0; Session.DecodesApplied < Decoder.Released; i++)
                {
                    if (i > 10_000) throw new InvalidOperationException("A decode was never applied.");
                    await Task.Delay(1, TestContext.Current.CancellationToken);
                }
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // The journal's listeners run after the decode is applied, on the same thread; wait for the file rather than assume.
    private static async Task<string[]> LinesAsync(string path, int atLeast)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            if (File.Exists(path))
            {
                var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
                if (lines.Length >= atLeast) return lines;
            }
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException($"{path} did not get {atLeast} lines within 10 s.");
    }

    [Fact]
    public async Task SlotDecoded_AllTxtOn_JournalAndAllTxtGetTheSameWsjtxLines()
    {
        var allTxt = Path.Combine(_dir, "alltxt");
        var st = new Station(_dir, f =>
        {
            f.AllTxt = true;
            f.AllTxtFolder = allTxt;
        });
        st.Decoder.Script(CqSlot, TimeSpan.FromSeconds(0.5), "CQ N8WCR EN81", "K1ABC W9XYZ -07");

        await st.RunUntilAsync(CqSlot.AddSeconds(15));

        var journal = await LinesAsync(new DecodeJournal(st.Journal).PathFor(CqSlot), 2);
        var copy = await LinesAsync(new AllTxtWriter(allTxt).PathFor(CqSlot), 2);
        copy.Should().Equal(journal, "ALL.TXT is a copy of the journal");
        journal[0].Should().Be(DecodeJournal.Format(CqSlot, 14_074_000, false, "FT8", -9, 0.1, 1264, "CQ N8WCR EN81"));
        Path.GetDirectoryName(new AllTxtWriter(allTxt).PathFor(CqSlot)).Should().EndWith("2026-11", "one folder per month, as WSJT-X tools expect");
        Directory.Exists(st.Wav).Should().BeFalse("saving slot audio is off by default");
    }

    [Fact]
    public async Task SlotDecoded_AllTxtOffAndSaveDecoded_NoAllTxtAndTheSlotSavedAsWav()
    {
        var allTxt = Path.Combine(_dir, "alltxt");
        var st = new Station(_dir, f =>
        {
            f.AllTxtFolder = allTxt;
            f.SaveWav = "decoded";
        });
        st.Decoder.Script(CqSlot, TimeSpan.FromSeconds(0.5), "CQ N8WCR EN81");

        await st.RunUntilAsync(CqSlot.AddSeconds(15));

        await LinesAsync(new DecodeJournal(st.Journal).PathFor(CqSlot), 1);
        Directory.Exists(allTxt).Should().BeFalse();
        Directory.GetFiles(st.Wav, "*.wav").Select(Path.GetFileName).Should().Equal("261124_025400.wav");
    }

    [Fact]
    public async Task Transmitting_CallStation_TransmittedMessageJournaled()
    {
        var allTxt = Path.Combine(_dir, "alltxt");
        var st = new Station(_dir, f =>
        {
            f.AllTxt = true;
            f.AllTxtFolder = allTxt;
        });
        st.Decoder.Script(CqSlot, TimeSpan.FromSeconds(0.5), "CQ N8WCR EN81");
        await st.RunUntilAsync(CqSlot.AddSeconds(14.5));
        st.Session.CallStation("N8WCR").Should().BeTrue();

        await st.RunUntilAsync(MyFirstSlot.AddSeconds(2));

        var expected = DecodeJournal.Format(MyFirstSlot, 14_074_000, true, "FT8", 0, 0, 1080, "N8WCR W7LIT DN40");
        (await LinesAsync(new DecodeJournal(st.Journal).PathFor(MyFirstSlot), 2)).Should().Contain(expected);
        (await LinesAsync(new AllTxtWriter(allTxt).PathFor(MyFirstSlot), 2)).Should().Contain(expected);
    }

    [Fact]
    public async Task SlotDecoded_AllTxtFolderCannotBeWritten_JournalStillWrittenAndNothingThrown()
    {
        // A file where the folder should be: every ALL.TXT write fails.
        var blocked = Path.Combine(_dir, "blocked");
        await File.WriteAllTextAsync(blocked, "not a folder", TestContext.Current.CancellationToken);
        var st = new Station(_dir, f =>
        {
            f.AllTxt = true;
            f.AllTxtFolder = blocked;
        });
        st.Decoder.Script(CqSlot, TimeSpan.FromSeconds(0.5), "CQ N8WCR EN81");
        st.Decoder.Script(CqSlot.AddSeconds(30), TimeSpan.FromSeconds(0.5), "CQ N8WCR EN81");

        await st.RunUntilAsync(CqSlot.AddSeconds(45));

        (await LinesAsync(new DecodeJournal(st.Journal).PathFor(CqSlot), 2)).Should().HaveCount(2);
        File.ReadAllText(blocked).Should().Be("not a folder");
    }
}
