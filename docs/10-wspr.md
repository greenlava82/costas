# 10 · WSPR mode

Owner decision D29 (2026-11-26): WSPR joins the Mode menu as a beacon mode. It receives and reports what it hears to WSPRnet, and transmits a beacon on a schedule while the operator is present. Built 2026-11-26 as W0 to W6 (the status notes at the end say what, and what changed from this text). The V-WSPR items are resolved in `08-open-questions.md` Findings.

Where this document is silent, the existing docs apply unchanged.

## 1. Principles

- WSPR has no contacts. The contact engine, ranker, need tiers and Next in line do not apply. Heard stations are spots, not log entries.
- Decoding goes through WSJT-X's `wsprd` (rule 3), as FT8 goes through `jt9`. `wsprd.exe` already ships in `third_party/wsjtx/bin`.
- The transmit encoder is our own port of WSJT-X's WSPR encoding and is checked against reference symbol vectors from WSJT-X's own tools (rule 3, as for `ft8code`; source of the vectors is V-WSPR8).
- Every transmission starts from an operator action, and the operator must stay present for the whole run (section 6). There is no unattended beacon (section 6.1).

## 2. Protocol facts

| Item | WSPR-2 |
| --- | --- |
| Slot | 120 s, starting on even UTC minutes |
| Transmission | 162 symbols of 8192/12000 s (about 0.683 s), about 110.6 s, starting 1 s into the slot (**VERIFY** V-WSPR1) |
| Modulation | 4-FSK, continuous phase, 12000/8192 Hz (about 1.465 Hz) tone spacing, about 6 Hz wide |
| Payload | 50 bits: callsign, 4-character grid and power in dBm (type 1); compound callsign (type 2); hashed callsign with 6-character grid (type 3). K=32, r=1/2 convolutional code, interleaved, combined with a 162-bit sync vector |
| Power field | dBm values ending in 0, 3 or 7, from 0 to 60 |
| Band window | 200 Hz, 1400 to 1600 Hz audio above the dial (**VERIFY** V-WSPR3) |

## 3. Mode definition

- `Mode.Wspr` in Core. `ModeInfo` gives it a 120 s slot on even minutes, the transmission timing above, ADIF mode `WSPR`, and a 200 Hz occupied bandwidth for the band-edge guard.
- The FT8/FT4 helpers (encoder, `jt9` arguments, early pass, FT slot maths) refuse WSPR, as M9 made them refuse the keyboard modes. `ModeInfo.Operable` gains WSPR only once receive works (W1).
- Dial frequencies per band come from the frequency table (**VERIFY** V-WSPR3). Radio mode USB (or USB-D/DATA where the radio has it), as for FT8.

## 4. Receive

- `SlotRecorder` records 2-minute slots. The 12 kHz buffer (1,440,000 samples) is allocated once up front, so the audio callback never allocates (rule 6).
- `WsprdDecoder` (Decoding) writes the slot to a WAV in its own scratch folder and runs `wsprd` once per slot. The process is supervised: timeout, kill, restart with back-off, kill on app exit. Arguments, input format, output files (`wspr_spots.txt`, `ALL_WSPR.TXT`), line format and decode time are **VERIFY** V-WSPR2.
- A parser in Core reads time, SNR, DT, frequency, drift, callsign, grid and power for all three message types, including hashed `<call>` and 6-character grids. Garbage lines are skipped and counted, never thrown.
- Spots go to a `wspr_spot` table: UTC time, dial Hz, audio Hz, SNR, DT, drift, callsign, grid, dBm, distance (km), upload state.
- A slot recorded across a band change or a dial move of more than 3 kHz is dropped (as D24 does for FT8): not listed and not uploaded.
- `--simulate` replays WSPR WAV recordings from `samples/wspr/`.

## 5. Reporting to WSPRnet

- `WsprnetUploader` (Integrations), off by default, per station profile. It uploads my receive spots in the form WSJT-X uses, over HTTPS (owner 2026-11-26; WSJT-X itself uses plain HTTP) (**VERIFY** V-WSPR4: endpoint, fields, batch size, limits).
- The queue lives in SQLite and survives a restart. It backs off on failure, shows its state on a status indicator like QRZ and PSK Reporter, and never blocks receive (rule 9). Every request carries `Costas/<version> (<callsign>)` (rule 10).
- Optional and display only: a "Heard by" list of stations that reported my beacon, read from WSPRnet or wspr.live (**VERIFY** V-WSPR5). Off when the service is unreachable, with the status shown.

