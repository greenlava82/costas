# 05 · Integrations

Each section says what is confirmed from a primary source and what is marked **VERIFY** (recalled, not yet confirmed). Confirm VERIFY items before coding against them and record the result in `08-open-questions.md`.

## 1. Decoder: WSJT-X `jt9`

Source: https://github.com/WSJTX/wsjtx (`lib/jt9.f90`). Baseline version WSJT-X 3.0.2.

### Running it (file mode, v1)

```
jt9 -8 -d 3 -L 200 -H 3000 -c W7LIT -G DN40 -x ZL2RPA -g RF70 -Q 3 \
    -e <bin dir> -a <data dir> -t <temp dir> <YYMMDD_HHMMSS.wav>
```

Options confirmed from the source option table:

| Option | Meaning |
| --- | --- |
| `-8` / `-5` | FT8 mode / FT4 mode |
| `-d DEPTH` | Decoding depth 1 to 3. Use 3 |
| `-L HERTZ`, `-H HERTZ` | Lowest and highest frequency decoded |
| `-f HERTZ` | Receive frequency offset (default 1500). Set to the DX station's offset during a contact |
| `-c CALL`, `-G GRID` | My call and grid (enables a-priori decoding) |
| `-x CALL`, `-g GRID` | DX call and grid, when in a contact |
| `-Q PROGRESS` | QSO progress 0 to 5, for a-priori decoding |
| `-p SECONDS` | T/R period |
| `-e PATH`, `-a PATH`, `-t PATH` | Executable, writable data and temp paths |
| `-M`, `-N THREADS` | Multithreaded FT8 decoder and its thread count (0 to 12) |
| `-m THREADS` | FFT threads |
| `-q` | Quiet: no `<DecodeFinished>` line |
| `-s KEY` | Shared-memory mode: stays running and takes its input from a `QSharedMemory` segment (see below) |

Positional arguments are WAV files: `jt9 [OPTIONS] file1 [file2 ...]`.

### Input

- WAV, 12,000 Hz, 16-bit signed, mono. FT8: 180,000 samples (15 s). FT4: 90,000 samples (7.5 s). Zero-pad after the capture cut-off.
- File name `YYMMDD_HHMMSS.wav` in UTC, the slot start. `jt9` takes the decode time from the name.
- Run with the working directory set to a per-run temp folder; `jt9` writes scratch files there.

### Output

