# 10a · WSPR build guide

Step-by-step directions for building `10-wspr.md`. The spec says *what*; this says *where and how*, names the existing code to copy, the traps, and when to stop and ask the owner. Follow it in order. Where it and the spec disagree, the spec wins, and you say so in the PR.

## 0. Ground rules for whoever builds this

- Read `CLAUDE.md`, `10-wspr.md`, `03-architecture.md` and `09-keyboard-modes.md` section 12 (how a non-FT mode was added last time) before writing code.
- **One PR per step** (W0a, W0b, W1, …). Each PR: `dotnet build` with no warnings, `dotnet test` all green, the step's tests added, docs updated in the same PR. Do not start a step until the previous one is merged.
- Conventions that reviewers check every time: GPLv3 header on every new file (`scripts/header.txt`), one class per file, XML doc comments on public Core types, tests named `Method_Condition_Result`, time driven by `ManualClock` (`src/Ft8Client.Core/Time/ManualClock.cs`), no sleeps in tests, stand-in executables written with `tests/Shared/ExecutableScript.cs`.
- Never run anything that keys a real radio. All transmit tests use `SimulatedRig` and a null audio output.
- **Manual acceptance items** (anything marked Manual) cannot be done by you. Put them as an unchecked checklist in the PR description for the owner.

### Stop and ask the owner (do not guess)

Stop, write the question in the PR or chat, and wait when:

1. A VERIFY item (V-WSPR1 to V-WSPR8) cannot be confirmed from the source named for it, or the source contradicts the spec.
2. No way of producing `wsprcode`-style reference vectors works (V-WSPR8). Do not lower the W3 acceptance on your own.
3. Anything would change what goes on the air beyond what `10-wspr.md` says: slot choice, timing, power, message content, hop order, retries.
4. A guard (band edge, licence, clock, presence, fault) would have to be weakened or skipped to make something work.
5. An existing FT8, FT4, PSK or RTTY test has to change. Behaviour of the existing modes must stay identical.

## 1. Traps in the existing code

Read these before W0. Each has bitten a mode addition before or will now.

| Trap | Where | What to do |
| --- | --- | --- |
| `ModeInfo.IsSlotted` means "runs the FT8/FT4 pipeline" (jt9, contact engine, early pass), not "has time slots". About 60 call sites rely on that, and many use `!IsSlotted` to mean "keyboard mode" | `src/Ft8Client.Core/ModeInfo.cs`, `Session.cs`, `Session.Keyboard.cs`, view models | Leave `IsSlotted` false for WSPR. Add `IsKeyboard` and `IsWspr` (W0b) and replace every `!IsSlotted` that means keyboard with `IsKeyboard`, so WSPR never falls into the keyboard path by accident |
| `ModeInfo.SlotMilliseconds`, `DecoderSamples`, `Separator`, `TxStartMilliseconds`, `TxMilliseconds` all go through `Slotted()`, which throws for any non-FT mode | `ModeInfo.cs` | Give the timing members explicit WSPR cases (W0a). `DecoderSamples` and `Separator` stay FT-only (they are `jt9` facts) |
| `SlotClock` forces non-slotted modes to FT8 in its constructor and ignores them in `SetMode`; `OffsetOf` assumes FT8 or FT4 | `src/Ft8Client.Audio/Timing/SlotClock.cs` | Add `ModeInfo.HasSlots` (FT8, FT4, WSPR) and use it here only. WSPR marks: see W1 |
| `SlotRecorder`'s buffer is sized for one FT8 slot, and `Cutoff`/`Snapshot` size their output with `DecoderSamples` | `src/Ft8Client.Audio/Timing/SlotRecorder.cs` | Size the buffer for the longest `HasSlots` mode and use a new `ModeInfo.RecordSamples(mode)` (W1). The buffer is allocated once in the constructor, never in `Append` |
| `Session.OnSlotEvent` sends every non-FT mode to the keyboard transmitter | `src/Ft8Client.App/Engine/Session.cs` (around line 223) | Branch to WSPR first (W1) |
| FT commands (`CallStation`, `CallCq`, `AnswerCaller`, Auto-call, macros) assume FT or keyboard | `Session.cs`, `Session.AutoCall.cs`, `Session.Keyboard.cs` | In WSPR each refuses with "Not in WSPR mode" and changes nothing (W1). Tune and Halt Tx keep working |
| Automatic transmit level (`TxLevelController`, `TxLevelTuner`) changes the audio drive to hit an ALC target, which would undo the WSPR attenuation | `src/Ft8Client.App/Engine/TxLevelController.cs` | Pause automatic level changes in WSPR; the attenuation is applied on top of the operator's last level (W4). Note this in the PR for the owner |
| `Transmitter.Prepare` takes an FT `TxPlan` and encodes it itself | `src/Ft8Client.App/Engine/Transmitter.cs` | Add a sibling that takes pre-rendered samples and runs the **same** guard checks; never copy the guards (W4) |
| The keyboard watchdog limits an over to 3 minutes (`KeyboardTransmitter.MaxOver`) | `KeyboardTransmitter.cs` | Not used by WSPR. WSPR's longest transmission is one slot's audio; the transmitter refuses samples longer than `WsprTiming.TxLength` plus 1 s |