## 6. Beacon transmit

### 6.1 Operator presence (owner decision 2026-11-26)

The owner first asked for an unattended beacon on 10 m. It is dropped: under 47 CFR 97.203(d) a beacon may be automatically controlled only on 28.20-28.30 MHz, 50.06-50.08 MHz, 144.275-144.300 MHz, 222.05-222.06 MHz, 432.300-432.400 MHz and 33 cm and up, and WSPR on 10 m is at about 28.1246 MHz. The automatic digital station rule (97.221) covers only a station responding to interrogation, which a beacon is not. So a control operator must be present, locally or by remote control, on every band. Other countries' rules may be added later. Finding recorded in `08-open-questions.md`.

- The beacon starts only from **Start beacon** at the desk or on the phone. It is off at start-up, never saved as on, and never resumes by itself after a restart, a fault or a watchdog stop.
- **Presence:** any operator input at the desk or on the phone remote (key, click, tap, or the phone's "I'm here") counts. The beacon's own transmissions, band hops and decodes do not.
- **Presence time:** the Tx watchdog minutes (`WatchdogMinutes`, default 6), but never less than one WSPR slot (2 minutes): `max(WatchdogMinutes, 2)`.
- When the presence time passes with no input, the transmission in progress finishes, the beacon stops and the bottom bar says "Beacon stopped: no operator input for N minutes (Tx watchdog)". Receive and reporting go on.

### 6.2 Schedule

- **Duty cycle:** a Tx percentage, default 20 %, so about one slot in five transmits. Which slots are picked: WSJT-X's method (V-WSPR6; owner 2026-11-26), a table of 60 slots per 2-hour UTC block with random gaps, at 20 % 3 to 5 receive slots between transmissions and never two in a row (so a Costas beacon behaves like any other).
- A transmission is planned before its slot starts. If the guards refuse it, the slot is received instead and the reason is shown.

### 6.3 Power

- **RF power:** while the beacon runs, Costas sets the radio's RF power over CAT through `RadioPowerService`, default 5 % of the radio's maximum in watts (at least 1 W), and restores the previous setting when the beacon stops (**VERIFY** V-WSPR7: `rigctld` `RFPOWER` per rig, and the Flex).
- **Audio attenuation:** a setting from 0 to -30 dB on top of the RF power (owner decision 2026-11-26, widened from -20 so a 5 W radio reaches 5 mW). WSPR has a constant envelope, so a lower drive level lowers output roughly in proportion without distortion, taking the output below the radio's minimum. The estimate assumes 0 dB already gives the full set power (ALC just starting to act); at very low drive some radios stop scaling in proportion, so below about -25 dB an external attenuator or a dummy-load check is more trustworthy than the estimate.
- **Reported power:** the operator picks the dBm value sent in the message from the legal list. Costas shows its estimate (RF power and attenuation, as "Radio 5 W − 20 dB ≈ 50 mW (17.0 dBm)") and the radio's meter reading beside it and warns when either differs from the chosen value by more than 3 dB. It never changes the chosen value (rule 4). Meters read poorly at low power, so the estimate is advice only.
- **Meter floor:** some radios' power meters cannot read below 5 W (Hamlib's FT-710 table starts there, so anything from nothing to 5 W reads 5 W; `TxMeterCheck.MeterFloorWatts`). A reading at or below the floor is not shown as dBm and not compared with the reported power; the power line says "Meter can't read below 5 W; using the radio setting and attenuation", and in WSPR the bottom-bar meter strip shows the estimate beside it ("≤5 W · est. 50 mW").

### 6.4 Encoding

- `WsprEncoder` (Core) packs types 1, 2 and 3, convolutionally encodes, interleaves and combines with the sync vector. The waveform is continuous-phase 4-FSK at the operator's audio offset within the 1400 to 1600 Hz window.
- A message that cannot be sent exactly as shown (callsign or grid that does not fit the type, a dBm value not on the list) is refused with the reason, never altered (rule 4).
- Type 2 or 3 messages alternate the way WSJT-X does for compound calls and 6-character grids (**VERIFY** with V-WSPR6).

### 6.5 Safety guards

