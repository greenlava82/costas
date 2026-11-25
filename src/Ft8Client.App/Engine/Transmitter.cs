// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using Ft8Client.Audio.Backends;
using Ft8Client.Audio.Timing;
using Ft8Client.Core;
using Ft8Client.Core.Bands;
using Ft8Client.Core.Contacts;
using Ft8Client.Core.Encoding;
using Ft8Client.Core.Time;
using Ft8Client.Core.Transmit;
using Ft8Client.Rig;

namespace Ft8Client.App.Engine;

/// <summary>
/// Keys the radio and plays pre-rendered audio on the slot clock: PTT at the PttOn mark (Tx start minus the lead),
/// audio at the TxStart mark, PTT off a 100 ms tail after the audio ends. Any rig or audio fault during a
/// transmission stops the audio and releases PTT first, then reports. Guards (encoding, band edge, clock, devices)
/// refuse a transmission before it starts. Audio waits for the radio's answer to the PTT command, so a slow answer
/// starts the message a little late instead of clipping its start. Slot-thread entry points never wait on I/O.
/// </summary>
public sealed class Transmitter
{
    /// <summary>PTT tail after the audio ends.</summary>
    public static readonly TimeSpan Tail = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How long the radio is given to answer the PTT command before the transmission is given up. Audio never starts
    /// before the answer: a command that queued behind a radio poll would otherwise put the first symbols into a radio
    /// that has not keyed yet.
    /// </summary>
    public static readonly TimeSpan PttConfirmLimit = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly IClock _clock;
    private readonly FrequencyTable _frequencies;
    private IRig _rig;
    private IAudioOutput? _output;
    private volatile string? _outputFault;
    private PreparedTx? _next;
    private PreparedTx? _current;
    private DateTime _audioStartedUtc;
    private DateTime? _pttOffDueUtc;
    private DateTime? _lateAudioDueUtc;
    private DateTime? _lateAlignUtc;
    private DateTime? _answerWaitMarkUtc; // the Tx start mark of a message waiting for the radio's PTT answer
    private TxHold? _hold;
    private DateTime? _lastStartedSlot;
    private bool _ptt;
    private long _haltEpoch; // incremented by every Halt; keying planned before a Halt is refused after it
    private Task<string?>? _pttOn; // the latest PTT-on command; audio waits for its answer
    private DateTime _pttOnAtUtc;
    private AudioStream? _stream;
    private DateTime? _streamAudioDueUtc;
    private bool _streamPlaying;
    private Mode _streamMode;
    private int _streamOffsetHz;
    private int? _streamWidthHz;

    /// <summary>Creates a transmitter.</summary>
    public Transmitter(IRig rig, IAudioOutput? output, IClock clock, FrequencyTable frequencies)
    {
        _rig = rig;
        _output = output;
        _clock = clock;
        _frequencies = frequencies;
        _rig.Faulted += OnRigFault;
        if (_output is not null) _output.Faulted += OnAudioFault;
    }

    /// <summary>Raised when audio starts (plan, start time).</summary>
    public event Action<PreparedTx, DateTime>? Started;

    /// <summary>Raised when a Tune or test tone starts.</summary>
    public event Action<PreparedTx>? ToneStarted;

    /// <summary>Raised when a transmission ends; true if it completed, false if it was cut short.</summary>
    public event Action<PreparedTx, bool>? Ended;

    /// <summary>Raised when a keyboard-mode over's audio starts.</summary>
    public event Action? StreamStarted;

    /// <summary>Raised when a keyboard-mode over ends; true if it played to its end, false if it was cut short.</summary>
    public event Action<bool>? StreamEnded;

    /// <summary>Raised with a fault message after PTT has been released.</summary>
    public event Action<string>? Fault;

    /// <summary>
    /// A transmission was committed: an FT8/FT4 message queued for a slot, a keyboard over or Tune about to key. Raised
    /// before PTT, so MQTT subscribers get warning of RF. Handlers must return at once.
    /// </summary>
    public event Action? Committed;

    /// <summary>
    /// A PTT edge: true just before PTT is asserted, false once it has been released. Every key and unkey Costas makes
    /// passes through here (FT8/FT4, PSK, RTTY, Tune). Handlers must return at once.
    /// </summary>
    public event Action<bool>? PttChanged;