## 2. Steps

### W0a · Facts first (docs only, no code)

Resolve V-WSPR1 to V-WSPR8 in `08-open-questions.md`. For each: what you ran or read, the exact output or text, the date. Move each resolved row from "Facts to verify" to "Findings".

- Use the WSJT-X **3.0.1** source (the version `third_party/fetch.ps1` downloads), from `github.com/WSJTX/wsjtx`, tag `v3.0.1` (SourceForge's git has no 3.x tags). Quote file paths and line numbers.
- **V-WSPR2 (`wsprd`):** run `third_party/wsjtx/bin/wsprd.exe` with no arguments to get its usage text and save it. Find WSJT-X's WSPR sample WAVs (in the source tree under `samples/`, or the WSJT-X samples download) and copy two or more into `samples/wspr/` with a `README.md` saying where they came from. Run `wsprd` on each from an empty folder and save stdout and every file it writes as golden output (`samples/wspr/<name>.golden.txt`). Record: arguments WSJT-X passes (find where WSJT-X starts `wsprd` in the source), input length in samples, output line format with an annotated example, files it needs beside it (run from a clean folder and note missing-DLL errors), and decode time.
- **V-WSPR1 (timing):** from the source, the Tx start offset in the slot, the symbol length and count, and how long into the slot WSJT-X waits before decoding (this sets `WsprTiming.CaptureCutoff`).
- **V-WSPR3 (dial frequencies):** WSJT-X's default WSPR entries (in its default frequency list in the source). Add them to `src/Ft8Client.Data/Resources/frequencies.json` in W1, not now.
- **V-WSPR4 (WSPRnet upload):** the URL, method, parameters and how often WSJT-X uploads (its WSPRnet upload code in the source). Do not send anything to wsprnet.org yet.
- **V-WSPR6 (schedule):** how WSJT-X picks Tx slots for a Tx percentage, how it alternates type 2 or 3 messages for compound calls and 6-character grids, and its coordinated band-hopping schedule. Write each down as an algorithm with an example.
- **V-WSPR7 (power):** `rigctld` with the dummy rig (`rigctld -m 1`): `l RFPOWER` and `L RFPOWER 0.05`, replies saved. Check how `RadioPowerService` and `RigPower(Watts, MaxWatts)` already set power (`src/Ft8Client.App/Services/RadioPowerService.cs`) and record whether that covers the Flex.
- **V-WSPR8 (reference vectors):** WSJT-X 3.0.1 for Windows ships no `wsprcode` or `wsprsim`. Try in this order and record which worked: (1) the `wsjtx` package on Linux (`capture-vectors.sh` already runs there), (2) building `wsprsim` from the C files in the source's `lib/wsprd/` with gcc in WSL or MSYS2 (C only, no Fortran), (3) building `wsprcode` from the Fortran source. The tool must print the 162 channel symbols for a given message. If none works: stop and ask (rule 2 above).

Done when: every V-WSPR row is resolved or has a stop-and-ask question open, and the samples and golden files are committed.

### W0b · Mode definition (no visible change)

Copy the pattern of M9 (`09-keyboard-modes.md` section 12 step 1).

- `Mode.cs`: add `Wspr` with a doc comment.
- `ModeInfo.cs`:
  - `IsWspr(mode)`, `IsKeyboard(mode)` (PSK31, PSK63, RTTY), `HasSlots(mode)` (FT8, FT4, WSPR).
  - `SlotMilliseconds`, `SlotLength`, `TxStartMilliseconds`, `TxMilliseconds`, `TxEnd`: WSPR cases from V-WSPR1 (120,000 ms; 1,000 ms; 162 × 8192 / 12000 s = 110,592 ms unless V-WSPR1 says otherwise).
  - `OccupiedBandwidthHz(Wspr)` = 200 (the whole 1400-1600 Hz window, as the spec says; owner 2026-11-26); `Adif(Wspr)` = `("WSPR", null)`; `Name(Wspr)` = `"WSPR"`.
  - `DecoderSamples`, `Separator`, `Slotted` stay FT-only and throw for WSPR.
  - **Not** in `Operable` yet.
- Replace each `!ModeInfo.IsSlotted(x)` that means "keyboard mode" with `ModeInfo.IsKeyboard(x)`. List every call site in the PR with "keyboard", "FT pipeline" or "neutral", so the reviewer can check each.
- New Core file `Wspr/WsprTiming.cs`: constants `SlotLength`, `TxStart`, `SymbolCount` (162), `SymbolSamples12k` (8192), `ToneSpacingHz` (12000 / 8192), `TxLength`, `CaptureCutoff`, `RecordSamples`, `WindowLowHz` (1400), `WindowHighHz` (1600), all with doc comments citing the V-WSPR finding.
- Every FT8/FT4-only helper that takes a mode (FT encoder, `Jt9Decoder.BuildArguments`, early pass) throws `ArgumentOutOfRangeException` for WSPR.

Tests (`tests/Ft8Client.Core.Tests`): `ModeInfo` cases for WSPR on every member (value or throws); `TryParse_Wspr_ReturnsFalse`; `IsKeyboard` and `HasSlots` for all six modes. Done when: every existing test passes unchanged.

### W1 · Receive

**Timing and audio** (`Ft8Client.Audio`):
- `SlotClock`: constructor and `SetMode` accept `HasSlots` modes. `OffsetOf` for WSPR: `Start` 0, `PttOn` = `TxStart` − PTT lead, `TxStart` = `WsprTiming.TxStart`, `EarlyDecode` and `CaptureCutoff` both at `WsprTiming.CaptureCutoff` (no early pass). Slot boundaries on even minutes come from `SlotMath.SlotStart` with the 120 s length; check `SlotMath` handles it and add tests.
- `SlotRecorder`: buffer sized for `RecordSamples(Wspr)`, allocated in the constructor. `Cutoff` uses `RecordSamples(mode)`.
- Tests: `SlotClock` with `ManualClock` across a WSPR slot (marks in order, at the right offsets, boundary on an even minute, a time jump mid-slot); `SlotRecorder` length for WSPR.

**Decoder** (`Ft8Client.Decoding`), copying `Jt9Decoder.cs` and `Jt9Locator.cs`:
- `WsprdOptions`, `WsprdLocator` (finds `wsprd(.exe)` beside `jt9`), `WsprdDecoder`. Per slot: a fresh scratch folder under the temp root, the WAV written with the existing `WavFile` writer, `wsprd` run with the arguments from V-WSPR2, a timeout (60 s), the process killed on timeout or cancel through `IProcessGuard`, the output read, the folder deleted.
- It is a separate class from `IDecoder` if `IDecoder`'s FT-shaped `DecodeContext` does not fit; do not bend `IDecoder`.
- Tests with `ExecutableScript` stand-ins: normal output parsed, a hang killed and reported, a non-zero exit reported, missing executable reported. A test on the real `wsprd` against `samples/wspr/` golden files, skipped when `third_party` is absent (copy how the `jt9` golden tests skip).

**Parser and spots** (Core, `Wspr/`):
- `WsprSpot` record: `SlotStartUtc`, `Snr` (int dB), `DtSeconds` (double), `FrequencyHz` (long, RF), `DriftHz` (int), `Callsign`, `Grid` (string?), `PowerDbm` (int), `IsHashed` (bool).
- `WsprSpotParser.TryParse(line, slotStartUtc, dialHz, out spot)`: exact format from V-WSPR2. Table tests for type 1, 2 and 3 lines, `<...>` unresolved hash, 6-character grid, negative drift, and garbage lines (returns false, never throws).
- Distance: reuse the existing grid distance code in `Core/Geo`.

**Storage** (`Ft8Client.Data`):
- Migration `Migrations/008_wspr_spot.sql` (next free number at the time; copy the style of the existing ones): table `wspr_spot` with the fields above plus `dial_hz`, `distance_km`, `upload_state`, `profile_callsign`; index on time.
- `WsprSpotRepository` (Dapper, copy `SpotRepository`): add, recent N, pending uploads, mark uploaded. Tests with an in-memory database.

**Session and simulation** (`Ft8Client.App`):
- New partial `Engine/Session.Wspr.cs`. In `Session.OnSlotEvent`, before the keyboard branch: `if (ModeInfo.IsWspr(Config.Mode)) { OnWsprSlotEvent(e); return; }`. At `CaptureCutoff`: hand the slot to `WsprdDecoder` off the slot thread, parse, store, publish.
- A slot recorded across a band change or a dial move of more than 3 kHz is dropped (find how D24 does it for FT8 and reuse it).
- FT commands refuse in WSPR (trap table).
- `SessionSnapshot` gains a WSPR part (recent spots, next slot time, decoder state); keep it immutable like the rest.
- `frequencies.json`: WSPR dial frequencies from V-WSPR3. `ModeInfo.Operable` gains WSPR. The Mode menu then lists it.
- `--simulate`: replay `samples/wspr/` through the same path (copy `SimulatedSlotSource`).
- Tests: simulation over `samples/wspr/` yields the golden spots; FT commands refused in WSPR; switching FT8 → WSPR → FT8 leaves FT8 behaviour unchanged.

### W2 · Reporting to WSPRnet

Copy `PskSpotUploader` and `PskReporterService` for shape, and `Fakes/FakeQrzServer.cs` for the fake.

- Settings: `Data/Settings/WsprOptions.cs` (copy `PskReporterOptions`): `UploadEnabled` (default false) and the beacon settings W4 needs. Add it to `AppSettings` and the settings migration test.
- `Integrations/Wsprnet/WsprnetClient.cs`: one request per V-WSPR4, over HTTPS (`https://wsprnet.org/post/`; owner 2026-11-26: confirm with one real spot that the reply contains `spot(s) added`, and stop and ask if it does not), `HttpClient` with a 10 s timeout and the User-Agent `Costas/<version> (<callsign>)` (find how the PSK Reporter and QRZ clients set it and reuse it).
- `Integrations/Wsprnet/WsprnetUploader.cs`: reads pending rows from `WsprSpotRepository`, sends, marks uploaded; on failure backs off 1, 2, 4 … up to 30 minutes; a rejected spot is marked failed and not retried forever.
- `Integrations/Fakes/FakeWsprnetServer.cs`: accepts, rejects, times out on command. Stops its listener only after its accept loop ends (CLAUDE.md).
- `App/Services/WsprnetService.cs` and `ServiceNames.Wsprnet`, so the status indicator shows ok, retrying or down. Uploads never run with `--simulate` (copy `uploadsAllowed: !Simulating` in `AppHost.cs`).
- Heard by (optional in this step; may move to W5): only if V-WSPR5 found a documented query. Display only, off when unreachable.
- Tests: upload against the fake server; queue survives a restart (new repository over the same file); back-off on failure; nothing sent when the setting is off; the app session keeps running with the server down.
- Manual (owner): spots appear under the owner's call on wsprnet.org.

### W3 · Encoder

Port from the WSJT-X 3.0.1 C source in `lib/wsprd/` (the files found in V-WSPR8; quote them in the PR). GPLv3 to GPLv3, so a port is allowed; keep a comment naming the source file and function above each ported function.

Core, `Wspr/`, one class per file:
- `WsprMessage` (callsign, grid, dBm, type) and `WsprPacker`: type 1 (callsign of up to 6 with a digit in the third place after padding, 4-character grid, dBm), type 2 (prefix or suffix), type 3 (hashed call and 6-character grid, the hash function ported exactly). `TryPack` returns the 50 bits or a refusal reason; never adjusts the input (rule 4).
- `WsprPower`: the legal dBm list (0 to 60 ending in 0, 3 or 7), `IsLegal`, `EstimateDbm(watts, attenuationDb)` = 10 × log10(watts × 1000) + attenuationDb, rounded to one decimal for display only.
- `WsprConvolutional` (K=32, r=1/2, polynomials from the source), `WsprInterleaver` (bit-reversal as in the source), `WsprSync` (the 162-bit sync vector copied from the source, with a test that it has 162 entries), `WsprEncoder` (message → 162 symbols 0 to 3).
- `WsprWaveform.Render(symbols, offsetHz, sampleRate, attenuationDb)`: continuous-phase 4-FSK, tone k at `offsetHz + k × ToneSpacingHz`, 8192 samples per symbol at 12 kHz (scale for other output rates the way `GfskSynth` does), with a short raised-cosine ramp at both ends to avoid key clicks, amplitude scaled by 10^(attenuationDb / 20). Allocates its output once; called before the slot, never in an audio callback.
- Vectors: a `scripts/capture-wspr-vectors.sh` (copy `capture-vectors.sh`) using `wsprsim -c` built from `lib/wsprd/` as V-WSPR8 records, writing `tests/Ft8Client.Core.Tests/Vectors/wspr.tsv` (message, type, 162 symbols). At least 40 messages: plain calls, calls of 3 to 6 characters, every power value, compound calls with prefix and suffix, 6-character grids, and edge grids (AA00, RR99).
- Tests: every vector matches symbol for symbol; `TryPack` refusals (no digit, too long, bad grid, illegal dBm) with the reason text; round trip: render each of 10 messages, write a WAV, decode through the real `wsprd` (skipped without `third_party`), get the same call, grid and dBm with |DT| ≤ 1 s and frequency within 2 Hz.

### W4 · Beacon transmit

Core, `Wspr/`, all pure logic tested with `ManualClock`:
- `WsprPresence`: `NoteOperatorInput(utc)`, `Expired(now, watchdogMinutes)` using `max(watchdogMinutes, 2)`. Feed it from the same places the Tx watchdog gets operator input (find them from `ContactEngine.WatchdogExpired` and `Session.SetWatchdogCap`; the watchdog cap applies too). Beacon transmissions, hops and decodes never call it.
- `WsprBeaconScheduler`: given slot start, Tx percentage, an injected `Random`, the hop state and presence, returns Transmit or Receive with a reason. Algorithm: WSJT-X's `create_tx_schedule`/`next_tx_state` as V-WSPR6 records (owner 2026-11-26), a 60-slot table per 2-hour UTC block rebuilt when the block or the percentage changes, drawn from the injected `Random` (WSJT-X's unseeded C `rand()` cannot be matched, so the method is copied, not its slots). Receives when: beacon off, presence expired, first slot after a hop, or a guard refusal on the last plan.