All of these are part of the feature (rule 5), checked before every transmission:

- Band edge and licence class (`FrequencyTable.TransmitAllowed`) over the whole 200 Hz window, not just my tone.
- Clock: no transmission when the clock is off by more than 2 s.
- PTT released on any rig or audio error, and on Halt Tx.
- A rig fault, audio fault or refusal stops the beacon; it does not retry by itself.
- The transmitter refuses WSPR audio longer than one transmission (`WsprTiming.TxLength` plus 1 s).

### 6.6 Leaving WSPR

Changing mode or band by hand while the beacon runs asks first ("Stop the beacon and switch to FT8?"), then stops the beacon. This matches U2.

## 7. Band hopping (owner request 2026-11-26)

- The operator picks a list of bands. Costas changes band between slots, optionally on WSJT-X's coordinated schedule so hopping stations meet on the same band (**VERIFY** V-WSPR6).
- **After a hop:** the first slot on the new band is received only, so the antenna tuner can settle before anything is transmitted (owner decision 2026-11-26).
- The presence rule (6.1) applies on every band. Hops are not operator input.
- A hop to a band where the guards refuse transmit (licence class, band limits) receives only on that band and says so.

## 8. Screen

- Picking WSPR in the Mode menu replaces Operate with the WSPR view: Start/Stop beacon, Tx percentage, RF power, attenuation, reported dBm with the estimate and warning, countdown to the next slot and whether it transmits, the band hop list, heard spots (time, call, grid, SNR, drift, distance in the operator's unit), WSPRnet upload status, and Heard by.
- Phone remote: beacon state, next slot, Halt Tx, Start/Stop beacon and "I'm here". Settings stay desk-only.
- A mockup is drawn and approved before the view is built.

## 9. Build order

Step-by-step directions, files, traps and stop-and-ask points: `10-wspr-build.md`.

| Step | Build | Accept |
| --- | --- | --- |
| W0 | Resolve V-WSPR1 to V-WSPR8; a source for reference vectors and WSJT-X's WSPR samples to `samples/wspr/`; `Mode.Wspr` and `ModeInfo` (not yet operable); FT helpers refuse WSPR | Automated: every existing test passes unchanged; `ModeInfo` tests for WSPR |
| W1 | 2-minute recording, `WsprdDecoder`, parser, `wspr_spot` table, simulation; WSPR becomes operable for receive | Automated: golden output for every sample; parser table tests; a hung `wsprd` is killed and reported |
| W2 | WSPRnet uploader and fake server; Heard by | Automated: queue survives restart, backs off, app works with WSPRnet down. Manual: my spots appear on wsprnet.org |
| W3 | `WsprEncoder` and waveform | Automated: at least 40 reference vectors match symbol for symbol; rendered audio decodes through `wsprd` to the same message |
| W4 | Beacon scheduler, presence rule, power and attenuation, guards | Automated with `SimulatedRig` and a manual clock: PTT lead and tail, release on fault, refusals off-band and with a 3 s clock error, presence timeout stops after the current transmission, presence time never under 2 min, nothing keys after a restart. Manual: my beacon is heard on WSPRnet |
| W5 | WSPR view and phone remote | Automated: view-model tests. Manual: matches the approved mockup |
| W6 | Band hopping | Automated: hop schedule tests with a manual clock; first slot after a hop never transmits. Manual: an evening of hopping across three bands |

Status (2026-11-26, `drt/wspr-plan`): W0 built. V-WSPR1 to V-WSPR8 are in `08-open-questions.md` Findings; `Mode.Wspr`, `ModeInfo.IsKeyboard`/`IsWspr`/`HasSlots` and `WsprTiming` are in Core; WSPR is not yet operable. Owner decisions taken on the way: WSJT-X's slot table (6.2), HTTPS upload (5), the 200 Hz guard window (3).

Status (W1, 2026-11-26): receive is built. WSPR is in the Mode menu (`ModeInfo.Operable`), with dial frequencies for 160 m to 6 m (not 60 m: WSJT-X has none) in `frequencies.json`; `Session.OnWsprSlotEvent` records the 2-minute slot, hands it to `WsprdDecoder` at the 114.048 s cut-off, parses it with `WsprSpotParser`, files the signals in `wspr_spot` (migration 008) and shows them in `SessionSnapshot.Wspr`; FT8, FT4, macro and Auto-call commands refuse with "Not in WSPR mode."; `--simulate` replays `samples/wspr/`. The WSPR screen itself is W5: until then WSPR shows the ordinary Operate view.

## Build decisions for review

Decisions taken while building without the owner, to be confirmed or changed:

- **Owner decision 2026-11-26 · Start beacon transmits the next slot.** The first slot after Start beacon transmits whatever the Tx percentage says; the duty cycle applies from the slot after. The rules that always win still apply first (receive after a band hop, no operator input, a guard refusal), and a receive-only slot leaves the first transmission for the next slot.
- **Owner decision 2026-11-26 · The beacon is one-shot, with no presence timeout.** Start beacon sends one transmission in the next slot and the beacon then stops, putting the radio's power back; there is no "I'm here" and no timeout, since a timeout between Start and the end of the transmission could cut it off. Each transmission is an operator action. The continuous engine (Tx percentage, hopping) is still in the code behind `Session.BeaconOneShot`, which only tests turn off; the Tx percentage and hop settings do nothing in the app until the owner decides what to do with them.

- **W1 · A WSPR clock mark after a switch.** The clock changes mode at a slot boundary, so `Session` ignores a WSPR mark when the session is no longer in WSPR (and the reverse). Switching from WSPR to a keyboard mode moves the clock to FT8 timing, since WSPR's 2-minute marks make no sense for PSK or RTTY transmit.
- **W1 · Unresolved hashed calls** (`<...>`) are kept as spots with the call `<...>`, so the count and the signal show; they are not uploaded in W2.
- **W1 · Stored spots** are kept 30 days unless waiting for upload; nothing calls the prune yet.
- **W1 · No 60 m WSPR row** in `frequencies.json`; 6 m uses 50.293 MHz (WSJT-X lists it for regions 2 and 3).

Status (W2, 2026-11-26): the upload is built and tested against a fake server, and not yet against wsprnet.org. `WsprnetClient` posts one spot per request over HTTPS in WSJT-X's field set; `WsprnetUploader` works from the `wspr_spot` table (so the queue survives a restart), backs off 1, 2, 4 ... 30 minutes, marks a refused or day-old spot failed, and sets the WSPRnet indicator; `WsprnetService` runs it a random 5 to 25 s apart, only when the profile's `Wspr.UploadEnabled` is on and never in simulation. Until the W5 Settings section exists the switch is `Profile.Wspr.UploadEnabled` in `settings.json`.

- **W2 · Decision:** no `wsprstat` (no-spot status) post is sent; WSJT-X sends it after a slot with no spots and while transmitting to report the Tx percentage. Spots do not need it. Revisit with W4, when the beacon runs.
- **W2 · Decision:** spots whose call did not resolve (`<...>`) are stored but never queued for upload.
- **W2 · Decision:** a pending spot more than 24 hours old is marked failed rather than sent.
- **W2 · Owner to check by hand:** the first real upload over HTTPS answers with text containing `spot(s) added`. A different reply marks every spot failed (as WSJT-X drops its queue), so check it with one spot before relying on it, and say if WSPRnet needs plain HTTP.

Status (W3, 2026-11-26): the encoder is built. `WsprPacker` (a strict port of `wsprsim`'s packing), `WsprHash`, `WsprConvolutional`, `WsprInterleaver`, `WsprSync`, `WsprEncoder` and `WsprWaveform` are in Core. 68 vectors from `wsprsim -cd` (`Vectors/wspr.tsv`, 43 type 1, 16 type 2, 9 type 3, every legal power, the edge grids AA00 and RR99) match symbol for symbol, and 13 messages rendered to audio decode through the real `wsprd` to the same text, the hashed ones resolved by our hash.

- **W3 · Decision:** the audio offset is the **centre** of the four tones, as WSJT-X's Tx frequency is (it sends tone 0 at `offset - 1.5 × 1.4648` Hz), not the lowest tone as `10-wspr-build.md` said; so a Costas beacon reports on the same frequency as WSJT-X's at the same setting. `WsprWaveform.ToneHz` states it.
- **W3 · Decision:** `WsprPacker` refuses what `wsprsim` packs without checking: a callsign without the digit in the right place or with a digit after it, a two-digit suffix below 10 or a two-letter suffix (`K1ABC/QR` is in the vectors as a refusal), a prefix of more than 3 characters, a type 3 message with a 4-character grid, any power not on the list, lower-case. It never pads or trims.
- **W3 · Decision:** a prefix whose number is exactly 32768 (`NYN/...`) is packed with `>=` where `wsprsim` has `>`, which makes it unreadable; ours round-trips through `wsprd` (and so has no `wsprsim` vector).
- **W3 · Decision:** no unpack self-check inside `WsprEncoder` (as `FtxEncoder` has). The strict packer, the vectors and the `wsprd` round trip stand in for it.
- **W3 · Decision:** the waveform ramps over 20 ms at each end (a raised cosine). WSJT-X's own ramp was not measured.

Status (W4, 2026-11-26): the beacon is built and tested on a simulated radio with a manual clock and a null audio output; it has not been on the air. `Session.StartBeaconAsync` (the only way it starts), `StopBeacon`, `NoteWsprPresence`; `WsprPresence`, `WsprTxSchedule` (WSJT-X's table), `WsprBeaconScheduler`, `WsprMessageCycle` in Core; `Transmitter.PrepareWspr` runs the same guards as `Prepare` (one shared `GuardRefusal`) with the band edge and licence over the whole 1400 to 1600 Hz window; `BeaconPowerService` sets 5 % of the radio's power (at least 1 W) and puts it back; automatic transmit level is paused in WSPR. There is no screen for it until W5.

- **W4 · Decision:** a slot is planned at the cut-off of the slot before (114 s in, six seconds ahead), on its own task. A transmission has always finished by then (it ends 111.6 s in), so a beacon that stops for want of the operator never cuts one off. Starting the beacon mid-slot plans the next slot at once if more than 2 s remain.
- **W4 · Decision:** presence is also checked when a slot starts, so an operator who left between the plan and the slot start stops it before PTT.
- **W4 · Decision:** **Stop beacon** lets a transmission already on the air finish (as the presence stop does); Halt Tx cuts it and also stops the beacon. Any rig or audio fault, a Halt, a manual band or mode change, and a dial move of more than 3 kHz stop the beacon; none restarts it.
- **W4 · Decision:** the clock guard uses the SNTP offset only. The median DT of FT decodes does not exist in WSPR, so with no SNTP reading the clock is unknown and transmitting is allowed, as it is for FT8 before a median is known.
- **W4 · Decision:** Tune is refused while the beacon runs ("Tune is only available while nothing else is being sent").
- **W4 · Decision:** a compound call with a 4-character locator cannot send WSJT-X's alternate type 3 message (`wsprsim` and `wsprd` need 6 characters), so Start beacon refuses it and says to set a 6-character locator. A 6-character locator on a plain call alternates type 1 and type 3 as WSJT-X does, unless "prefer type 1" is set.
- **W4 · Decision:** the defaults are 20 % Tx, 5 % power and a reported 37 dBm (5 W on a 100 W radio). The screen warns when the radio's power and the attenuation, or its meter, differ from the reported dBm by more than 3 dB; the reported value is never changed.
- **W4 · Decision:** the radio's old power is put back once nothing is transmitting; if a transmission is still on the air when the beacon stops, it waits up to 3 minutes for it.

Status (W5, 2026-11-26): the screen and the phone card are built (`WsprViewModel`, `WsprView.axaml`, `RemoteWspr`, the three phone commands, `mockups/wspr/index.html`, `02-ui-spec.md`).

- **W5 · Decision (needs the owner):** the screen was built **without waiting for the mockup to be approved**, from a mockup drawn in the app's own styles. Look at it and say what to change.
- **W5 · Decision:** the beacon's settings (share, reported dBm, radio power, attenuation, offset, type 1, WSPRnet) are on the WSPR screen, not in a Settings window section. Everything they hold is a per-profile `WsprOptions`; moving them is a view change only.
- **W5 · Decision:** the question before leaving WSPR or changing band is an inline bar on the WSPR screen (the menu snaps back to what is running meanwhile), not a dialog. A band or mode change from the phone does not ask (U2, which asks for the other modes, is not built yet); it stops the beacon because `Session.SetBand` does.
- **W5 · Decision:** the phone's `wsprStart` and `wsprHere` follow D25 as the other transmit commands do (they refuse until the user has pressed Operate as on their own profile); `wsprStop` and Halt work for anyone.
- **W5 · Not built:** "Heard by" (V-WSPR5), listed as optional in the plan and display only. The query is in `08-open-questions.md` when wanted.

Status (W6, 2026-11-26): band hopping is built, on a simulated radio and a manual clock. `WsprHopSchedule` (Core), `WsprOptions.HopBands` / `HopDwellSlots` / `HopCoordinated`, `Session.HopBand` (the app's band switch), and the hop settings on the WSPR screen.

- **W6 · Decision (needs the owner):** hopping needs a **dwell**. Owner decision 2026-11-26 says the first slot on a new band only receives; with a hop every 2 minutes (WSJT-X's coordinated schedule) that is every slot, so a coordinated beacon would never transmit. `HopDwellSlots` (default 5, 1 to 100) stays on each band of the operator's list for that many slots, so all but the first can transmit. The **coordinated** option is built as the plan describes (the band for each slot is the one WSJT-X's schedule names, among the operator's bands, falling back to the list walk) and, under the receive-only-after-a-hop rule, it only receives. It is kept so the schedule is there; say whether to drop it, or to let a hop that lands on the band the radio was just on count as no hop.
- **W6 · Decision:** hopping happens only while the beacon runs, on the operator's own list, never on its own: Start beacon is still the only thing that starts it, the presence rule still applies on every band (a hop is not input), and a manual band or mode change still stops it.
- **W6 · Decision:** the band changes at the cut-off of the slot before (114 s in), right after that slot's audio has gone to the decoder, through the same band switch the menu uses (it tunes the radio and clears the band's station list). The slot just recorded keeps its spots (its audio is complete and was recorded on the old band); the first slot on the new band is receive-only.
- **W6 · Decision:** a band whose guards refuse transmit (licence class, band edges) is receive-only with the reason shown ("Receive only on 20 m: ..."); the beacon carries on. Any other refusal (the clock, the audio, a radio fault) still stops it.
- **W6 · Decision:** Start beacon refuses a hop list with a band that has no WSPR frequency in the table (60 m has none) and names it, and a dwell outside 1 to 100; nothing is dropped silently.
- **W6 · Decision:** if the radio does not arrive on the band, or the band switch throws, the beacon stops with the reason.

## Owner's checklist: what only you can check

Nothing on this list has been done. Everything built so far ran on a simulated radio, a manual clock and a null audio output, except decoding, which ran through the real `wsprd`.

- [ ] **Look at the screen** (W5): WSPR mode on a real run; compare with `mockups/wspr/index.html`; say what to change. It was built before the mockup was approved.
- [ ] **W2, WSPRnet over HTTPS:** turn on "Report what I hear to WSPRnet" with a real receiver; check spots appear under your callsign on wsprnet.org, and that the Heard indicator reads "WSPRnet: N spots sent". If every spot fails, WSPRnet may need plain HTTP (the reply must contain `spot(s) added`).
- [ ] **W4, power:** with the radio on a dummy load, start the beacon: the radio's power should drop to 5 % of its maximum (at least 1 W) and come back when you stop it. Try the FT-710 and the Flex (V-WSPR7 only tried the dummy rig). Check what the radio and the meter say against the reported dBm.
- [ ] **W4, on the air (dummy load first):** the beacon keys 0.8 s into an even-minute slot, sends for 110.6 s, and releases; Halt Tx cuts it; a pulled USB cable or audio device stops it. Then on an antenna: a spot of yours on wsprnet.org or wspr.live.
- [ ] **W4, presence:** leave the desk: the beacon stops after the Tx watchdog minutes (at least 2); tap I'm here on the phone and see it carry on.
- [ ] **W6, an evening of hopping across three bands:** a list such as `20m, 40m, 30m`; check the radio and tuner follow, the first slot on each band only receives, and a band your licence does not allow only receives.
- [ ] **Decisions above that change what goes on the air:** WSJT-X's slot table (D29, done as you chose), the 20 ms ramp, the centred audio offset, the dwell for hopping.

- **W5 (added later) · Phone mode picker:** the phone's FT8 / FT4 buttons gain **WSPR**, so the phone can move the station into WSPR mode. While the beacon runs the phone refuses a band or mode change ("Stop the beacon first: changing band or mode ends it."), which replaces the earlier decision that a phone change stops it without asking.
