// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Diagnostics;
using System.Globalization;
using Ft8Client.Core;
using Ft8Client.Core.Decoding;
using Ft8Client.Core.Processes;
using Ft8Client.Core.Wspr;

namespace Ft8Client.Decoding;

/// <summary>
/// Runs <c>wsprd</c> once per WSPR slot. Each run gets its own scratch folder; a run that exceeds its timeout is killed
/// and reported, never awaited forever. It is not an <see cref="IDecoder"/>: that interface is shaped for the FT modes'
/// <see cref="DecodeContext"/>, and WSPR needs only the audio and the dial frequency (docs/10-wspr-build.md, W1).
/// </summary>
/// <remarks>
/// <c>wsprd</c> keeps its call hash table (<c>hashtable.txt</c>) and FFT plans (<c>wspr_wisdom.dat</c>) in its data
/// folder. Both are kept in the temp root and copied into each new scratch folder, so a type 3 message whose call was
/// heard earlier is resolved, and a run does not plan from scratch.
/// </remarks>
public sealed class WsprdDecoder
{
    private static readonly string[] Kept = ["hashtable.txt", "wspr_wisdom.dat"];
    private readonly WsprdOptions _options;
    private readonly IProcessGuard _guard;

    /// <summary>Creates a decoder host.</summary>
    public WsprdDecoder(WsprdOptions options, IProcessGuard? guard = null)
    {
        _options = options;
        _guard = guard ?? NullProcessGuard.Instance;
        Directory.CreateDirectory(options.TempRoot);
    }

    /// <summary>Deletes scratch folders left by earlier runs.</summary>
    public static void CleanTempRoot(string tempRoot)
    {
        if (!Directory.Exists(tempRoot)) return;
        foreach (var dir in Directory.EnumerateDirectories(tempRoot, "wsprd-*"))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Builds the argument list for a run: the data folder, the dial frequency in MHz, and the WAV.</summary>
    public static IReadOnlyList<string> BuildArguments(long dialHz, string workDir, string wavName) =>
        ["-a", workDir, "-f", (dialHz / 1_000_000.0).ToString("0.000000", CultureInfo.InvariantCulture), wavName];

    /// <summary>The WAV file name <c>wsprd</c> is given: <c>YYMMDD_HHMM.wav</c>, slot start in UTC.</summary>
    public static string WavName(DateTime slotStartUtc) => slotStartUtc.ToString("yyMMdd_HHmm", CultureInfo.InvariantCulture) + ".wav";

    /// <summary>Decodes one slot recorded at <paramref name="dialHz"/>.</summary>
    public async Task<WsprDecodeResult> DecodeAsync(SlotAudio slot, long dialHz, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slot.Mode != Mode.Wspr) throw new ArgumentOutOfRangeException(nameof(slot), slot.Mode, "wsprd decodes WSPR slots only.");
        var sw = Stopwatch.StartNew();
        var workDir = Path.Combine(_options.TempRoot, "wsprd-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(workDir);
        try
        {
            var samples = new short[WsprTiming.RecordSamples];
            Array.Copy(slot.Samples12k, samples, Math.Min(samples.Length, slot.Samples12k.Length));
            var wavName = WavName(slot.SlotStartUtc);
            WavFile.Write(Path.Combine(workDir, wavName), samples, ModeInfo.DecoderSampleRate);
            Restore(workDir);

            var psi = new ProcessStartInfo(_options.ExecutablePath)
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in BuildArguments(dialHz, workDir, wavName)) psi.ArgumentList.Add(a);

            Process? process;
            try
            {
                process = Process.Start(psi);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                return new WsprDecodeResult([], 0, sw.Elapsed, DecodeStatus.Failed, $"Could not start wsprd: {ex.Message}");
            }
            if (process is null) return new WsprDecodeResult([], 0, sw.Elapsed, DecodeStatus.Failed, "Could not start wsprd.");

            using (process)
            {
                _guard.Adopt(process);
                var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
                var timeout = _options.Timeout ?? WsprdOptions.DefaultTimeout;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                try
                {
                    await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Jt9Decoder.Kill(process);
                    var status = ct.IsCancellationRequested ? DecodeStatus.Cancelled : DecodeStatus.TimedOut;
                    var msg = status == DecodeStatus.TimedOut
                        ? $"wsprd exceeded its {timeout.TotalSeconds:0.#} s limit and was stopped."
                        : "Decode cancelled.";
                    return new WsprDecodeResult([], 0, sw.Elapsed, status, msg);
                }

                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                var (spots, skipped) = Parse(stdout, slot.SlotStartUtc, dialHz);
                if (process.ExitCode != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
                    if (detail.Length > 300) detail = detail[..300];
                    return new WsprDecodeResult(spots, skipped, sw.Elapsed, DecodeStatus.Failed, $"wsprd exited with code {process.ExitCode}. {detail}".Trim());
                }
                Keep(workDir);
                return new WsprDecodeResult(spots, skipped, sw.Elapsed, DecodeStatus.Ok);
            }
        }
        finally
        {
            Jt9Decoder.TryDelete(workDir);
        }
    }

    /// <summary>Reads <c>wsprd</c>'s standard output: the spots, and how many non-blank lines were not decodes.</summary>
    public static (IReadOnlyList<WsprSpot> Spots, int Skipped) Parse(string stdout, DateTime slotStartUtc, long dialHz)
    {
        var spots = new List<WsprSpot>();
        var skipped = 0;
        foreach (var line in stdout.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || t == WsprSpotParser.FinishedMarker) continue;
            if (WsprSpotParser.TryParse(t, slotStartUtc, dialHz, out var s)) spots.Add(s);
            else skipped++;
        }
        return (spots, skipped);
    }

    private void Restore(string workDir)
    {
        foreach (var name in Kept)
        {
            var kept = Path.Combine(_options.TempRoot, name);
            try
            {
                if (File.Exists(kept)) File.Copy(kept, Path.Combine(workDir, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // wsprd starts without it
        }
    }

    // Replaced in one move, never written in place, so a run that starts as another ends reads a whole file.
    private void Keep(string workDir)
    {
        foreach (var name in Kept)
        {
            var made = Path.Combine(workDir, name);
            var kept = Path.Combine(_options.TempRoot, name);
            try
            {
                if (!File.Exists(made)) continue;
                var staged = Path.Combine(_options.TempRoot, $"keep-{Guid.NewGuid():N}.tmp");
                File.Copy(made, staged);
                try { File.Move(staged, kept, overwrite: true); }
                finally { if (File.Exists(staged)) File.Delete(staged); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // kept by a later run
        }
    }
}