    /// <summary>
    /// Raised when a transmission or tune is refused by a guard, with the kind of reason: disabled, no_audio, output_dead,
    /// output_muted, radio_fault, clock, band_edge or message. For the usage statistics; the text still goes to the caller.
    /// </summary>
    public event Action<string>? Refused;

    /// <summary>Transmit gain in dBFS.</summary>
    public double GainDb { get; set; } = -6.0;

    /// <summary>
    /// Why this build does not transmit at all, or null. Every transmission, Tune and tuner start is refused with it
    /// before PTT is touched.
    /// </summary>
    public string? DisabledReason { get; set; }

    /// <summary>Why the audio output stopped working, or null. Cleared when a new output is set.</summary>
    public string? OutputFault => _outputFault;

    /// <summary>A start later than this after its mark is skipped.</summary>
    public TimeSpan LateStartLimit { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>True while PTT is asserted or audio is playing.</summary>
    public bool Transmitting
    {
        get { lock (_gate) return _ptt || _current is not null || _stream is not null; }
    }

    /// <summary>The output's sample rate, for rendering audio ahead (48 kHz when no output is open).</summary>
    public int OutputSampleRate => _output?.SampleRate ?? 48_000;

    /// <summary>
    /// Starts a keyboard-mode over (docs/09-keyboard-modes.md section 9): PTT now, the stream's audio after the PTT lead,
    /// and PTT off a tail after the stream finishes. Returns null once started, or why it was refused: transmitting is
    /// disabled, no audio output, a radio fault, outside the band, already transmitting, or PTT could not be asserted.
    /// The clock check does not apply, since these modes have no slots. Rig and audio faults, and Halt, end it like any
    /// transmission. <paramref name="widthHz"/> is the signal's width for the band-edge check (an RTTY shift wider than
    /// the standard), or null for the mode's usual width.
    /// </summary>
    public string? StartStream(AudioStream stream, Mode mode, int offsetHz, TxGuardInput guard, TimeSpan pttLead, int? widthHz = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(guard);
        if (DisabledReason is not null) return Refuse("disabled", DisabledReason);
        if (!guard.AudioReady || _output is null) return Refuse("no_audio", "No audio output device is open; transmitting is disabled.");
        if (SilencedText() is { } silent) return Refuse("output_muted", silent);
        if (guard.RigFault is not null) return Refuse("radio_fault", $"Radio fault: {guard.RigFault}");
        if (!_frequencies.TransmitAllowed(guard.Band, mode, guard.DialHz, offsetHz, widthHz, out var bandWhy)) return Refuse("band_edge", bandWhy);
        long epoch;
        lock (_gate)
        {
            if (_ptt || _current is not null || _next is not null || _stream is not null) return "Already transmitting.";
            epoch = _haltEpoch;
            _stream = stream;
            _streamPlaying = false;
            _streamMode = mode;
            _streamOffsetHz = offsetHz;
            _streamWidthHz = widthHz;
            _ptt = true;
            _streamAudioDueUtc = _clock.UtcNow + pttLead;
        }
        Committed?.Invoke();
        var ptt = KeyOn(epoch);
        // A radio that refuses PTT at once has already halted the over: say why instead of reporting it started.
        if (ptt.IsCompletedSuccessfully && ptt.Result is { } failed) return failed;
        lock (_gate)
        {
            if (!ReferenceEquals(_stream, stream)) return "The over was stopped before it started.";
        }
        return null;
    }

    /// <summary>True while <paramref name="stream"/> is the keyboard over this transmitter is sending.</summary>
    public bool IsStreaming(AudioStream stream)
    {
        lock (_gate) return _stream is not null && ReferenceEquals(_stream, stream);
    }

    /// <summary>
    /// Checks what is on the air now, or prepared to go out, against the band-edge limits at <paramref name="dialHz"/>.
    /// Returns null when there is nothing or it still fits, otherwise why it must stop (the dial moved during a
    /// transmission, or after it was prepared and checked at the old dial).
    /// </summary>
    public string? OnAirOutsideBand(string band, long dialHz)
    {
        Mode mode;
        int offset;
        int? width;
        lock (_gate)
        {
            if (_stream is not null)
            {
                (mode, offset, width) = (_streamMode, _streamOffsetHz, _streamWidthHz);
            }
            else if ((_current ?? _next) is { } tx)
            {
                // A Tune tone is checked as FT8 is when it starts.
                (mode, offset, width) = (tx.Mode, tx.OffsetHz, null);
            }
            else
            {
                return null;
            }
        }
        return _frequencies.TransmitAllowed(band, mode, dialHz, offset, width, out var why) ? null : why;
    }

    /// <summary>The message on the air now, or null.</summary>
    public string? CurrentMessage
    {
        get { lock (_gate) return _current?.Plan.Message; }
    }

    /// <summary>The transmission waiting for its slot.</summary>
    public PreparedTx? Pending
    {
        get { lock (_gate) return _next; }
    }

    /// <summary>True while the pending transmission is held back waiting for decodes.</summary>
    public bool Held
    {
        get { lock (_gate) return _hold is not null && _next is not null; }
    }

    /// <summary>
    /// Counts Halts. A caller that decides to transmit under its own lock reads this there and passes it to
    /// <see cref="Prepare"/>, so a Halt that comes after the decision but before the transmission is installed wins.
    /// </summary>
    public long HaltEpoch
    {
        get { lock (_gate) return _haltEpoch; }
    }

    /// <summary>Test seam: called on the slot thread after a slot event has picked its transmission, before acting on it.</summary>
    internal Action<SlotEvent>? SlotEventCaptured { get; set; }

    /// <summary>Slot start of the latest transmission that went on the air, or null.</summary>
    public DateTime? LastStartedSlot
    {
        get { lock (_gate) return _lastStartedSlot; }
    }

    /// <summary>True when audio for the slot has started (or the slot's transmission has finished).</summary>
    public bool AudioStarted(DateTime slotStartUtc)
    {
        lock (_gate) return (_current is { } c && c.Plan.SlotStartUtc == slotStartUtc) || _lastStartedSlot == slotStartUtc;
    }

    /// <summary>Replaces the rig (after setup or a profile change).</summary>
    public void SetRig(IRig rig)
    {
        _rig.Faulted -= OnRigFault;
        _rig = rig;
        _rig.Faulted += OnRigFault;
    }

    /// <summary>Replaces the audio output.</summary>
    public void SetOutput(IAudioOutput? output)
    {
        if (_output is not null) _output.Faulted -= OnAudioFault;
        _output = output;
        _outputFault = null;
        if (_output is not null) _output.Faulted += OnAudioFault;
    }

    /// <summary>
    /// Checks and renders a transmission for its slot. Returns null and a reason when it must not go out: the message
    /// cannot be sent exactly as shown, it would fall outside the band, the clock is off, or a device is missing.
    /// With a <paramref name="hold"/>, the slot's PTT and audio marks are skipped until <see cref="Release"/> or a new
    /// <see cref="Prepare"/>; if neither comes before the hold expires, the transmission starts late by itself.
    /// <paramref name="haltEpoch"/> is the <see cref="HaltEpoch"/> the caller read when it decided to transmit (the
    /// current one when null): if a Halt has come since, nothing is installed and null is returned with a null
    /// <paramref name="refusal"/>, since the Halt already says why nothing goes out.
    /// </summary>
    public PreparedTx? Prepare(TxPlan plan, Mode mode, int offsetHz, TxGuardInput guard, out string? refusal, TxHold? hold = null, long? haltEpoch = null)
    {
        long epoch;
        lock (_gate) epoch = haltEpoch ?? _haltEpoch;
        refusal = DisabledReason is { } disabled ? Refuse("disabled", disabled)
            : !guard.AudioReady || _output is null ? Refuse("no_audio", "No audio output device is open; transmitting is disabled.")
            : _outputFault is { } dead ? Refuse("output_dead", OutputDeadText(dead))
            : SilencedText() is { } silent ? Refuse("output_muted", silent)
            : guard.RigFault is not null ? Refuse("radio_fault", $"Radio fault: {guard.RigFault}")
            : !ClockEstimate.TransmitAllowed(guard.ClockOffsetSeconds, guard.ClockBlockSeconds, out var clockWhy) ? Refuse("clock", clockWhy)
            : !_frequencies.TransmitAllowed(guard.Band, mode, guard.DialHz, offsetHz, out var bandWhy) ? Refuse("band_edge", bandWhy)
            : null;
        if (refusal is not null || _output is null) return null;
        var enc = FtxEncoder.Encode(plan.Message, mode);
        if (!enc.Ok)
        {
            refusal = Refuse("message", $"Not sent: \"{plan.Message}\". {enc.Error}");
            return null;
        }
        var samples = GfskSynth.Render(enc.Tones, mode, offsetHz, _output.SampleRate, Math.Pow(10, GainDb / 20));
        var tx = new PreparedTx(plan, mode, offsetHz, samples, _output.SampleRate) { HaltEpoch = epoch };
        lock (_gate)
        {
            // Halted while this was being checked and rendered: the Halt wins.
            if (_haltEpoch != epoch) return null;
            _next = tx;
            _hold = hold;
        }
        Committed?.Invoke();
        return tx;
    }

    /// <summary>
    /// Lifts the hold on the pending transmission. Returns false when nothing was held. The caller starts it with
    /// <see cref="BeginLate"/> if its PTT mark has passed.
    /// </summary>
    public bool Release()
    {
        lock (_gate)
        {
            if (_hold is null || _next is null) return false;
            _hold = null;
            return true;
        }
    }

    /// <summary>Drops a prepared transmission that has not started, releasing PTT if it was asserted for it.</summary>
    public void CancelPending()
    {
        lock (_gate)
        {
            _next = null;
            _hold = null;
            _lateAudioDueUtc = null;
            _lateAlignUtc = null;
            _answerWaitMarkUtc = null;
            if (_current is null && _ptt) _pttOffDueUtc = _clock.UtcNow;
        }
    }

    /// <summary>Slot clock events. Called on the slot thread; never waits on I/O.</summary>
    public void OnSlotEvent(SlotEvent e)
    {
        PreparedTx? tx;
        long epoch;
        lock (_gate)
        {
            tx = _next is { } n && n.Plan.SlotStartUtc == e.SlotStartUtc ? n : null;
            // Held for decodes, or already started late: Poll starts the audio.
            if (_hold is not null || _lateAudioDueUtc is not null) tx = null;
            epoch = _haltEpoch;
        }
        if (tx is null) return;
        SlotEventCaptured?.Invoke(e);

        switch (e.Mark)
        {
            case SlotMark.PttOn when e.LateBy <= LateStartLimit:
                lock (_gate)
                {
                    // A Halt (or a new Prepare, a hold or a late start) since the capture above: leave PTT alone.
                    if (!StillNext(tx, epoch)) return;
                    _ptt = true;
                }
                KeyOn(epoch);
                break;
            case SlotMark.TxStart when e.LateBy <= LateStartLimit:
                lock (_gate)
                {
                    if (!StillNext(tx, epoch)) return; // halted or replaced since the capture above
                    if (!_ptt) return; // PTT was not asserted (fault or too late): do not play
                    if (!PttAnswered)
                    {
                        // The radio has not answered the PTT command yet: Poll starts the audio the moment it does.
                        _lateAudioDueUtc = _clock.UtcNow;
                        _lateAlignUtc = null;
                        _answerWaitMarkUtc = _clock.UtcNow - e.LateBy;
                        return;
                    }
                    _next = null;
                    _current = tx;
                    _lastStartedSlot = tx.Plan.SlotStartUtc;
                    _audioStartedUtc = _clock.UtcNow;
                    _pttOffDueUtc = _audioStartedUtc + tx.Duration + Tail;
                }
                if (!PlayUnlessHalted(tx.Samples, epoch)) return;
                Started?.Invoke(tx, _audioStartedUtc);
                break;
            case SlotMark.PttOn or SlotMark.TxStart:
                // Too late to start this slot: skip it.
                lock (_gate)
                {
                    if (!StillNext(tx, epoch)) return;
                    _next = null;
                    if (_current is null && _ptt) _pttOffDueUtc = _clock.UtcNow;
                }
                Poll();
                break;
        }
    }

    /// <summary>
    /// Starts a prepared transmission after its marks have passed (decodes arrived late, or the operator acted late): PTT
    /// now, audio after the PTT lead. Without <paramref name="alignToUtc"/> the whole message is sent, shifted later.
    /// With it, the message keeps its place in the slot as if it had started at <paramref name="alignToUtc"/> (the Tx
    /// start) and the part that would have been sent already is left out, as WSJT-X does for a late start. The caller
    /// has checked that the start is within the limit. Returns false, keying nothing, when a Halt has come since
    /// <paramref name="tx"/> was prepared.
    /// </summary>
    public bool BeginLate(PreparedTx tx, TimeSpan pttLead, DateTime? alignToUtc = null)
    {
        ArgumentNullException.ThrowIfNull(tx);
        lock (_gate)
        {
            if (tx.HaltEpoch != _haltEpoch) return false;
            _next = tx;
            _ptt = true;
            _lateAudioDueUtc = _clock.UtcNow + pttLead;
            _lateAlignUtc = alignToUtc;
            _answerWaitMarkUtc = null;
        }
        KeyOn(tx.HaltEpoch);
        return true;
    }

    /// <summary>Releases PTT when the audio and its tail have finished. Called on the slot thread every millisecond.</summary>
    public void Poll()
    {
        PollStream();
        var keyHeld = false;
        long epoch;
        lock (_gate)
        {
            epoch = _haltEpoch;
            if (_hold is { } h && _next is not null && _clock.UtcNow >= h.UntilUtc)
            {
                // The decodes did not arrive in time: send what was prepared, late but within the limit.
                _hold = null;
                _ptt = true;
                _lateAudioDueUtc = _clock.UtcNow + h.PttLead;
                keyHeld = true;
            }
        }
        if (keyHeld) KeyOn(epoch);

        var unanswered = false;
        lock (_gate)
        {
            unanswered = _ptt && !PttAnswered && _current is null && !_streamPlaying && _clock.UtcNow - _pttOnAtUtc >= PttConfirmLimit;
        }
        if (unanswered)
        {
            _ = AbortAsync($"Not sent: the radio did not answer the transmit command within {PttConfirmLimit.TotalSeconds:0} s.");
            return;
        }

        PreparedTx? late = null;
        lock (_gate)
        {
            epoch = _haltEpoch;
            if (_lateAudioDueUtc is { } la && _clock.UtcNow >= la && _next is { } n && _ptt && PttAnswered)
            {
                _lateAudioDueUtc = null;
                _next = null;
                // An answer that took longer than the late-start limit: the message keeps its place in the slot and
                // the part already past is left out, so it still ends before the next slot begins.
                if (_lateAlignUtc is null && _answerWaitMarkUtc is { } mark && _clock.UtcNow - mark > LateStartLimit) _lateAlignUtc = mark;
                _answerWaitMarkUtc = null;
                if (_lateAlignUtc is { } align)
                {
                    // Leave out what would already have been sent; nothing left means nothing to send.
                    var skip = (int)Math.Round((_clock.UtcNow - align).TotalSeconds * n.SampleRate);
                    n = skip <= 0 ? n : n with { Samples = skip < n.Samples.Length ? n.Samples[skip..] : [] };
                    _lateAlignUtc = null;
                }
                _current = n;
                _lastStartedSlot = n.Plan.SlotStartUtc;
                _audioStartedUtc = _clock.UtcNow;
                _pttOffDueUtc = _audioStartedUtc + n.Duration + Tail;
                late = n;
            }
        }
        if (late is not null && PlayUnlessHalted(late.Samples, epoch)) Started?.Invoke(late, _audioStartedUtc);

        PreparedTx? done = null;
        var streamDone = false;
        lock (_gate)
        {
            if (_pttOffDueUtc is not { } due || _clock.UtcNow < due) return;
            _pttOffDueUtc = null;
            _ptt = false;
            done = _current;
            _current = null;
            if (_stream is not null && _streamPlaying)
            {
                _stream = null;
                _streamPlaying = false;
                streamDone = true;
            }
        }
        _ = SetPttSafe(false);
        if (done is not null) Ended?.Invoke(done, true);
        if (streamDone) StreamEnded?.Invoke(true);
    }

    // A keyboard over: audio once the PTT lead has passed, and the PTT tail once the stream has played out.
    private void PollStream()
    {
        AudioStream? start = null;
        long epoch;
        lock (_gate)
        {
            if (_stream is null) return;
            epoch = _haltEpoch;
            var now = _clock.UtcNow;
            if (_streamAudioDueUtc is { } due)
            {
                if (now < due || !_ptt || !PttAnswered) return;
                _streamAudioDueUtc = null;
                _streamPlaying = true;
                start = _stream;
            }
            else if (_streamPlaying && _pttOffDueUtc is null && _stream.Finished && _output?.IsPlaying != true)
            {
                _pttOffDueUtc = now + Tail;
            }
        }
        if (start is null) return;
        _output!.PlayStream(start);
        lock (_gate)
        {
            if (_haltEpoch == epoch)
            {
                StreamStarted?.Invoke();
                return;
            }
        }
        _output.Stop(); // halted while it started: the Halt's Stop came first
    }

    /// <summary>Halt: stops audio and drops PTT immediately. Keying planned before it is refused afterwards.</summary>
    public Task HaltAsync()
    {
        long ignored = 0;
        return Halt(ref ignored, keepEpoch: false);
    }

    /// <summary>
    /// Halts like <see cref="HaltAsync()"/> to make way for a transmission the caller has already decided on (a Tune
    /// tone giving way to a message): when <paramref name="haltEpoch"/> is still current it is moved past this Halt, so
    /// that transmission is not refused because of it. A stale one stays stale: another Halt came first and wins.
    /// </summary>
    public Task HaltForNextAsync(ref long haltEpoch) => Halt(ref haltEpoch, keepEpoch: true);

    private Task Halt(ref long haltEpoch, bool keepEpoch)
    {
        PreparedTx? cut;
        bool streamCut;
        lock (_gate)
        {
            var current = haltEpoch == _haltEpoch;
            _haltEpoch++;
            if (keepEpoch && current) haltEpoch = _haltEpoch;
            streamCut = _stream is not null;
            _stream = null;
            _streamAudioDueUtc = null;
            _streamPlaying = false;
            cut = _current;
            _current = null;
            _next = null;
            _hold = null;
            _pttOffDueUtc = null;
            _lateAudioDueUtc = null;
            _lateAlignUtc = null;
            _answerWaitMarkUtc = null;
            _ptt = false;
            _pttOn = null;
        }
        return FinishHaltAsync(cut, streamCut);
    }

    private async Task FinishHaltAsync(PreparedTx? cut, bool streamCut)
    {
        _output?.Stop();
        await SetPttSafe(false).ConfigureAwait(false);
        if (cut is not null) Ended?.Invoke(cut, false);
        if (streamCut) StreamEnded?.Invoke(false);
    }

    /// <summary>
    /// Starts the radio's own antenna tuner, after the same radio and band-edge guards as a tone. True when the radio
    /// accepted it; false when the radio has no tuner command, refused it, or a guard refuses (the caller then sends a
    /// tune tone, whose guards say why).
    /// </summary>
    public async Task<bool> StartRigTunerAsync(int offsetHz, TxGuardInput guard)
    {
        if (DisabledReason is not null || guard.RigFault is not null) return false;
        if (!_frequencies.TransmitAllowed(guard.Band, Mode.Ft8, guard.DialHz, offsetHz, out _)) return false;
        return await _rig.StartTunerAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Tune: a steady tone at the offset for up to <paramref name="seconds"/> (the Tune watchdog), PTT on now. Returns
    /// null once the tone is on the air, or why it was refused or stopped: no audio output, a radio fault or no radio,
    /// outside the band, or PTT could not be asserted (then no audio is played).
    /// </summary>
    public async Task<string?> TuneAsync(int offsetHz, double seconds, TxGuardInput guard)
    {
        if (DisabledReason is not null) return Refuse("disabled", DisabledReason);
        if (!guard.AudioReady || _output is null) return Refuse("no_audio", "No audio output device is open.");
        if (_outputFault is { } dead) return Refuse("output_dead", OutputDeadText(dead));
        if (SilencedText() is { } silent) return Refuse("output_muted", silent);
        if (guard.RigFault is not null) return Refuse("radio_fault", $"Radio fault: {guard.RigFault}");
        if (!_frequencies.TransmitAllowed(guard.Band, Mode.Ft8, guard.DialHz, offsetHz, out var why)) return Refuse("band_edge", why);
        var n = (int)(seconds * _output.SampleRate);
        var tone = new float[n];
        var amp = Math.Pow(10, GainDb / 20);
        for (var i = 0; i < n; i++) tone[i] = (float)(amp * Math.Sin(2 * Math.PI * (offsetHz + 25) * i / _output.SampleRate));
        var tx = new PreparedTx(new TxPlan(_clock.UtcNow, Session.TuneMessage, null, null), Mode.Ft8, offsetHz, tone, _output.SampleRate);
        long epoch;
        lock (_gate)
        {
            epoch = _haltEpoch;
            tx = tx with { HaltEpoch = epoch };
            _ptt = true;
            _current = tx;
            _pttOffDueUtc = _clock.UtcNow + tx.Duration + Tail;
        }
        Committed?.Invoke();
        var failed = await KeyOn(epoch).ConfigureAwait(false);
        if (failed is not null) return failed; // halted: PTT released, nothing to play
        lock (_gate)
        {
            if (!ReferenceEquals(_current, tx)) return "The tone was stopped before it started."; // halted meanwhile
        }
        if (!PlayUnlessHalted(tone, epoch)) return "The tone was stopped before it started.";
        ToneStarted?.Invoke(tx);
        return null;
    }

    // Under _gate. True once the radio has answered the latest PTT-on command (or none is outstanding).
    private bool PttAnswered => _pttOn is null || _pttOn.IsCompleted;

    // Under _gate. True while tx is still the one waiting for its slot, with no Halt, hold or late start since.
    private bool StillNext(PreparedTx tx, long epoch) =>
        _haltEpoch == epoch && ReferenceEquals(_next, tx) && _hold is null && _lateAudioDueUtc is null;

    // Keys the radio and remembers the command, so audio can wait for the radio's answer. Nothing is keyed when a Halt
    // has come since epoch was read. A Halt that lands before the radio has answered may have released PTT before this
    // PTT-on took effect: PTT is then released again once it has, so it is never left keyed with nothing to release it.
    private Task<string?> KeyOn(long epoch)
    {
        lock (_gate)
        {
            if (_haltEpoch != epoch) return Task.FromResult<string?>(HaltedText);
            _pttOnAtUtc = _clock.UtcNow;
        }
        var command = SetPttSafe(true);
        lock (_gate)
        {
            if (_haltEpoch == epoch) _pttOn = command;
        }
        _ = command.ContinueWith(t =>
        {
            // A failed PTT-on has already halted and released PTT itself.
            if (t.Result is null && HaltEpoch != epoch) _ = SetPttSafe(false);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return command;
    }

    // Plays samples unless a Halt has come since epoch was read. A Halt that lands during Play has already stopped the
    // output before this Play started, so the output is stopped again. False when nothing is left playing.
    private bool PlayUnlessHalted(float[] samples, long epoch)
    {
        lock (_gate)
        {
            if (_haltEpoch != epoch) return false;
        }
        _output!.Play(samples);
        lock (_gate)
        {
            if (_haltEpoch == epoch) return true;
        }
        _output.Stop();
        return false;
    }

    private const string HaltedText = "Transmission halted.";

    /// <summary>Sets PTT. When asserting fails, halts and returns why (also raised as <see cref="Fault"/>).</summary>
    private async Task<string?> SetPttSafe(bool on)
    {
        try
        {
            if (on) PttChanged?.Invoke(true); // before keying: RF follows
            await _rig.SetPttAsync(on, CancellationToken.None).ConfigureAwait(false);
            if (!on) PttChanged?.Invoke(false); // only once the radio has released
            return null;
        }
        catch (Exception ex)
        {
            // Any failure, not only a rig error: a release that fails silently would leave the UI saying PTT is off.
            if (!on)
            {
                Fault?.Invoke(ex.Message);
                return ex.Message;
            }
            var why = $"PTT could not be asserted: {ex.Message}";
            await AbortAsync(why).ConfigureAwait(false);
            return why;
        }
    }

    private void OnRigFault(object? sender, RigFault f)
    {
        if (Transmitting) _ = AbortAsync($"Radio fault during transmission: {f.Message}");
    }

    private void OnAudioFault(object? sender, string why)
    {
        // The device's stream has stopped for good: every later transmission would key PTT and send silence.
        _outputFault = why;
        if (Transmitting) _ = AbortAsync($"Audio output fault during transmission: {why}");
        else
        {
            // A transmission prepared before the fault would key PTT into the dead output at its slot.
            CancelPending();
            Fault?.Invoke(OutputDeadText(why));
        }
    }

    private string Refuse(string kind, string text)
    {
        Refused?.Invoke(kind);
        return text;
    }

    // Keying the radio with the output muted puts nothing on the air and looks like a broken radio: refuse and say why.
    private string? SilencedText() => _output?.Silenced is { } why
        ? $"Not sent: the audio output device in Settings > Audio is silenced ({why}), so the radio would get no audio. "
          + "Unmute it and set its volume to 100 in Windows sound settings."
        : null;

    private static string OutputDeadText(string why) =>
        $"The audio output stopped ({why}). Costas opens it again when the device is back, for a radio's USB audio when the radio is on.";

    /// <summary>Stops the transmission like Halt, then reports <paramref name="message"/> as a fault.</summary>
    public async Task AbortAsync(string message)
    {
        await HaltAsync().ConfigureAwait(false);
        Fault?.Invoke(message);
    }
}