- **VERIFY** the line format by running `jt9` on the sample files and saving the output as golden files. Expected for FT8: `HHMMSS SNR DT FREQ ~  MESSAGE [flags]`, for example `025345  -8  0.1 1234 ~  CQ JA1QRS PM95`. FT4 uses `+` in place of `~`. A trailing `?` marks low confidence; `a1` to `a7` mark a-priori decodes; the jt9 from WSJT-X 3.x also prints `a8` (seen in the operator's output, 2026-10-11), so the parser accepts `a` with any number.
- The run ends with `<DecodeFinished>` followed by three integers (confirmed: `format('<DecodeFinished>',2i4,i9)`).
- Parse with a tolerant regular expression anchored on the separator character, not fixed columns.

### Details to settle in Milestone 0

- **VERIFY** the correct `-p` value and sample count for FT4.
- Hashed callsigns (`<...>`) do not resolve across separate `jt9` runs (V3); shared-memory mode keeps them.
- **VERIFY** which files `jt9.exe` needs beside it (FFTW, Fortran runtime DLLs). During development, run it in place from an installed WSJT-X (`C:\WSJT\wsjtx\bin\jt9.exe`, path configurable). For release, bundle that set under `third_party/wsjtx/` with the GPLv3 licence text and a link to the exact source.
- Measure decode time on the busiest sample. If it exceeds 1.2 s, try `-M -N 4`.

### Running it (shared-memory mode)

Confirmed from WSJT-X 2.7.0 and 3.0.2 (`main.cpp`, `widgets/mainwindow.cpp`, `lib/jt9a.f90`, `lib/shmem.cpp`, `commons.h`) and run against both `jt9` builds:

```
jt9 -s <key> -w 1 -m <FFT threads> -e <bin dir> -a <data dir> -t <temp dir>
```

- `jt9` attaches to the `QSharedMemory` with that key; the creator (Costas) makes it first. Qt's native name is `qipc_sharedmemory_` + the key's ASCII letters + the SHA-1 of the key in lower-case hex. On Windows that is the name of a page-file mapping (`OpenFileMapping`); on Linux it is a file in Qt's temp folder (`TMPDIR`), and the segment is System V, keyed by `ftok(file, 'Q')`. If it cannot attach, `jt9` aborts.
- The segment holds `dec_data`: `int ipc[3]`, spectra (`ss`, `savg`, `sred`), the audio `short d2[1800*12000]` at byte offset 5,075,032, then `params` at 48,275,032 (236 bytes in 2.7, 344 in 3.0, which appends the multithreaded decoder's fields). Size 48,275,376 serves both. WSJT-X 3.2 and later change the layout entirely (a new IPC header), so only 2.7.x and 3.0.x are fed this way; the version is read from `wsjtx_app_version -v` beside `jt9`.
- Handshake: write `params` and the audio, then set `ipc[1]` = 1. `jt9` clears it, decodes, prints its decodes and `<DecodeFinished>`, then waits for `ipc[2]` = 1. `ipc[1]` = 999 makes it exit. `jt9` takes Qt's lock on the segment around these checks; Costas does not need it, because it writes `ipc[1]` last and never while a request is running.
- FT8 is decoded in three requests, as WSJT-X does: `nzhsym` 41 with the first 41 × 3456 samples, 47 with 47 × 3456, and 50 with all of them, `ndiskdat` false. The 50 pass relies on the 41 and 47 passes having filled its buffers; with `ndiskdat` true, `jt9` runs all three itself on one request (as for a file), which is what Costas sends when there was no early pass. The early pass stops early when the PC clock says the slot is more than 13.4 s in.
- Hashed calls resolve across requests (`<PJ4/K1ABC> W7LIT DN40` after `CQ PJ4/K1ABC`), and the DX call in `params` is added to the table, so the other station's hashed call resolves even if its full call was never heard.
- SuperFox: in Hound mode (`nexp_decode` 7) with `b_superfox` set, the Fox's (even) slots are decoded only for SuperFox at `nfqso` 750 Hz, `ntol` 100. WSJT-X 3.0.2 prints the Fox's messages as normal decode lines and a `$VERIFY$ <fox call> <code>` line; the 2.7.0-rc3 Ubuntu build printed nothing for the same `sfoxsim` file. Checked again 2026-11-14 with `jt9` and `sfoxsim` built from the `wsjtx-2.7.0` tag: `sfoxsim 750 0.0 MM K1JT 2 2 1 0 1 -5` decodes through Costas's shared-memory request in about 1.2 s, with or without a DX call, as `W0AAA K1JT RR73`, `W5FFF K1JT -18`, `CQ K1JT` (no grid) and `$VERIFY$ K1JT 000000`, all at 750 Hz. That recording is `tests/Ft8Client.Decoding.Tests/Data/superfox/000000_000000.wav` (kept out of `samples/`, whose files are replayed in simulation and need an ordinary golden decode). The code on the `$VERIFY$` line is not checked by `jt9`: WSJT-X's `Network/FoxVerifier` looks it up online.

## SuperFox signature check (9dx.cc)

- Confirmed from the WSJT-X 2.7.0 source (`Network/FoxVerifier.cpp`, `widgets/mainwindow.cpp`) and one live request on 2026-11-15: `GET https://www.9dx.cc/check/<fox call, percent-encoded>/<slot time yyyy-MM-ddTHH:mm:ssZ>/<code>.text` returns `200 text/plain` with one line `<time> <call> <code> VERIFIED` or `... INVALID` (a made-up code for K1JT gave `INVALID`). WSJT-X sends its User-Agent and allows 5 s.
- Costas sends the Fox's call, the slot time and the code, with its usual User-Agent. No limit is published; Costas sends at most one request per Fox per minute while unanswered and one per 10 minutes after an answer.

### Licensing

`jt9` is GPLv3. This app is GPLv3. Credit the WSJT Development Group in About. Their request to derivative works: do not ship features based on WSJT-X release candidates before the matching general-availability release. Track GA releases only.

## 2. Rig control: Hamlib `rigctld`

Hamlib: https://hamlib.github.io/ . Bundle the Windows release under `third_party/hamlib/`.

- Model list: run `rigctl -l` once, parse model number, manufacturer, model name, cache it for the searchable list.
- Start: `rigctld -m <model> -r <port> -s <baud> -t 4532` plus, for a separate PTT line, `-p <port> -P RTS|DTR`. Bind to 127.0.0.1 only. Pick a free port if 4532 is taken.
- Client: TCP, line based. Send a command and newline; read the reply.

| Purpose | Send | Reply |
| --- | --- | --- |
| Get frequency | `f` | `14074000` |
| Set frequency | `F 14074000` | `RPRT 0` |
| Get mode | `m` | mode line, passband line |
| Set mode | `M PKTUSB 0` | `RPRT 0` (fall back to `USB` if the radio rejects `PKTUSB`) |
| Get PTT | `t` | `0` or `1` |
| Set PTT | `T 1` / `T 0` | `RPRT 0` |
| Get split | `s` | split line, Tx VFO line |
| Set split | `S 1 VFOB` | `RPRT 0` |
| Set split Tx frequency | `I 14075000` | `RPRT 0` |
| Get ALC / SWR / power out meter | `l ALC` / `l SWR` / `l RFPOWER_METER_WATTS` | echo line, the value on a line of its own (`0.25`, `1.66`, `24.5`), `RPRT 0`; `RPRT -N` when not supported |

A reply of `RPRT -N` (negative) is an error. **VERIFY** exact reply shapes against the bundled version with `rigctld -m 1` (the dummy rig), which is also used in integration tests.

- Poll frequency and mode every 1 s when idle; do not poll during a transmission. During a transmission only the ALC, SWR and power out meters are read, on a second connection (`04-domain-logic.md` section 11).
- Port auto-scan (setup step 2): for each serial port and the model's common baud rates, start `rigctld`, send `f`, accept the first that returns a plausible frequency. Time out at 2 s per attempt.
- VOX / audio-only mode: no `rigctld`; band is chosen by the operator; PTT calls are no-ops.
- Split: default off in v1 (transmit offset is used as is). A setting enables "Fake it" (shift the dial during Tx to keep Tx audio between 1500 and 2000 Hz) once basic operation is proven.

## 2a. FlexRadio amplifier and tuner (SmartSDR TCP API)

Customer request 2026-11-18 (FLEX-8600 with a 4O3A PowerGenius XL amplifier and TunerGenius XL tuner): Operate and Standby in Costas, as SmartSDR's own amplifier and tuner panels have. Hamlib has no amplifier or tuner commands, so Costas opens its own connection to the radio's API, beside Hamlib's (`FlexAccessoryClient` in `Ft8Client.Rig/Flex`, `FlexAccessoryService` in the app).

- **When:** when the profile has a FlexRadio address (`RigSettings.FlexAddress`, Settings › Station › FlexRadio amp and tuner: `host` or `host:port`), whatever the radio model; owner decision 2026-11-18, so the usual TS-2000-through-SmartSDR-CAT setup keeps its power control and meters and still gets the amp. With the box empty, a radio set up as one of Hamlib's SmartSDR Slice A to H models (23005..23012) uses its own address (`host:4992`, `08-open-questions.md` FlexRadio in Hamlib). An address that is not one (a COM port, a URL, a bad port) is refused with a line saying so. Never while replaying recordings (D9).
- **Wire:** TCP to the address's port (4992 when none is given). Commands `C<seq>|<command>` and a new line; replies `R<seq>|<hex code>|<message>`, code 0 is success; status `S<handle>|<object> key=value …` (SmartSDR API wiki, "SmartSDR TCPIP API"). On connecting Costas sends only `sub amplifier all`, then `ping` every 5 s; a ping not answered in 5 s ends the connection. It does not send `client gui`, `client program` (the wiki says not to) or `keepalive enable`. Reconnect after 2 s, doubling to a minute.
- **Status:** both devices report as `amplifier <handle>` objects; the TunerGenius XL is told apart by `model=TunerGeniusXL` (FlexLib `Radio.cs`, `ParseAmplifierStatus`). A line may carry only what changed, so keys are kept per handle; `removed` drops the device. A device is shown once its model is known.
  - Amplifier: `state=` POWERUP, SELFCHECK, STANDBY, IDLE, TRANSMIT_A, TRANSMIT_B or FAULT (FlexLib `Amplifier.cs`). Operate is any of these but STANDBY, as FlexLib reads it; Costas shows POWERUP, SELFCHECK and unknown values as starting, not operate.
  - Tuner: `operate=0|1`, `bypass=0|1`, `tuning=0|1` (FlexLib `Tuner.cs`). Bypass only means something in operate.
- **Commands** (only these three, only on the operator's click):

| Purpose | Send | Source |
| --- | --- | --- |
| Amplifier to operate or standby | `amplifier set <handle> operate=1` / `operate=0` | SmartSDR API wiki, "TCPIP amplifier", SET; FlexLib `Amplifier.cs` |
| Tuner to operate or standby | `tgxl set handle=<handle> mode=1` / `mode=0` | FlexLib `Tuner.cs` (`IsOperate`); not in the wiki |
| Tuner bypassed or in line | `tgxl set handle=<handle> bypass=1` / `bypass=0` | FlexLib `Tuner.cs` (`IsBypass`); not in the wiki |

- **Safety:** nothing is switched on start-up, on connecting, on a band or profile change, or ever without a click. A change is refused while anything is on the air (PTT, a transmission or a tune), while the amplifier reports TRANSMIT_A or TRANSMIT_B, and while the tuner is tuning; operate is refused while the amplifier is still starting. A command that gets no answer is not retried (it may have been carried out); the operator sees "The radio did not answer." and the next status line shows what happened. The amplifier's own keying and protection stay with the radio's interlock; losing this connection does not stop a transmission, since the radio, not Costas, keys the amplifier.
- **Visible status:** a line under the address box in Settings (connecting; connected and what was found; connected but nothing paired; unreachable, with what to check), and Diagnostics lists the connection as `accessories`. The bottom bar shows the devices only while the radio reports them.
- **Log:** the radio's version line and the first 40 `amplifier` status lines after each connection are logged as received ("FlexRadio said: …"), along with every connection change and every command with its result, so a diagnostics bundle from a real station answers V-FLX2.
- **Trying it without hardware:** `dotnet run --project tools/Ft8Client.FakeFlex` pretends to be a radio with a PGXL in standby and a TGXL in operate on `127.0.0.1:4992`; type `127.0.0.1` in Settings. It carries out operate, mode and bypass commands and reports them back as a radio is expected to, prints every command, and has keys for transmitting, a fault, tuning, removal and a dropped connection. The tests use the same stand-in.

**VERIFY** on a real FLEX-8600 with a PGXL and TGXL (`08-open-questions.md` V-FLX2): the status lines, that the radio takes these commands from a client that is not a GUI client, and what it does when operate is switched during a transmission.

## 3. QRZ Logbook API

Source: https://www.qrz.com/docs/logbook/QRZLogbookAPI.html (confirmed).

- Endpoint: `POST https://logbook.qrz.com/api`, form-encoded name=value pairs in and out.
- Every request has `KEY` (the per-logbook API key) and `ACTION`. Unknown parameters are rejected.
- User-Agent is required, at most 128 characters: `Costas/<version> (<callsign>)`. Generic agents may be rate limited.
- INSERT, DELETE, STATUS and FETCH are marked subscription-required by QRZ.
- A logbook serves exactly one callsign; `W7LIT` and `W7LIT/P` are different logbooks. Store the key per station profile.

| Action | Request | Response |
| --- | --- | --- |
| `STATUS` | — | `RESULT=OK`, `DATA=` name=value pairs (total QSOs, confirmed, DXCC count, start and end dates, book name). Use for the key test |
| `INSERT` | `ADIF=<one record>`; optional `OPTION=REPLACE` | `RESULT=OK&LOGID=n&COUNT=1`, or `RESULT=FAIL&REASON=...`, or `RESULT=REPLACE` |
| `FETCH` | `OPTION=` comma-separated `name:value` pairs: `ALL`, `MODSINCE:YYYY-MM-DD`, `AFTERLOGID:n`, `MAX:n`, `BAND:20m`, `MODE:FT8`, `CALL:x`, `BETWEEN:d1+d2`, `TYPE:ADIF\|LOGIDS`, `STATUS:CONFIRMED\|ALL` | `RESULT=OK`, `COUNT`, `ADIF=` records |
| `DELETE` | `LOGIDS=1,2,3` | `RESULT=OK\|PARTIAL\|FAIL`. Permanent |

`RESULT=AUTH` means the key lacks privileges (no subscription, or wrong key).

### Sync down

1. First sync: `FETCH` with `OPTION=MAX:250,AFTERLOGID:0`; repeat with `AFTERLOGID` = highest `app_qrzlog_logid` received + 1 until fewer than 250 come back. Show a progress count.
2. Later syncs (at start, every 15 minutes, and on demand): `FETCH` with `MODSINCE:<last sync date minus one day>` and the same paging, to pick up edits and new confirmations.
3. Upsert into the local `qso` table keyed by `qrz_logid`.
4. Rebuild `LogIndex` after a sync that changed anything.

**VERIFY** on a real fetch: how the `ADIF` value is encoded in the response (it may be HTML-entity encoded, `&lt;` for `<`), and which fields carry confirmation (`app_qrzlog_status`, `qsl_rcvd`, `lotw_qsl_rcvd`). Treat a contact as confirmed if any of them says so.

### Upload

- On contact completion, write to the local log first, then enqueue an `INSERT`.
- Minimum fields: `call`, `qso_date`, `time_on`, `band`, `mode`, `station_callsign`. Send all fields in `06-data-model.md`.
- Never send `OPTION=REPLACE` automatically; it can overwrite confirmed contacts.
- `RESULT=FAIL` with a duplicate reason counts as uploaded; fetch its logid on the next sync.
- QRZ rejects contacts outside the logbook's configured date range; surface that reason verbatim.
- Queue survives restarts; retry with back-off; at most one request per second.

## 4. QRZ XML lookup

Source: https://www.qrz.com/XML/current_spec.html (confirmed).

- Log in: `GET https://xmldata.qrz.com/xml/current/?username=<u>;password=<p>;agent=Costas<version>` → `<QRZDatabase><Session><Key>…</Key></Session></QRZDatabase>`. Cache the key; do not log in per lookup.
- Look up: `GET https://xmldata.qrz.com/xml/current/?s=<key>;callsign=<call>` → `<Callsign>` with `call`, `fname`, `name`, `addr2` (city), `state`, `country`, `grid`, `lat`, `lon`, `dxcc`, and more.
- A `<Session><Error>` element or a missing `<Key>` means the session expired or the call was not found; re-login once on session errors. Only a "not found" error is a miss: any other error (a lookup limit, a lapsed subscription) is a failed lookup, never cached, and a refused login is an authentication failure.
- Full data needs a QRZ XML subscription; without one, fields are limited.
- Look up the stations on screen, not every decode: the current contact and stations calling me first (and the selected station), then Next in line and Watching. Each call is asked about at most once per run, one request at a time and at most one a second; a failed lookup is not retried for 10 minutes and pauses the queue 30 s (QRZ status Degraded). A refused login stops lookups with those credentials (QRZ status Down) until the username or password changes, a Test in Settings passes, or an hour has gone by. Cache results in `callsign_cache` (hits 30 days, misses 7), so a later run reads them from there and does not ask QRZ again. Strip `/P`-style suffixes for the lookup if the full call is not found. (Owner request 2026-10-11: city and state in Where for the whole list, not only the selected station.)
- Fields read: `call`, `fname`, `name`, `nickname`, `addr2`, `state`, `county`, `country`, `grid`, `lat`, `lon`, `dxcc`, `class`, `TimeZone`, `GMTOffset`, `DST`, `image`, `qslmgr`, `lotw`, `eqsl`, `mqsl`, `cqzone`, `ituzone` (names checked against the current spec, 2026-10-11). The spec documents `lotw`/`eqsl`/`mqsl` as `0/1` but its sample shows `Y`; both are accepted. The street address, email and birth year are not read.
- Every station heard is first filled in from `callsign_cache` (a local read); only calls with no fresh cache entry are queued for QRZ.
- This uses the QRZ username and password, which are separate from the logbook key. Both optional.

**Why two QRZ credentials.** QRZ runs two separate services with separate sign-ins, and neither accepts the other's:

| Credential | Service | What the app does with it |
| --- | --- | --- |
| Logbook API key (per logbook, from the logbook's settings page) | Logbook API, `logbook.qrz.com/api` | Reads the operator's own logbook (worked and confirmed, for need tiers and log totals) and uploads new contacts. It identifies one logbook, not a QRZ user, and cannot look up other callsigns |
| QRZ username and password | XML callsign data, `xmldata.qrz.com` | Logs in for a session key, then looks up other stations: name, city, state, grid. The contact subline, the station details strip, the US-state column and the name, QTH and state fields of logged contacts come from here. QRZ only answers lookups for a signed-in user; a QRZ XML subscription gives the full record, a free account gets fewer fields |

Either can be left out. Without the API key there is no worked-before data or upload; without the login, names and locations stay blank and stations are placed by the country file alone. Lookups are on exactly when both a username and a password are stored.

## 5. PSK Reporter: who hears me (live feed)

Source: https://www.mqtt.pskreporter.info/ (confirmed). A best-effort community service run by M0LTE on top of PSK Reporter. Treat as optional.

- Broker `mqtt.pskreporter.info`. Ports: 1883 TCP, 1884 TLS, 1885 WebSockets, 1886 WebSockets with TLS. Use 1884. No account.
- Topic: `pskr/filter/v2/{band}/{mode}/{tx_call}/{rx_call}/{tx_grid}/{rx_grid}/{tx_dxcc}/{rx_dxcc}`. Wildcards `+` (one level) and `#` (rest).
- Subscribe to `pskr/filter/v2/+/+/<MYCALL>/#`.
- Payload JSON: `sq` sequence, `f` frequency Hz, `md` mode, `rp` report dB, `t` epoch seconds, `sc`/`rc` sender and receiver calls, `sl`/`rl` locators, `sa`/`ra` ADIF DXCC codes, `b` band.
- **VERIFY** how a callsign containing `/` appears in the topic.
- Neighbour comparison (`04-domain-logic.md` section 13, optional, on by default): subscribe at start-up using the current grid in Settings, without waiting for my signal to be reported or persisting my country. `NeighbourGrids.Covering` covers the 300-mile circle with four-character sender squares, including squares across field and country boundaries and the date line. For each square subscribe to `pskr/filter/v2/<band>/<mode>/+/+/<square>/#`; all sender countries are allowed. The topic's four-character grid and the separate full locator in the payload are confirmed from [M0LTE's feed consumer](https://github.com/M0LTE/pskrmqtt2db/blob/4df4a930b929d0421faf4c34b6da4ef912ae992c/pskrmqtt2db/Services/PskrFeedService.cs) (checked 2026-10-20). Reports more than 300 miles from the configured grid are dropped on arrival; the rest are kept in memory for 30 minutes. Grid changes replace the subscriptions and clear old-area reports and samples. If more than 6,000 arrive in a minute the client stops following them for the session (reset when the grid changes). **VERIFY** the rate on a busy band (V17).
- Band openings (`04-domain-logic.md` section 14, optional, on by default): the same square subscriptions with `+` for the band, `pskr/filter/v2/+/<mode>/+/+/<square>/#`, serve both features. If more than 6,000 arrive in a minute the client falls back to the current band's square subscriptions, starting a fresh count, and samples the other bands in turn using the same squares, one band at a time for 30 seconds or 1,000 reports, at most one sample started per 30 seconds. No valid grid: no area subscriptions, and rows ask for the grid in Settings. **VERIFY** the all-band rate (V18).
- Reconnect with back-off. Store each report in `spot` (dedupe on receiver, band, 5-minute bucket; keep the strongest). Prune after 24 hours.

### Fallback and history: query service

Confirmed live: `GET https://retrieve.pskreporter.info/query?senderCallsign=<MYCALL>&flowStartSeconds=-3600&rronly=1&appcontact=<email>` returns XML rooted at `<receptionReports>` with `<lastSequenceNumber>` and `<maxFlowStartSeconds>` and zero or more `<receptionReport>` elements.

- **VERIFY** the `receptionReport` attribute names (expected: `receiverCallsign`, `receiverLocator`, `senderCallsign`, `senderLocator`, `frequency`, `flowStartSeconds`, `mode`, `sNR`, `receiverDXCC`).
- Use it once at start (to fill the last hour) and every 5 minutes only while the live feed is down. Never more often than once per 5 minutes; pass `lastseqno` from the previous answer to get only new reports. **VERIFY** the published limit.

## 5a. Reverse Beacon Network: who hears me (skimmers)

Source: https://reversebeacon.net/pages/Telnet+servers+30 and https://reversebeacon.net/pages/FT8+Announcement+40 (confirmed 2026-10-31). Optional, on by default (Settings › PSK Reporter and RBN). RBN covers RTTY much better than PSK Reporter, and its FT8/FT4 skimmers run around the clock.

- Telnet relays on `telnet.reversebeacon.net`: port 7000 carries CW and RTTY spots, port 7001 FT8 (and FT4) spots. The relays are "stripped down" for throughput and have no filters, so the client reads everything and keeps only lines naming my callsign. RBN may turn the FT8 stream off on big CW/RTTY contest weekends.
- Login is a callsign prompt (`Please enter your call:`, no new line); the client answers with my callsign (and does so after 5 s if no prompt came). Telnet negotiation bytes are skipped and nothing is agreed to.
- Spot line, DX-cluster style: `DX de HB9DCO-#:  14011.9  UP2L           CW    17 dB  35 WPM  CQ      1102Z`. Frequency in kHz, SNR in dB, time `HHMMZ` (no date; a time more than 5 minutes ahead is yesterday's). A second skimmer at a station is `W3LPL-2-#`. **VERIFY** the FT8/FT4 line on port 7001 (V-RBN1); the parser accepts the same shape with or without the speed field.
- Only spots of modes Costas sends are used (FT8, FT4, RTTY, PSK31, PSK63); CW is dropped. Each becomes a reception report like a PSK Reporter one, stored in `spot` with source `rbn`, so a skimmer that reports to both networks in the same 5-minute bucket on the same band counts once (the stronger report is kept).
- RBN gives no locator. The receiver's grid comes from what is already on disk: a fresh cached QRZ lookup, or the latest stored PSK Reporter report of that receiver. Otherwise the maps place it at its country's position. No lookups are made for skimmers.
- The connection stays open like a cluster node's (the relays are not made for clients that connect every few minutes). A silent connection is dropped after 5 minutes. After a failure: 30 s back-off doubling to 10 minutes, reset once a connection lasted 2 minutes. With no network the connection simply fails and backs off; the RBN indicator says so.
- Sending Costas's own decodes to RBN as a skimmer is not done: RBN takes spots only from its Aggregator program, not from clients.

## 5b. DX cluster spots (display only)

Owner request (item C4 in `09-next-up.md`). Sources: The DXSpider User Manual v1.51, the CC User Manual (ZL2iFB, v1.3 draft, February 2026) and lines captured from a DXSpider node (V-DXC1 in `08-open-questions.md`). Optional and **off by default**: Settings › Who to call › DX cluster, with the node's host and port (default 7300). No node is built in; Settings offers a few well-known public nodes as suggestions (from the CC User manual's list), none endorsed, and suggests the operator's club node first.

- **Connection.** One telnet connection to the chosen node, kept open (`ClusterFeedClient`). The login is the operator's callsign, sent when the node's prompt arrives (DXSpider `login: `, others `Please enter your call:`; the text ends without a new line) or after 5 s with no prompt. Telnet negotiation is skipped and nothing is agreed to. Costas then only reads: it sends no spots, announcements, filters or other commands, and `bye` when it stops (the DXSpider manual asks for a clean logout, or the node may keep the old session and refuse the next connect). A node that asks for a password is not supported: the status says so and Costas waits an hour before trying again. **VERIFY** the CC Cluster and AR-Cluster prompts (V-DXC2).
- **Spot line**, real-time format: `DX de DL1NCH:      3573.0 OZ8BLR       FT8 -05dB from JO46 814Hz      2012Z`. Spotter (a skimmer's `-#` dropped), frequency in kHz, spotted call, an optional comment, time `HHMMZ` (no date; a time more than 5 minutes ahead is yesterday's), and with DXSpider's `set/dxgrid` a locator after the time (the spotter's; not used). Fields are found by spacing, since long calls push the columns over. `SHOW/DX` output, announcements, WWV and talk lines are ignored. A spotted call that is not a valid call is dropped.
- **Storage.** Each spot goes into the `spot` table with source `cluster`: `my_call` is the station spotted, `rx_call` the spotter, `mode` the mode its comment named (or empty), no SNR. The table's usual rules apply: one row per spotter, band and station in a 5-minute bucket, pruned after 24 hours. On start the last 30 minutes are reloaded (their comments are not kept). Reach, the Hears you column and the PSK Reporter start-up load skip these rows: they are not reports of my signal.
- **Shown.** In memory one spot per base call and band, the newest (`ClusterSpots`), for 30 minutes. A heard station with a spot on the band being worked gets *Spotted 4 min ago* under Why in Next in line, with *Spotted 4 min ago on 20 m by DL1ABC · FT8 -12dB* as the tooltip; its details give that line for its newest spot on any band. The Awards view lists *Spotted on the DX cluster*: needed stations, up to 12, new countries first, then newest.
- **Needed** (`ClusterNeeds`): a country not in the log (new DXCC), or not in it on the spot's band (new band DXCC), counting only confirmed contacts when the ranking setting says so. New calls and grids are left out: almost every spot would be a new call, and a spot carries no grid of the station.
- **Mode filter** (`ClusterModeFilter`), since cluster spots have no mode field: the first mode word in the comment decides (`FT8`, `FT4`, `PSK31`/`BPSK31`, `PSK63`, `PSK` for either PSK speed, `RTTY`; `CW`, `SSB`, `USB`, `LSB`, `FM`, `AM`, `JT65`, `JS8`, `Q65`, `MSK144`, `WSPR`, `FST4`, `SSTV`, `OLIVIA`, `MFSK` and the like mean another mode). With no mode word, FT8, FT4, PSK31 and PSK63 spots count when their frequency is from 500 Hz below to 3.5 kHz above one of that mode's dial frequencies in the frequency table (human spots give the dial, skimmers dial plus audio offset). RTTY shares its segments with too much else, so an RTTY spot must say RTTY.
- **Display only.** Spots never change the ranking, the tiers, who is called, Auto-call or the rig frequency. The only action is the Awards row's **Go to 15 m** button, an explicit click that goes through the same band change as the Hunt alert's Go to (refused while transmitting, only to a band in the band list, the band's dial frequency, not the spot's).
- **Status.** The `cluster` service: Off ("DX cluster off", or why it cannot start: no node, no callsign), Degraded while connecting or after a drop, Ok when logged in, Down when unreachable or asking for a password. The bottom bar shows a *DX cluster* indicator only once the cluster is turned on. After a failure: 30 s back-off doubling to 10 minutes, reset once a connection lasted 2 minutes; a silent connection is dropped after 15 minutes. With no network the connection fails, backs off and the indicator says so; nothing else changes.
- **Etiquette and limits.** No published rate limits for users were found. Clusters are run by volunteers on a best-effort basis: one connection only, never a reconnect loop, no polling with `SHOW/DX`, and never sending spots on the operator's behalf. Nodes identify users by callsign without authentication, so Costas logs in only with the operator's own call. Which spots arrive (skimmer FT8 spots in particular) depends on the operator's settings stored on the node; Costas does not change them (owner question 7).
- **Simulation.** With `--simulate`, no node is contacted: the spots are `samples/dxcluster/spots.txt`, re-timed so the newest is a minute old, and nothing is stored.

## 6. PSK Reporter: uploading my spots

Source: https://pskreporter.info/pskdev.html (confirmed rules below).

- UDP to `report.pskreporter.info` port 4739, IPFIX (RFC 5101) packets.
- Send at most one packet every five minutes unless a packet fills up. Do not align sends to the clock; add random jitter. Use one UDP source port for the app's lifetime.
- Send the record-format descriptors (templates) with the first three packets after start and then at least hourly.
- Report each callsign at most once per five minutes, ideally once per hour unless something changed (band).
- A spot needs at least the sender callsign; send frequency, SNR, mode, sender locator, information source and time as well. The receiver record carries my callsign, locator and software name and version.

**VERIFY** the exact template layout before coding. Read the IPFIX section of pskdev.html and the WSJT-X reference implementation (`Network/PSKReporter.cpp` in the WSJT-X source, GPLv3, may be ported). Expected: enterprise number 30351; fields senderCallsign (.1), receiverCallsign (.2), senderLocator (.3), receiverLocator (.4), frequency (.5), sNR (.6), iMD (.7), decoderSoftware (.8), antennaInformation (.9), mode (.10), informationSource (.11), plus standard `flowStartSeconds` (150); variable-length strings with a one-byte length prefix; each set padded to 4 bytes.

Spot only decodes with a resolved callsign that are not low confidence. Never spot my own call. Off by default until the operator turns it on in setup step 6 or Settings.

## 7. WSJT-X UDP protocol

Source: `Network/NetworkMessage.hpp` in the WSJT-X source (confirmed). Lets GridTracker, JTAlert, N1MM+ and loggers work unchanged.

- UDP to a configurable address, default `127.0.0.1:2237`. Unicast or multicast.
- Every datagram: `quint32` magic `0xadbccbda`, `quint32` schema (send 2), `quint32` message type, then `utf8` Id (a unique name, `Costas`), then the fields.
- Encoding is Qt `QDataStream`, big-endian. `utf8` = `quint32` byte length then bytes; length `0xffffffff` = null. `bool` = one byte. `QTime` = `quint32` milliseconds since midnight. Floating point = 64-bit IEEE double. **VERIFY** `QDateTime` (expected: `qint64` Julian day, `quint32` ms since midnight, `quint8` timespec, 1 = UTC).

| Type | Name | Fields after Id, in order |
| --- | --- | --- |
| 0 | Heartbeat | max schema (quint32) = 3, version (utf8), revision (utf8). Every 15 s |
| 1 | Status | dial frequency Hz (quint64), mode, DX call, report, Tx mode (utf8 each), Tx enabled, transmitting, decoding (bool each), Rx DF, Tx DF (quint32 each), DE call, DE grid, DX grid (utf8 each), Tx watchdog (bool), sub-mode (utf8), fast mode (bool), special operation mode (quint8) = 0, frequency tolerance (quint32), T/R period (quint32), configuration name (utf8), Tx message (utf8). On every change |
| 2 | Decode | new (bool), time (QTime), snr (qint32), delta time s (double), delta frequency Hz (quint32), mode (utf8, `~` for FT8, `+` for FT4), message (utf8), low confidence (bool), off air (bool) |
| 3 | Clear | (none outbound). On band change |
| 5 | QSO Logged | date-time off (QDateTime), DX call, DX grid (utf8), Tx frequency Hz (quint64), mode, report sent, report received, Tx power, comments, name (utf8 each), date-time on (QDateTime), operator call, my call, my grid, exchange sent, exchange received, ADIF propagation mode (utf8 each) |
| 6 | Close | (none). On exit |
| 12 | Logged ADIF | ADIF text (utf8): a header and one record |

Inbound (owner request 2026-10-12), on the same socket the datagrams are sent from, since WSJT-X servers reply to the client's source address. Only datagrams whose Id is ours are acted on; with a loopback target, only datagrams from this computer. Field layouts confirmed from WSJT-X `Network/NetworkMessage.hpp` (read 2026-10-12):

| Type | Name | Fields after the Id | Action |
| --- | --- | --- | --- |
| 4 | Reply | Time `QTime`, snr `qint32`, DT `double`, DF `quint32`, mode `utf8`, message `utf8`, low confidence `bool`, modifiers `quint8` | Like WSJT-X, only when it matches one of our recent decodes exactly (text and slot time): a CQ calls the sender, as a double-click in Raw decodes does; a message to me answers it. Anything else is ignored with a status line. Modifiers are ignored |
| 8 | Halt Tx | Auto Tx only `bool` | Halt Tx at once, either way |
| 13 | Highlight Callsign | Callsign `utf8`, background `QColor`, foreground `QColor`, highlight last `bool` | Colours the call's lines in Raw decodes (latest slot only when "last" is set). An invalid colour (spec 0) cancels it. At most 200 calls are kept |

`QColor` on the wire: `qint8` spec (0 invalid, 1 RGB), then alpha, red, green, blue and a pad as `quint16`. Other inbound types (Clear, Replay, Free Text, Location, Switch Configuration, Configure) are read and ignored.

Test by decoding our own datagrams with a small reader in the test project and, manually, against GridTracker.

## 7a. Club Log most-wanted list

`GET https://clublog.org/mostwanted.php?api=1` (confirmed 2026-10-12, no account or key) returns a JSON object of rank to ADIF DXCC code, both as strings: `{"1":"344","2":"123",...}`, about 340 entries. A saved answer is in `samples/clublog/mostwanted.json`. The app downloads it at start when the cached copy (`cache/clublog-mostwanted.json`) is missing or a week old, with the app's User-Agent and the usual timeout, and keeps using the cached copy when Club Log cannot be reached. An answer with fewer than 300 entries is ignored.

## 7a1. Club Log real-time upload

Confirmed 2026-11-15 from clublog.org's developer pages ("How to upload QSOs in real time", updated 2025-12-17; "API keys", updated 2025-01-14; "Excessive API usage"; "App passwords", updated 2026-11-07).

- `POST https://clublog.org/realtime.php`, `application/x-www-form-urlencoded`: `email` (the account's email, not a callsign), `password` (an Application Password, made under Settings › API, shown once), `callsign` (must belong to that email), `adif` (exactly one record ending `<EOR>`), `api` (the application's key).
- 200 means stored: "QSO OK", "QSO Duplicate" (treat as success) or "QSO Modified". 400 "QSO Rejected": bad record, not logged. 403: credentials or prerequisites refused; the client "must STOP sending real time requests immediately" or the address is firewalled. 500: retry later.
- Costas keeps a 403 stop in the station profile's settings (`ClubLog.Stop`: the refused email and callsign, the time and Club Log's message; nothing derived from the password), so a restart does not send again. It ends when the operator saves a different email, Application Password or callsign, or presses Resume in Settings. The Club Log indicator shows the stop while it lasts.
- Real time only: not for batches (over about 150 contacts use `putlogs.php`, not built). A throttle exists with no published number; Costas sends at most one every 2 s.
- API key: requested at https://clublog.org/requestapikey.php. Club Log deletes keys found published in a Git repository, so Costas's key is the `CLUBLOG_API_KEY` repository secret, passed to release and development builds only. A build without it shows Club Log as unavailable.
- **VERIFY** the exact 403 body for a wrong password and the live behaviour once the key exists; the delete endpoint is not used.

## 7b. LoTW (ARRL Logbook of the World)

Optional, off until the operator enters a LoTW login. The account (login, TQSL Station Location, upload on log) belongs to each station profile (`StationProfile.Lotw`), so another operator's profile downloads and signs with their own; the password is in the credential store per profile (`lotw-password:<profile>`). The TQSL path, the download interval and the LoTW user list are shared (`AppSettings.Lotw`). Settings schema 3 copied the account every profile had been sharing into each profile, and at start-up the shared password is copied to each profile without one and then deleted (`LegacyMigration.SplitLotwPassword`).

- **Confirmations.** `GET https://lotw.arrl.org/lotwuser/lotwreport.adi?login=…&password=…&qso_query=1&qso_qsl=yes&qso_qslsince=<last>&qso_qsldetail=yes&qso_withown=yes&qso_owncall=<call>` every 12 hours and on demand, 2-minute timeout. `qso_qslsince` is the `APP_LoTW_LASTQSL` of the previous answer, kept in `sync_state` under target `lotw`. A page without `<eoh>` is a failed login or query. Each record with `QSL_RCVD=Y` marks the matching contact (call, band, mode, within 30 minutes) `lotw_confirmed`, or is added with source `lotw`. The URL carries the password and is never logged. FT4: VERIFY V20.
- **Uploads.** Through TQSL, which holds the operator's certificate: `tqsl -x -d -u -a compliant [-l <station location>] <file.adi>` on an ADIF file of contacts not yet uploaded, 5-minute timeout, process tree killed on timeout. Exit codes 0, 8 (all duplicates) and 9 (some duplicates) mark the contacts `uploaded`; any other code is shown and the contacts stay pending. Manual from the Log view, or after each logged contact when the operator turns that on.
- **User list.** `GET https://lotw.arrl.org/lotw-user-activity.csv` once a week, cached in `cache/lotw-user-activity.csv`. A call that uploaded within 365 days is a LoTW user (`LotwUsers`, Core). It feeds "Uses LoTW" in station details and the Why tooltip, and the ranking when only confirmed contacts count.

None of this runs in simulation. With LoTW unreachable the status says so and nothing else changes.

## 7c. POTA activator spots

`GET https://api.pota.app/spot/activator` (confirmed 2026-10-12, no account or key, served through CloudFront) returns a JSON array of the current activator spots, about 60 to 70 at a quiet hour. Fields seen: `spotId` (number), `activator` (call as spotted, may carry `/P` and the like), `frequency` (**kHz as a string**, `"14074"` or `"7191.0"`), `mode` (`FT8`, `CW`, `SSB`, `FM`, or empty), `reference` (`US-1234`), `parkName` (null in every entry seen), `name` (the park's name), `locationDesc` (`US-WI`), `spotTime` (ISO 8601 **without a zone**; UTC, checked against the response `Date` header), `spotter`, `comments`, `source` (`Web`, `RBN`, `GT`, ...), `invalid` (null), `grid4`, `grid6`, `latitude`, `longitude`, `count`, `expire` (seconds). The app uses `activator`, `frequency`, `mode`, `reference`, `name` (falling back to `parkName`), `locationDesc`, `spotTime`, and skips entries with `invalid` true or no call, reference or frequency.

- Optional, on by default (Settings › Who to call › Parks on the Air): the feed is public and read-only and the app sends nothing about the operator except the usual User-Agent.
- Polled every 2 minutes, only while the app runs and the setting is on; never more often (the response is a CDN-cached copy that changes about once a minute). On failure the wait doubles, up to 30 minutes. The last spots are shown for 15 minutes after the last good answer, then dropped. Status is the `pota` service: Ok, Degraded (showing the last spots), Down, or Off.
- Matching: a spot matches a heard station when the base calls are equal (`Callsign.BaseCall`, so K1ABC matches K1ABC/P) and the spot's frequency is on the band being worked; the newest spot wins. A station calling `CQ POTA` with no spot is shown as POTA without a park.
- Ranking (owner decision 2026-10-20, D16): with Settings › Who to call › "Rank stations at a park I have not worked higher" on (default), a spotted activator whose park is not in the log ranks right after the first tier and ahead of the rest, and stays in Next in line even when its call is worked on this band. A `CQ POTA` with no spot is not moved. The park never changes the need tier itself.
- Logged contacts with a matched park get ADIF `SIG=POTA`, `SIG_INFO=<reference>`, kept in the record's raw ADIF (no `qso` column) and copied by `AdifMapper.ToAdif` into QRZ uploads, exports and the UDP logged-ADIF message. Worked parks come from `SIG`/`SIG_INFO` and from ADIF 3.1.4 `POTA_REF` (comma list, `@location` suffix stripped).

## 7d. Announced DXpeditions (NG3K ADXO)

`GET https://www.ng3k.com/adxo.xml` (read 2026-11-07, no account or key) is an RSS 2.0 feed of NG3K's Announced DX Operations, about 65 items. Each `<item>` has a `<title>` like `Solomon Is: Oct 3-Nov 20, 2026 -- H44MS -- QSL via: LoTW` and a `<description>` whose lines are dates, entity, call, QSL and source, each ending ` --`, then a last line of notes (`By DL2GAC; 80-10m; mainly FT8, some SSB; ...`). Dates come as `Oct 3-Nov 20, 2026`, `Nov 3-16, 2026` or `Dec 12, 2026-Jan 2, 2027`. The listed call is sometimes only a prefix (`3B9`, `8P`); the notes then say what is signed (`as 3B9/M0CFW`, `using 3B9KW`). The feed carries Bill Feidt's copyright: Costas shows the listing with NG3K named as the source and does not republish it.

- Optional, on by default (Settings › Who to call › DXpeditions); sends nothing about the operator except the usual User-Agent. Owner request 2026-11-07 (from the Aurora comparison).
- Read at most every 12 hours; the last copy is kept in `cache/adxo.xml` and reused after a restart while it is younger than that. On failure the wait starts at 10 minutes and doubles up to 2 hours; an older copy stays in use (status Degraded). Status is the `dxpeditions` service: Ok, Degraded, Down or Off.
- Matching (`DxpeditionCalendar.Match`): on a listed day (the last day counts whole, UTC), a heard call matches when it is the listed call, a portable call with the listed call on either side of the slash, or a call the notes give after "as" or "using". A listed prefix alone never matches a plain call.
- Display only: *DXpedition · <entity>* under Why on station rows and in station details, and a list on the Awards view (on the air now, heard now first, then new countries; starting in the next 14 days). It never changes the ranking or who is called.

## 8. Country file

`cty.dat` from https://www.country-files.com/ . Ship a copy in the app; "Update country file" in Settings downloads the current one (**VERIFY** the stable download URL), validates that it parses and has more than 300 entities, then swaps it in.

## 9. Time check

SNTP over UDP port 123 to `pool.ntp.org` (configurable). Take 4 samples, use the one with the lowest round-trip delay. Run at start and hourly. Offline is not an error; fall back to the DT estimate.

## 10. Audio devices (Windows)

NAudio, WASAPI shared mode by default. Identify devices by their endpoint ID and show the friendly name. Capture at the device's mix format, convert to mono float, resample to 12 kHz with a windowed-sinc resampler. Never use the default device implicitly: if the configured device is missing, receive stops with a clear status and transmit is disabled. Watch for device removal and arrival and reopen automatically.

## 10. Update check

GitHub Releases for `somedudenamedtony/costas` (public). CI publishes a release when the CI workflow is run by hand on `main` (`workflow_dispatch`; pushes and merges only build and test), as release `v<major>.<minor>.<run number>` (three parts, as Velopack requires; builds before Velopack were `v0.1.0.<run>`, which compare older). Attached: Velopack's files (`CostasApp-win-Setup.exe`, `CostasApp-<version>-full.nupkg`, `CostasApp-<version>-delta.nupkg` from the previous release, and the feed `releases.win.json`, `assets.win.json`, `RELEASES`), the classic Inno Setup installer `Costas-Setup-<version>.exe` with its `.sha256` (for builds from before Velopack, which only look for that name), and the Linux preview `Costas-<version>-linux-x64.tar.gz` with its `.sha256`. On Linux the update bar's Update opens the release page instead of downloading anything.

Owner decision 2026-10-26: updates on Windows go through Velopack (https://velopack.io) so they download only what changed. The Velopack pack id is `CostasApp`, so it installs to `%LOCALAPPDATA%\CostasApp` (per user, no administrator rights); `%LOCALAPPDATA%\Costas` stays the data folder, which Velopack's uninstall must not touch.

- **Velopack install** (the normal case): `UpdateManager` with `GithubSource` reads the feed from the releases (app User-Agent on every request), downloads the deltas from the running version (or the full package when there is no delta chain), verifies them, and on Update calls `WaitExitThenApplyUpdates`: Velopack's updater waits for Costas to exit (PTT released, child processes stopped), swaps the files and starts it again. Refused while transmitting or in a contact.
- **Classic install** (Inno Setup, up to 0.1.0.407): the GitHub check below. When the release has `CostasApp-win-Setup.exe`, Update downloads and verifies that, and once Costas has shut down runs it with `--silent --log <logs>/update-<time>.log` through a hidden `cmd` that then starts the new Costas from `%LOCALAPPDATA%\CostasApp\current` (a silent Velopack install does not start it), or reopens the classic install if the installer fails. The new Costas removes the per-user classic install (its uninstaller, silently) and makes the Start menu shortcut again, since the uninstaller deletes the one with the same name. An all-users classic install is left (removing it needs administrator rights) and logged. A release without the Velopack installer is installed the classic way: the installer through a hidden `cmd` with `/SILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /LOG=<logs>/update-<time>.log`, which waits for Costas, jt9 and rigctld from the install folder to exit before copying, and Costas is started again whether or not the upgrade succeeded.
- `GET https://api.github.com/repos/somedudenamedtony/costas/releases/latest` with the app's User-Agent and `Accept: application/vnd.github+json`. Unauthenticated limit is 60 requests an hour per address; the app asks at start and then once a day. 404 (no release yet, or the repository is private) means "no update", not an error.
- The checksum comes from the asset's `digest` field (`sha256:<hex>`) when GitHub provides it, otherwise from the `.sha256` asset. With neither, the app refuses to install the download.
- Draft and pre-release releases are ignored. A release is offered only when its version is greater than the running build's, and not after the operator chose "Skip this version".


## 10a. Usage statistics (Aptabase)

Anonymous usage counts go to Aptabase (https://aptabase.com). Opt-out: on by default, switched off in Settings,
Privacy. Owner decision 2026-10-15.

- `POST https://{us|eu}.aptabase.com/api/v0/event`, header `App-Key: A-US-…` (the region comes from the key),
  JSON body `{ timestamp, sessionId, eventName, systemProps, props }` as the official SDKs send it. Aptabase refuses
  events older than a day; props are strings or numbers.
- The app key (`A-US-6947074211`; not a secret, it ships inside the app) is baked into release builds only: CI
  sets `APTABASE_APP_KEY` for the publish steps of a manual run on `main` (`AssemblyMetadata` `AptabaseAppKey`). Other builds have no key and send
  nothing. Nothing is sent when the `CI` environment variable is `true` (the installer test starts the app).
- User-Agent `Costas/<version>` without the callsign: these requests must not identify the station.
- No first-run notice (owner decision 2026-10-15): the switch lives only in Settings, Privacy. Nothing is sent when it
  is off, in simulation, or while the operating system
  reports no network. Events wait in memory (at most 500, under 23 hours old) and are sent once a minute; failures
  back off up to 30 minutes; whatever is waiting at exit gets one 3-second attempt and is then dropped.
- Events: `app_started` (settings snapshot, see `UsageEvents`), `app_running` (once a UTC day for long runs),
  `band_changed`, `view_opened`, `window_opened`, `cq_started`, `station_called`, `caller_answered`, `caller_dismissed`, `call_when_free`, `auto_call_started`, `tune`,
  `hunt_go`, `cluster_go`, `hunt_added`, `contact_logged`. Never a callsign, grid, name, frequency, port, address or log content.

## 10b. Problem reports (Formspree)

**☰ › Report a problem…** posts the operator's report to a form relay that emails it to the project, so no mail
password ships in the app. Today that is a Formspree form: `POST https://formspree.io/f/<id>` with
`Accept: application/json` and a JSON body of `message`, `_subject` and, only when the operator gives one, `email`
(Formspree makes it the reply-to). Any 2xx is success; 429 means the rate limit (20 posts a minute per form) or the
monthly quota (50 on the free plan) was hit. The endpoint (`FEEDBACK_URL`) and the email button's address (`FEEDBACK_EMAIL`) are set in `ci.yml`, overridable by
repository variables of the same name; local builds have neither and offer Copy only.

Privacy: nothing is sent without the operator pressing Send. The User-Agent leaves the callsign out (as for usage
statistics). Version and OS are ticked by default; the log tail (last 200 lines, at most 20,000 characters, secrets
removed) is off until ticked because it can hold callsigns and frequencies.

## 10c. Crash reports, logs and metrics (Sentry)

Unexpected errors, warning and error log lines, and the usage events go to Sentry (the `Sentry` NuGet package, `Ft8Client.Integrations.Crash.CrashReporter`). Owner
request 2026-11-07 (logs and metrics added the same day; Aptabase stays for now, owner may drop it if Sentry covers
it on the free plan); same rules as the usage statistics.

- The DSN is baked into release and development builds: CI sets `SENTRY_DSN` (the `costas` project in the `ember-ridge-development` Sentry org; a `SENTRY_DSN` repository
  variable overrides it) for the
  publish steps of a manual run on `main` and of a push to `main` (`AssemblyMetadata` `SentryDsn`). Not a secret: it ships inside the app and
  only accepts reports. Releases report under environment `production`, development builds (`UpdateChannel` `dev`)
  under `development` (since 2026-11-15, so a dev build can check reports work). Pull-request and local builds send
  nothing, nor when `CI` is `true`.
- Same switch as the statistics (Settings, Privacy, `Usage.Enabled`), read for every report; off at start means the
  SDK is not started and the offline cache (`<data>/crash-reports`, at most 30 envelopes) is deleted. Never in
  simulation.
- Captured: unhandled exceptions (AppDomain, unobserved tasks), UI-thread errors the app survives
  (`App.WatchForUnexpectedErrors`) and the fatal catch in `Program.Main`. No breadcrumbs (they would carry log lines),
  no tracing, no sessions, no client reports, no automatic logging or metrics integrations, `SendDefaultPii` off.
- Issues from the log (2026-11-15): most faults are caught and logged, and log lines never become Sentry issues, so
  the Issues page stayed empty while Logs filled up. `SentryLogSink` also calls `CrashReporter.CaptureLogged` for
  Error and up, and for warnings that carry an exception: an event with the exception (stack) and the scrubbed
  rendered line, fingerprinted by Sentry's default for exceptions and by the message template otherwise. Each
  template + exception type goes once per run, at most 25 per run (quota). Sentry's duplicate detection drops the
  second capture of the same exception object (`Log.Error(ex)` and then `CrashReporter.Capture(ex)`).
- Unhandled exceptions: `Program.LastChance` flushes for up to 3 s after capturing, because the process ends next.
- Test report: Settings, Privacy, **Send a test report** calls `CrashReporter.SendTestAsync`, which captures a
  `CrashReportTestException` (level info, tag `source=test`) under the same switch, scrubbing and offline rules.
- Line numbers: `DebugType` `embedded` (Directory.Build.props), so stack frames carry file and line on Windows and
  Linux (the Linux build deletes loose PDBs) without uploading symbols.
- Logs: `SentryLogSink` (Serilog, Warning and up) hands the rendered line plus exception type and message to
  `CrashReporter.Log`, which scrubs it and sends it as the parameter of a `{0}` template (no Serilog properties).
- Metrics: `UsageReporter` passes every allowed event to `CrashReporter.Count` (`mirror`), a Sentry counter with the
  same props, string values scrubbed. Sent even when the build has no Aptabase key.
- Sentry fills logs' `user.id` with its installation id, which is the same across runs; Costas sets a random scope
  user id at each start instead. `BeforeSendLog`/`BeforeSendMetric` drop everything while the switch is off.
- `BeforeSend` (`CrashReporter.Clean`) clears user, server name, request, device name and time zone and exception
  `Data`, and runs `CrashScrubber` over the message, exception values, extras, tags, thread names, frame paths and
  debug image paths: the operator's callsigns, grids, name and QTH from every profile, the OS user and machine name,
  the user folder, and anything shaped like a callsign (with a letter in the prefix, so `20m` stays), grid, frequency, serial port, IP, email or URL query value.
- Offline: an `INetworkStatusListener` on `NetworkInterface.GetIsNetworkAvailable` keeps Sentry's caching transport
  from trying while the OS reports no network.
- Sentry project settings (owner, in Sentry): Security & Privacy, "Prevent Storing of IP Addresses" on, and the
  server-side data scrubbers on.

## 11. eQSL

Optional uploads and incoming-card sync, configured per station profile. The account username and password
are kept in the secret store; QTH nickname and enable switches are non-secret profile settings.

- HTTPS POST `https://www.eqsl.cc/qslcard/ImportADIF.cfm`, form field `ADIFData`: ADIF header fields
  `EQSL_USER`, `EQSL_PSWD`, optional `EQSL_QTH_NICKNAME`, then EOH and one QSO. Positive single-record
  acknowledgments and explicit duplicate counts complete delivery; errors and unknown replies retry with
  persistent exponential backoff. At most one upload per second. QRZ and eQSL delivery fields are separate.
- HTTPS POST `https://www.eqsl.cc/qslcard/DownloadInBox.cfm`, fields `UserName`, `Password`, `QTHNickname`.
  Accept direct ADIF or follow a same-origin HTTPS ADIF download link under `/qslcard/`. Credentials never
  go in URLs; redirects are disabled. Replies are not logged or persisted as diagnostics.
- Full inbox sync hourly (five-minute retry after failure), so recently received cards for old QSOs are included.
  Store valid cards idempotently by profile/call/band/mode/UTC minute. Mark only unique local matches confirmed;
  keep unmatched cards in the inbox. Preserve eQSL confirmations across later QRZ refreshes.
- Incoming cards are available on the eQSL Settings page, including a manual sync/account test. Simulation
  disables both uploads and sync. Offline services never interrupt radio operation.

**VERIFY** against the primary eQSL API documentation and a redacted live account response before release:
ADIF upload acknowledgment/duplicate wording, QTH nickname form parameter, and inbox download-link format.
The implementation tests use synthetic replies. This workspace had no .NET SDK and an unreachable network
proxy, so neither a build nor live eQSL verification was possible in this session.

## 12. Wavelog and Cloudlog

Optional uploads to the operator's own Wavelog or Cloudlog site (owner request 2026-11-07, from the Aurora comparison), configured per station profile. The API key is in the secret store (`cloudlog-key:<profile>`); the address, station location id and switch are profile settings. Read from both projects' `application/controllers/Api.php` on 2026-11-07 (Wavelog is a Cloudlog fork with the same API).

- `POST {site}/index.php/api/qso`, JSON `{"key", "station_profile_id", "type": "adif", "string": "<one ADIF record>"}`. Answers: 201 `{"status": "created", ...}`; 400 `{"status": "abort", "messages": [...]}` when the import reported an error (Wavelog's duplicate check returns "... Duplicate for <call>", which Costas counts as already sent); 401 `{"status": "failed", "reason": "missing or wrong api key"}` or "station id does not belong to the API key owner." or "station callsign does not match station callsign in station profile."; 403 for a read-only key; Wavelog may answer 429 when rate limited.
- `GET {site}/index.php/api/station_info/{key}` lists the key owner's station locations (`station_id`, `station_profile_name`, `station_callsign`, `station_active`). Settings uses it for Test; it never sends a contact.
- The site address the operator types is reduced to its base (`/index.php` and a pasted `/api/qso` are removed; https is assumed without a scheme).
- Queue target `cloudlog`, at most one request a second, network failures retry with the usual back-off, 401/403/404 wait an hour. The key and request bodies are never logged; the status line strips the key from any reason quoted. Each contact keeps its delivery in `wavelog_upload_state` and `wavelog_upload_error` (migration 006): queued when queued, uploaded when accepted or when a download shows Wavelog has it, queued with the reason while a refusal waits. The Log view shows it in a Wavelog column, and its **Wavelog ▾** button downloads now or sends the waiting uploads now; both appear only for a profile with Wavelog/Cloudlog set up.
- **VERIFY** against a live Wavelog and Cloudlog install: the duplicate wording on Cloudlog, and that `index.php/api/...` works on sites with URL rewriting.

### Wavelog API v2 (owner request 2026-11-18, D27)

Read from docs.wavelog.org (API v2 overview, QSO, Station and Token pages) on 2026-11-18. v2 is only in Wavelog 3.1.0 and later. Its tokens start `wl2_`, are refused on the v1 endpoints, and v1 keys are refused on v2 (`401 invalid_token`), so the key picks the API (`CloudlogClient.ApiFor`): `wl2_` means v2, anything else v1. Cloudlog has only v1.

- Every call is `{site}/index.php/api/v2/<resource>` with `Authorization: Bearer wl2_…` and the usual User-Agent. The token is never in a body or a URL.
- Upload: `POST qso`, JSON `{"import_type": "adif", "station_profile_id": <number>, "adif": "<one ADIF record>"}`, the same record v1 sends. 201 `{"data": {"parsed", "imported", "skipped", "messages"}}`: imported, or skipped as a duplicate, counts as sent. The ADIF import is used rather than the single-QSO JSON create because the docs count duplicates there as skipped, while the JSON create's duplicate answer is not documented. A station id that is not a whole number is refused before sending.
- Test: `GET token` (any valid token) gives `{"data": {"name", "scopes": [...], "expires_at"}}`; Settings says whether the token can read (`qso:read`) and upload (`qso:write`) and until when, and warns when uploads are on without `qso:write`. Then `GET station` (scope `station:read`) gives `{"data": [{"id", "name", "callsign", "active", …}]}`; without that scope Test asks for the id by hand.
- Errors are `{"error": {"code", "message", "details"}}`. `401 invalid_token`, `401 token_expired`, `403 insufficient_scope` (the status names `details.required_scope`), `403 forbidden` and `404` wait an hour, like v1's key errors. `409 conflict` (a duplicate) counts as sent. `429 rate_limited` retries after `details.retry_after` seconds when that is longer than the usual back-off. Rate limits are off unless the site's owner turns them on.
- A pasted `…/api/v2/…` address is reduced to the site base like the v1 ones.
- **VERIFY** against a live Wavelog 3.1+ site: that one record sent twice comes back as `skipped` (or 409), and the `expires_at` time zone (shown as a date only). Uploads with a v2 token were tried by the owner on 2026-11-18 and work.

#### Download for worked-before (owner request 2026-11-18, D27)

A per-profile switch (`CloudlogOptions.DownloadForWorked`, off by default, v2 tokens only) brings the chosen station location's Wavelog log into the local log, so worked-before can come from Wavelog instead of, or as well as, QRZ (`WavelogSyncService`).

- `GET qso?format=adif&station_id=<id>&since_id=<n>&per_page=1000`, scope `qso:read`. The answer is `{"data": {"exported", "lastfetchedid", "adif"}, "meta": {"has_more"}}`, oldest first; nothing new is `exported: 0` and `adif: null`. While `has_more`, the next page asks from `lastfetchedid`, at most one request a second.
- The cursor (`lastfetchedid`) is kept in `sync_state` with the target `wavelog:<host><path>:<station id>`, per profile, and saved after every page: the first run reads the whole log, an interrupted one carries on, and a different site or station location starts over.
- Records go through `AdifMapper.ToQso` with source `wavelog` and merge by the usual duplicate rule in one transaction per page. A sync never brings back a contact the operator deleted, and an edited contact keeps its edits, as for QRZ and LoTW. A downloaded contact is never queued for upload; one that matches a local contact still waiting to upload to Wavelog takes that item off the queue.
- It runs from the Wavelog/Cloudlog worker: at start, at once when the profile, site, station, token or switch changes, then every 15 minutes (Wavelog asks for delta loads, not polling every minute). After a refusal it waits an hour, after a network fault 5 minutes, or longer if the site asked. A change to the log rebuilds worked-before (`LogChanged` → `RebuildLogIndex`). Simulation never downloads.
- **VERIFY** against a live site: which QSL fields Wavelog's ADIF export carries (`lotw_qsl_rcvd`, `qsl_rcvd`, `eqsl_qsl_rcvd`). Without them, Wavelog contacts count as worked but not confirmed.

