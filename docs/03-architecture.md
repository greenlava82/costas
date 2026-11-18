# 03 · Architecture

## Shape

A headless core owns audio, timing, the radio, station state and contact state. The UI renders that state and sends commands. Transmit timing never depends on the UI thread.

```
                +-------------------- App (Avalonia, MVVM) --------------------+
                |  views <- view models <- AppState (observable snapshots)     |
                +-------------------------------^------------------------------+
                                                | commands down, events up
+-----------------------------------------------+------------------------------------------+
|                                   Session (orchestrator)                                 |
|  SlotClock -> AudioCapture -> SlotRecorder -> DecoderHost(jt9) -> MessageParser          |
|                                                         |                                |
|                               StationTracker <----------+----> ContactEngine             |
|                                     |                               |                    |
|        NeedResolver(LogIndex) --> Ranker                   TxScheduler -> Encoder        |
|        HearsMe(PskFeed) -------/                                |          -> AudioPlayback
|                                                                 +-> RigService (rigctld) |
|  LogService(SQLite) <-> QrzLogbook      PskReporter(feed, query, upload)    UdpBroadcaster|
+------------------------------------------------------------------------------------------+
```

## Projects

| Project | Responsibility | May reference |
| --- | --- | --- |
| `Ft8Client.Core` | Messages, callsigns, grids, country file, station tracker, need tiers, ranking, contact engine, FT8/FT4 encoder and waveform maths. Pure logic | nothing |
| `Ft8Client.Audio` | `IAudioBackend`, WASAPI (Windows) and PortAudio (Linux) implementations, resampler, ring buffers, `SlotClock`, `SlotRecorder`, `TxPlayer` | Core |
| `Ft8Client.Decoding` | `IDecoder`, `Jt9Decoder` (process host), output parser, WAV writer | Core |
| `Ft8Client.Rig` | `IRig`, `RigctldRig` (process host and TCP client), `SimulatedRig`, serial port scan | Core |
| `Ft8Client.Data` | SQLite schema and migrations, repositories, ADIF reader and writer, settings, secret store, `LogIndex` | Core |
| `Ft8Client.Integrations` | QRZ logbook client, QRZ XML lookup, PSK Reporter feed, query and uploader, Reverse Beacon Network and DX cluster telnet clients, WSJT-X UDP broadcaster, SNTP check | Core, Data |
| `Ft8Client.App` | Avalonia views and view models, `Session` composition, simulation wiring | all |

## Key interfaces (in Core unless noted)

```csharp
public interface IDecoder {            // Decoding
    Task<DecodeResult> DecodeAsync(SlotAudio slot, DecodeContext ctx, CancellationToken ct);
}
public sealed record SlotAudio(DateTime SlotStartUtc, Mode Mode, short[] Samples12k);
public sealed record DecodeContext(string MyCall, string MyGrid, string? DxCall, string? DxGrid,
                                   int LowHz, int HighHz, int Depth, int QsoProgress);
public sealed record Decode(DateTime SlotStartUtc, int Snr, double Dt, int OffsetHz,
                            string Text, bool LowConfidence);

public interface IRig {                // Rig
    Task<RigState> GetStateAsync(CancellationToken ct);      // frequency, mode, ptt, split
    Task SetFrequencyAsync(long hz, CancellationToken ct);
    Task SetModeAsync(RigMode mode, CancellationToken ct);
    Task SetPttAsync(bool on, CancellationToken ct);
    Task SetSplitAsync(bool on, long txHz, CancellationToken ct);
    event EventHandler<RigFault> Faulted;
}

public interface IAudioBackend {       // Audio
    IReadOnlyList<AudioDevice> ListInputs();
    IReadOnlyList<AudioDevice> ListOutputs();
    IAudioInput OpenInput(string deviceId, int sampleRate);
    IAudioOutput OpenOutput(string deviceId, int sampleRate);
}

public interface IClock { DateTime UtcNow { get; } long Ticks { get; } }   // injectable for tests
```

`Session` exposes commands (`CallStation`, `CallCq`, `AnswerCaller`, `Resend`, `LogNow`, `Abandon`, `HaltTx`, `SetBand`, `SetMode`) and publishes immutable snapshots (`BandSnapshot`, `ContactSnapshot`, `SignalSnapshot`, `StatusSnapshot`) on change. View models subscribe and marshal to the UI thread.