App, `Session.Wspr.cs`:
- Commands: `StartBeacon()` (operator input; refuses with a reason if the callsign, grid or dBm cannot be packed), `StopBeacon(reason)`, `NoteWsprPresence()` (the phone's "I'm here").
- Planning: before each slot, ask the scheduler. For Transmit: pack and render, then the new `Transmitter` sibling of `Prepare` that takes samples and runs the same guards with `TxGuardInput`: band edge and licence via `FrequencyTable.TransmitAllowed(band, Mode.Wspr, dialHz, WsprTiming.WindowLowHz, 200, out reason)`, clock, rig and audio health. A refusal: receive that slot, show the reason, stop the beacon.
- Offset: the operator's audio offset must be inside 1400 to 1600 Hz minus the signal width; outside, refuse with the reason (never move it).
- Presence expired: let the transmission in progress finish (do not cut it), then stop with "Beacon stopped: no operator input for N minutes (Tx watchdog)".
- Faults: any rig or audio fault releases PTT (existing `Transmitter` behaviour) and stops the beacon; it never restarts by itself.
- Start-up: beacon off. `WsprOptions` never stores "on".
- Power: on Start, remember `RadioPowerService.Power.Watts`, set `max(RadioPowerService.MinWatts, round(MaxWatts × 0.05))` or the operator's setting, only while not transmitting (`CanChange`); on Stop, restore. When the radio has no power control, carry on and show "Set the power on the radio; Costas cannot". Automatic transmit level paused in WSPR (trap table).
- Attenuation: `WsprOptions.AttenuationDb` (0 to −20, default 0) passed to `WsprWaveform.Render`.
- Reported dBm: `WsprOptions.ReportedDbm`, chosen from `WsprPower`'s list. Show `EstimateDbm` and, if the radio reports output power, the meter reading; warn when either differs by more than 3 dB. Never change it.
- Leaving WSPR or a manual band change while the beacon runs: the view model asks first (copy how U2 asks), then `StopBeacon`.

Tests (`tests/Ft8Client.App.Tests` with `SimulatedRig`, null output, `ManualClock`):
- `StartBeacon_NoCommand_NeverKeys`: a session created and run for 30 simulated minutes never asserts PTT.
- PTT lead before audio and tail after it, as the FT8 tests do.
- Rig fault mid-transmission releases PTT and stops the beacon; no later slot transmits.
- Refusals: dial out of band, licence class (General on a segment it lacks), clock offset 3 s, offset outside the window, illegal dBm, unpackable callsign. Each gives the reason text and no PTT.
- Presence: with watchdog 6, no input for 6 minutes stops after the current transmission; with watchdog 1, the limit is 2 minutes; input keeps it running; beacon transmissions do not count.
- Duty cycle: with a seeded `Random` the table follows V-WSPR6's method: at 20 % every gap between transmissions is 3 to 5 receive slots, no two transmissions are consecutive, the first slot of a 2-hour block never transmits, and over 1,000 slots the share is 20 % ± 3.
- Restart: a new session over the same settings starts with the beacon off.
- Power restored on Stop, on fault and on leaving WSPR.
- Manual (owner, dummy load first): beacon heard on WSPRnet.

### W5 · Screen and phone remote

- Draw a mockup first (`mockups/wspr/`, copying how `mockups/keyboard-modes/` was made with the app's own styles) and **stop for the owner's approval** before building the view.
- `ViewModels/WsprViewModel.cs`, `Views/WsprView.axaml`: contents from `10-wspr.md` section 8. `MainWindow` shows it in place of `OperateView` when the mode is WSPR. Distances through the existing miles/km setting. Compiled bindings.
- Remote: add WSPR state to `RemoteState`/`RemoteStateBuilder`, commands to `RemoteCommand` (Start beacon, Stop beacon, I'm here; Halt Tx already exists), and the section in `Remote/index.html`. A remote Start beacon follows the same profile and control rules as other remote transmit commands (D25).
- Settings window: a WSPR section for `WsprOptions`.
- Tests: view-model tests (countdown, next-slot label, power warning, list order newest first); remote command tests; `02-ui-spec.md` updated with the view.

### W6 · Band hopping

- Core `WsprHopSchedule`: from V-WSPR6, the band for a slot given the operator's band list and, if chosen, the coordinated schedule. Pure and clock-driven.
- Session: at the boundary before a slot on a new band, change band through the existing band switch; mark the next slot receive-only; a band whose guards refuse transmit is receive-only with the reason shown.
- Hops do not count as operator input. Presence still stops the beacon.
- Tests with `ManualClock`: band sequence over two hours matches the schedule; first slot after every hop never transmits; a refused band receives; presence expiry stops transmitting while hopping goes on receiving (or stops hopping, if the owner says so; stop and ask if the spec is unclear by then).
- Manual (owner): an evening hopping across three bands.

## 3. Definition of done for the whole mode

- Every step's automated tests pass in `dotnet test` on Windows and Linux CI.
- `10-wspr.md` is updated to say "built" per section, with anything that changed and why.
- `08-open-questions.md`: every V-WSPR item in Findings; D29 status updated.
- The owner's manual checklist from W2, W4, W5 and W6 is listed in one place in the last PR.
