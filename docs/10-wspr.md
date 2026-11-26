# 10 · WSPR mode

Owner decision D29 (2026-11-26): WSPR joins the Mode menu as a beacon mode. It receives and reports what it hears to WSPRnet, and transmits a beacon on a schedule while the operator is present. Nothing here is built yet. Items marked **VERIFY** are listed in `08-open-questions.md` (V-WSPR1 to V-WSPR8) and must be confirmed before the code that depends on them.

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

- `WsprnetUploader` (Integrations), off by default, per station profile. It uploads my receive spots in the form WSJT-X uses (**VERIFY** V-WSPR4: endpoint, fields, batch size, limits).
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

- **Duty cycle:** a Tx percentage, default 20 %, so about one slot in five transmits. Which slots are picked: **VERIFY** V-WSPR6 (follow WSJT-X's method so a Costas beacon behaves like any other).
- A transmission is planned before its slot starts. If the guards refuse it, the slot is received instead and the reason is shown.

### 6.3 Power

- **RF power:** while the beacon runs, Costas sets the radio's RF power over CAT through `RadioPowerService`, default 5 % of the radio's maximum in watts (at least 1 W), and restores the previous setting when the beacon stops (**VERIFY** V-WSPR7: `rigctld` `RFPOWER` per rig, and the Flex).
- **Audio attenuation:** a setting from 0 to -20 dB on top of the RF power. WSPR has a constant envelope, so a lower drive level lowers output roughly in proportion without distortion, taking the output below the radio's minimum.
- **Reported power:** the operator picks the dBm value sent in the message from the legal list. Costas shows its estimate (RF power and attenuation) and the radio's meter reading beside it and warns when either differs from the chosen value by more than 3 dB. It never changes the chosen value (rule 4). Meters read poorly at low power, so the estimate is advice only.

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