## Slot pipeline

For FT8 (15 s slots; FT4 uses the same pipeline with 7.5 s):

| Time in slot | What happens |
| --- | --- |
| 0.0 s | `SlotClock` raises SlotStart. `SlotRecorder` starts a new 12 kHz buffer. If a transmission is scheduled for this slot, `TxScheduler` asserts PTT at 0.5 s minus the PTT lead (default 0.2 s) |
| 0.5 s | Tx audio starts (pre-rendered before the slot) |
| 13.14 s | Tx audio ends; PTT released after a 0.1 s tail |
| 11.85 s | FT8 only: early pass. `SlotRecorder.Snapshot` hands the first 11.8 s to the decoder, which keeps `jt9` running; most decodes arrive about 12.5 to 13.4 s into the slot and go to `ContactEngine` and the raw decodes |
| 13.6 s | Capture cut-off. The buffer is zero-padded to the full slot length and handed to `DecoderHost` |
| about 15.5 s | Decodes arrive (with the early pass, the rest of them). `MessageParser` → `StationTracker` → `ContactEngine` decides the next transmission → `Ranker` recomputes → snapshots published |
| 15.0 s | Next slot |

The decision for the next slot must be made before it starts. If decodes arrive late, the contact engine may still start a transmission up to 1.0 s late (configurable); later than that, it skips the slot. When the operator calls a station or starts CQ during a slot of the right parity, the transmission starts in that slot: up to 1.0 s after the Tx start the whole message goes out, shifted later; from then until the click start limit (default 3.0 s after the Tx start) it keeps its place in the slot and the part already past is left out, as WSJT-X does. Measured with `ft8sim` and `jt9`, a message missing its first 3 s still decodes at full sensitivity down to about -18 dB (8 in 10 at -20 dB, 1 in 10 at 4 s missing). Later than that, it goes out in the next slot of that parity. The click start limit is an operator setting (`Operating.CommandStartLimitMs`, 0 to 5 s; PR #150, owner sign-off 2026-11-14); set below the late-start limit, a click after it waits for the next slot. Decodes belonging to an earlier slot never trigger a transmission.

`Jt9PersistentDecoder` keeps `jt9` running between slots in shared-memory mode (`jt9 -s`), as WSJT-X does, so its hash table of nonstandard calls, its list of the last slot's messages (for `a7` decodes) and the signals found by the early pass carry over. For FT8 it runs WSJT-X's three passes: `nzhsym` 41 on the first 11.8 s (the early pass, at the `EarlyDecode` mark), 47 at the cut-off (subtracts what the early pass found) and 50 (decodes the rest). The full decode of a slot returns every decode of that slot, early ones included. Measured on a 4-core Linux VM with WSJT-X 3.0.2 on the busy samples: the early pass finds 14 to 20 of 21 decodes and finishes 12.8 to 13.4 s into the slot; the rest are done 1.8 to 1.9 s after the cut-off (single-threaded file mode: 3.7 to 3.9 s; file mode with `-M -N 4`: 2.3 to 2.4 s). The early pass's decodes go to the contact engine, the raw decodes and the station list at once, so Next in line shows them about 2 s before the full decode lands (PR #150, owner sign-off 2026-11-14); missed slots, the slot's counts and Who else is calling wait for the full decode, which adds the rest without reordering the list. When `jt9` cannot be fed this way (a version whose memory layout is unknown, macOS, or a `jt9` that never answers three times in a row), the decoder runs `jt9` once per slot in file mode instead, without the early pass.

Without the early pass the full decode runs `jt9` at depth 3, which takes 3 s or more on a busy band, so its results arrive after the next slot has begun. So that my next message is chosen on time, a quick decode runs alongside it while a contact is in progress (only the other station's frequency, ±100 Hz, at depth 3 with a-priori decoding, typically 0.2 to 0.3 s once `jt9` has its FFT wisdom; without `jt9_wisdom.dat` every run spends about 0.9 s planning FFTs first, so the decoder keeps the file in its temp root and copies it into each run's scratch folder) or while calling CQ (the whole band at depth 1, about 0.5 s). Its results go to the contact engine only; the station list, raw decodes and everything else wait for the full decode. Each message reaches the contact engine once, whichever decode finds it first, so a second copy is not taken for the other station repeating itself.

The quick decode has its own `jt9` process, also kept running and given the DX call, so it never waits behind the full decode and the other station's hashed call resolves in it too.

If the quick decode of the previous slot has not finished when my slot starts, the transmission is held until it has, up to the late-start limit, and then goes out with the message already prepared. The full decode is never waited for.

When transmitting in a slot, nothing is recorded or decoded for that slot.

## Threads

- **Audio threads** (WASAPI callbacks, PortAudio's blocking read and write threads on Linux): copy samples to or from a single-producer single-consumer ring buffer. No allocation, locks, logging or exceptions.
- **Slot thread**: one dedicated thread drives `SlotClock` with a high-resolution timer and schedules PTT and Tx start. It never waits on I/O.
- **Session work queue** (`WorkQueue`): one thread that runs, in order, what follows the start of a transmission or a slot: the log write, the journal and the snapshot rebuild. The slot thread only posts to it (`Session.Defer`), so a locked database or a slow disk cannot delay PTT release or the next slot's marks.
- **Worker tasks**: decoding, rig polling (1 s), QRZ, PSK Reporter, the RBN and DX cluster telnet connections, database writes. All async, all cancellable.
- **Log copy** (`LogCopyService`, a supervised loop on the thread pool): keeps the ADIF copy of the log in `backups\` current. `QsoRepository.Changed` fires after every contact write, on whatever thread wrote (usually the session work queue); its handler only records the time under a short lock and releases a semaphore, so it never waits on the disk. The loop rewrites the files once the log has been quiet for 5 s, at most 60 s after the first change (`Core.Time.Debounce`), and on a failure shows the `logcopy` service as Down in the bottom bar and tries again after 1 minute, doubling to 30.
- **UI thread**: rendering only. Snapshots are immutable, so no locks are shared with the core.

## Processes

| Process | Started | Supervised how |
| --- | --- | --- |
| `jt9` | Two kept running in shared-memory mode (full and quick decode), started at the first decode; file mode, once per slot, when shared memory cannot be used | Each request has a timeout of one slot length; on overrun or exit the process is killed and the slot decoded in file mode; the next request starts a new process after a pause of 1, 2, 4 … 30 s; asked to exit (`ipc[1]` = 999) and then killed on app exit |
| `rigctld` | At session start when a radio is configured | TCP health check each poll; restart with back-off 1, 2, 5, 10 s; on fault drop PTT via a fresh connection or serial line, stop Tx, raise `RigFault` |

All child processes are placed in a Windows job object so they die with the app.

## Simulation mode

Required from Milestone 0 so that everything can be built and tested without a radio. It is never on by default (decision D9): an installed app with no radio set up shows no data, says "No radio set up" and refuses to transmit. Simulation runs only with `--simulate` or in developer mode.

- Developer mode is hidden: click the product name in About seven times to toggle it. It adds a Developer section to Settings (replay the sample recordings, with or without the simulated partner) and lists Hamlib's test radios (Dummy, models 1 and 6) in Setup. With a test radio selected, transmit audio goes to a null output so a radio with VOX on cannot be keyed by a test.

- `--simulate <folder>` replaces the audio input with a player that feeds WAV files from the folder slot by slot, aligned to the real slot clock, looping.
- `SimulatedRig` implements `IRig` in memory and logs PTT transitions with timestamps.
- Audio output goes to a null sink that records what would have been sent, for assertions.
- `--simulate-partner` adds a scripted partner: when the app transmits to a station, the simulator injects that station's next message into the following slot (by synthesising it with the encoder and mixing it into the input), so full contacts can run end to end.
- A fake PSK Reporter feed and a fake QRZ server (in-process HTTP listener) are used in integration tests and can be enabled in simulation.
- DX cluster spots (`DxClusterService`) are replayed from `samples/dxcluster/spots.txt`, re-timed to now; no node is contacted and nothing is stored.

## Remote page

`App/Remote`: `RemoteControlService` runs Kestrel (ASP.NET Core) inside the app while Settings has it on. `GET /` serves one embedded HTML page, `POST /api/pair` hands out a device token with no code (owner decision 2026-10-25; the token only identifies the phone), and `/ws` carries `RemoteState` (built from `SessionSnapshot` by `RemoteStateBuilder`, reusing the Operate view models' wording) down and commands up. `RemoteControl` holds the rules (sign-in, `ControlLease`, commands mapped onto `IRemoteTarget`, which `Session` implements) and is tested without the network. Requests from outside private, link-local and CGNAT ranges get 403; WebSockets from another origin are refused. Token hashes go in the OS credential store. The lease check runs every 250 ms on its own task and calls `Session.HaltAsync(reason)` when the controlling phone is silent for 15 s.

It also serves `manifest.webmanifest` and three icons (`icon-180/192/512.png`, drawn by `scripts/make-icon.py`) as embedded resources, so the page can go on a home screen. There is no service worker: the page is plain HTTP, and service workers need a secure origin.

The page's extra data (log, QRZ lookups, country file, most-wanted list, bands) reaches `RemoteStateBuilder` through `RemoteContext`, which `AppHost` fills in. Band and Tx offset changes go through `IRemoteTuning` (`HostTuning` calls `AppHost.SetBandAsync` and `Session.SetTxOffset` and saves settings, as the desk does). Each phone's open station sheet is a view-only `show` command kept per device in `RemoteControl`; it does not take the control lease.

### Sign-in (optional)

Off by default (`RemoteOptions.RequireSignIn`, D25). When on, every device signs in with a username and password from `RemoteOptions.Users`, at home too, and `POST /api/pair` answers 403; paired phones' tokens stop counting, so they are cut off at the next broadcast. `GET /api/config` tells the page which to show. `POST /api/login {username, password}` returns `{token, username}`, 401 for a wrong name or password (an unknown name is checked against a decoy hash, so it takes as long), or 429 with `Retry-After` from `LoginThrottle` (5 free failures per username and per client address, then 30 s doubling to 15 min). `POST /api/logout {token}` ends one sign-in. The token goes in the WebSocket `hello` as before; the first message back is `{type: "user", username}`.

`RemoteUsers` keeps, per user id, the password hash (`PasswordHash`: PBKDF2-SHA256, 210,000 rounds, random salt) and the signed-in devices (`UserSessions`: SHA-256 of each token and the day it was last used; a sign-in unused for 30 days ends) in the OS credential store, never in `settings.json`. Changing a password, Remove and "Sign out everywhere" in Settings end that user's sign-ins; unticking Allowed refuses them without ending them. `RemoteControl.SignIn` returns a `RemoteIdentity` (device id, user id, username); the control lease still holds the device id.

Users are keyed by a random id, not the name, so a later station profile per user (another licensed operator working under their own callsign) can hang off `RemoteUser` and `RemoteIdentity.UserId` without moving any stored data. What that changes on the air needs owner decisions first (D25).

### Behind a reverse proxy

A request carrying `X-Forwarded-For`, `X-Forwarded-Proto`, `X-Forwarded-Host` or `Forwarded` counts as proxied (`ProxyHeaders`). The headers are believed only from this computer (loopback) or the address in `RemoteOptions.TrustedProxy` (Settings, Proxy address, for nginx on another computer); from any other device they could be made up, so they are ignored and the request is treated as coming straight from that device. When believed, the client address for the log and `LoginThrottle` is the last `X-Forwarded-For` entry (the one the nearest proxy added), and a WebSocket's `Origin` may match `X-Forwarded-Host` as well as `Host`.

The rules (`SignInPolicy`):

| Request | Sign-in off | Sign-in on |
|---|---|---|
| Phone on home Wi-Fi | Served (pairing) | Served; password refused: it would cross the Wi-Fi unencrypted |
| Tailscale (from or to a 100.64/10 address) | Served | Password accepted (WireGuard encrypts it) |
| This computer, no proxy headers | Served; Settings warns (a proxy here without its headers would let the internet in with no password) | Password refused: no telling how it got here |
| Believed proxy saying `https` | Refused (403, "turn on sign-in") | Password accepted |
| Believed proxy saying `http` or nothing | Refused | Password refused: needs https |
| Proxy headers from any other device | Refused | Headers ignored |

The page and the WebSocket are served wherever the request is; the password rule applies to `POST /api/login`. A token, once issued, is accepted on any route, so a phone that signed in over Tailscale keeps working on home Wi-Fi with Tailscale off (its token then crosses the Wi-Fi unencrypted, a known limit).

A minimal nginx site, with TLS set up as usual (for example by certbot):

```nginx
server {
    listen 443 ssl;
    server_name radio.example.org;
    # ssl_certificate ...; ssl_certificate_key ...;

    location / {
        proxy_pass http://127.0.0.1:8073;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;     # the /ws WebSocket
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
        proxy_read_timeout 60s;                     # the page pings every 5 s
    }
}
```

## Desktop notifications

`App/Services/Notifications`. `MainViewModel` turns a Hunt sighting (`HuntService.Alert`) and each new caller (`CallerAlert.Fresh`) into an `AttentionEvent` (`NotificationText`, Core) and hands it to `DesktopNotifications` on the UI thread. That asks `NotificationGate` (Core, unit tested: settings, foreground rule, 10-minute repeat quiet time, one popup per kind per minute with merging) and passes a `DesktopNotification` to the backend, an `IDesktopNotifier`. `Show` returns at once and does its work on the thread pool, so neither the UI thread nor the slot thread waits on it. `MainWindow` supplies the foreground test (main window active, not minimized, not in mini mode) and handles `Activated(call)`: out of mini mode, restore, activate, Operate tab, select the station. Nothing else happens on a click.

| Backend | Where | How |
| --- | --- | --- |
| `WindowsToastNotifier` | Windows 10 and later | `Windows.UI.Notifications` called directly through WinRT with .NET's source-generated COM interop (`[GeneratedComInterface]` declarations of the few interfaces used, in `Notifications/WinRt`), so the app keeps its `net10.0` target with no Windows SDK projection or Windows App SDK. The AppUserModelID is the one Velopack sets on the process (`velopack.<id>`, matching its shortcut); without one (classic Inno Setup install, development run) Costas sets `Costas.Costas`, which the Inno shortcuts carry, and registers its name and icon under `HKCU\Software\Classes\AppUserModelId\Costas.Costas`. Clicks arrive through the toast's Activated event; no COM activator is registered, so a toast clicked after Costas has closed does nothing |
| `FreedesktopNotifier` | Linux | `org.freedesktop.Notifications.Notify` on the session bus with Tmds.DBus.Protocol (already used by Avalonia; pinned to Avalonia's version), one notification per kind replaced through `replaces_id`, the `default` action only, `suppress-sound`; `ActionInvoked` maps the id back to the call. No bus or no server: logged once, then nothing |
| `NullDesktopNotifier` | elsewhere, tests, and until the app picks a backend | Nothing |

`App` picks the backend (`DesktopNotifications.PlatformBackend`) before the first window opens, because on Windows the process AppUserModelID must be set before then.

## Configuration and storage

Per-user data lives in `%LOCALAPPDATA%\Costas\` (on other platforms, the equivalent app-data folder): `costas.db`, `settings.json`, `logs\`, `wav\`, `cache\`, `backups\` (daily copies and the ADIF log copy). See `06-data-model.md`.

## Error handling

- Faults are values, not dialogs. Each service exposes a status (`Ok`, `Degraded`, `Down`, with a message) shown in the bottom-bar indicators and Diagnostics.
- Any fault in the rig or audio output during a transmission releases PTT first, then reports.
- Network services retry with exponential back-off, capped at 5 minutes, with jitter.
- Unhandled exceptions are logged, PTT is released, and the app shows a single recoverable error page.

## Logging

Serilog, rolling daily files, 14 days kept. Levels: Information for slot summaries and state changes, Debug for protocol traffic. Never log secrets. A separate plain-text decode journal (see `06-data-model.md`) is not a debug log.

## Portability rules

Windows-only code is confined to: `WasapiAudioBackend`, the credential store implementation, the job-object helper, `WindowsToastNotifier` with its WinRT declarations, and the installer. Everything else must compile and run on macOS and Linux.

Linux (D17) uses `PortAudioBackend` over the distribution's `libportaudio2` (ALSA, which also reaches PulseAudio and PipeWire through their `pulse` and `pipewire` devices). Device ids are `host API:device name`; a stored id whose ALSA card number has changed (`(hw:N,M)`) still finds its device. PortAudio lists devices as they were when it started, so the list is refreshed by restarting PortAudio when no stream is open. `jt9` and `rigctld` come from the distribution's `wsjtx` and `libhamlib-utils` packages, found on the PATH. The Linux build transmits like the Windows one (turned on 2026-10-31, D17). CI checks the Linux audio path end to end: a PulseAudio null sink is the loopback, and `PortAudioLoopbackTests` plays rendered FT8 and FT4 through it, captures it back and decodes it with `jt9` (measured 2026-10-13 on a VM: exact text, DT +0.10 s for output and input latency together).
