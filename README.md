<p align="center"><img src="src/Ft8Client.App/Assets/costas-256.png" width="96" alt="Costas icon"></p>

# Costas

**A free FT8, FT4, PSK31, PSK63 and RTTY program for Windows and Linux that shows you stations, not messages.**

Costas uses the same decoder as WSJT-X, so it decodes as well and sounds identical on the air. What changes is the
screen: instead of a scrolling list of decoded lines, you get one row per station, ranked by what you need for your
log, whether the station is free to call, and whether it can hear you.

**[Website](https://costasdigi.com/)** ·
**[User manual](https://costasdigi.com/introduction/)** ·
**[Download](https://github.com/somedudenamedtony/costas/releases/latest)** ·
**[Report a problem](https://github.com/somedudenamedtony/costas/issues)**

![The Operate screen](manual/images/operate-idle.png)

## Features

- **Next in line and Repeats**: the stations you need, best first (new country, band, grid, call), and the ones you
  have already worked, with one click to call either.
- **Who hears you**: PSK Reporter shows whether each station, or someone near it, hears your signal.
- **One-click contacts**: click **Call** or press Enter and Costas runs the exchange and logs it. Fox/Hound
  (SuperFox included) is automatic, and **Auto-call** works down the list for you.
- **PSK31, PSK63 and RTTY** on the same screen, with macros and live receive text.
- **Contests**: ARRL RTTY Roundup, Field Day, WW Digi and ARRL Digi.
- **Hunt list, DXpeditions and awards**: alerts for what you are after, the DX operations on the air, and DXCC, WAS,
  WAZ and VUCC progress.
- **Logging**: a local log with ADIF import and export, uploaded as you go to QRZ, LoTW, eQSL, Club Log and
  Wavelog/Cloudlog, with confirmations synced back.
- **Works with your other tools**: WSJT-X UDP (GridTracker, JTAlert, loggers), DXLab Suite (Commander and
  SpotCollector), Reverse Beacon Network spots, and radio control through Hamlib, FLRig or FlexRadio SmartSDR.
- **Operate from a phone or tablet** on your home network, or away from home through Tailscale.
- **Transmit level that sets itself** per band from the radio's ALC, with live power, ALC and SWR meters.
- **Safe by design**: it never transmits without you, never changes a message silently, and has a watchdog,
  band-edge check, clock check, SWR stop and PTT release on any fault.
- **Updates itself** with small downloads that keep your log and settings.

The [manual](https://costasdigi.com/introduction/) covers every screen and setting, with screenshots.

<table>
<tr>
<td><img src="manual/images/contact-in-progress.png" alt="A contact in progress"></td>
<td><img src="manual/images/calling-you.png" alt="The Calling you bar"></td>
</tr>
<tr>
<td align="center">A contact in progress</td>
<td align="center">Someone calling you while you work another station</td>
</tr>
<tr>
<td><img src="manual/images/awards.png" alt="The Awards view"></td>
<td><img src="manual/images/log.png" alt="The Log view"></td>
</tr>
<tr>
<td align="center">Award progress and the countries you still need</td>
<td align="center">The log, with QRZ, LoTW and eQSL status</td>
</tr>
</table>

## Install

### Windows

1. Download `CostasApp-win-Setup.exe` from the
   [latest release](https://github.com/somedudenamedtony/costas/releases/latest) and run it. It installs for your
   user only, with no administrator rights.
2. Costas is not code-signed yet. If Windows says *Windows protected your PC*, click **More info**, then
   **Run anyway**. Windows 11's Smart App Control blocks unsigned programs outright; see
   [Troubleshooting](https://costasdigi.com/troubleshooting/#smart-app-control).
3. Start Costas. Setup walks you through your station, radio, audio, a transmit test, a clock check and QRZ.

Needs 64-bit Windows 10 (1809) or later. The `jt9` decoder and Hamlib are bundled, so you do not need WSJT-X.

### Linux (preview)

Each release also has a self-updating **AppImage** (`CostasApp.AppImage`), a **.deb** for Debian, Ubuntu and Mint
(`sudo apt install ./costas_*_amd64.deb`) and a **tarball**. All include `jt9`; the AppImage and tarball need Hamlib's
`rigctld` and PortAudio from your distribution. Linux transmits in every mode but has had little time on real radios,
so watch your ALC and power on your first contacts. Details:
[Install on Linux](https://costasdigi.com/install/#linux-preview).

### Updates

Costas checks for a new release when it starts and daily, and installs it with one click, keeping your log and
settings. Testers can follow [development builds](https://costasdigi.com/install/#development-builds) instead.

## Status

Costas is young software. FT8 and FT4 are in daily use on the author's Yaesu FT-710; other radios, PSK and RTTY,
and Linux have had less time on the air. Use low power at first and
[report what you find](https://github.com/somedudenamedtony/costas/issues), or use **☰ › Report a problem…** in the
app.

## Privacy

Costas sends anonymous usage statistics (Aptabase) and crash reports (Sentry). They never include your callsign,
grid, name, frequencies, the stations you hear or work, or anything from your log, and nothing is sent from builds
you make yourself or in simulation. Turn both off in **Settings › Privacy**. The full list of what is sent is in
[the manual](https://costasdigi.com/settings/#privacy).

## For developers

C# on .NET 10 with Avalonia 12. WSJT-X's `jt9` decodes FT8 and FT4 and Hamlib's `rigctld` controls the radio, both as
supervised child processes; the PSK and RTTY modems are Costas's own. Rules for working on the code are in
[`CLAUDE.md`](CLAUDE.md), and the design is in [`docs/`](docs/), starting with
[`01-product-spec.md`](docs/01-product-spec.md) and [`03-architecture.md`](docs/03-architecture.md).

```
third_party/fetch.sh            # or on Windows: pwsh third_party/fetch.ps1 (needs 7-Zip); fetches jt9, Hamlib, cty.dat
dotnet build
dotnet test
dotnet run --project src/Ft8Client.App -- --simulate samples/ft8 --simulate-partner
```

The last line replays sample recordings with a simulated station that answers your calls, so no radio is needed.
Simulation never keys a radio or uploads anything. Without `--simulate` and with no radio set up, the app shows no
data. [Building from source](https://costasdigi.com/building/) in the manual covers the decode harness, the installer
(`scripts/build-installer.ps1`, `scripts/build-linux.sh`) and code signing. Developer mode (Hamlib's test radio,
development builds) is hidden: in **☰ › About**, click the product name seven times.

CI builds and tests every push on Windows and Linux and keeps each run's installer as an artifact. Each green push to
`main` refreshes the `dev` prerelease; a release goes out only when the owner runs **Actions › CI › Run workflow**
on `main`. The manual and website are in [`manual/`](manual/) (MkDocs Material) and publish to
[costasdigi.com](https://costasdigi.com/) on every push to `main`; preview with
`pip install -r manual/requirements.txt && mkdocs serve`.

## Credits and licence

Costas is free software under the [GNU General Public License, version 3](LICENSE) or later, because it ships and
runs the WSJT-X decoder.

- Decoding by `jt9` from [WSJT-X](https://wsjt.sourceforge.io/) (K1JT, K9AN, G4WJS and the WSJT Development Group), GPLv3.
- Rig control by [Hamlib](https://hamlib.github.io/), LGPL.
- Transmit encoder ported from [ft8_lib](https://github.com/kgoba/ft8_lib) by Kārlis Goba, MIT.
- Country file `cty.dat` by Jim Reisert AD1C, [country-files.com](https://www.country-files.com/).
- Reception reports from [PSK Reporter](https://pskreporter.info/) (Philip Gladstone, N1DQ) and its MQTT feed by
  M0LTE. Logbook and lookups by [QRZ.com](https://www.qrz.com/).

Costas is developed by Tony Shepherd, W7LIT. The code namespace is `Ft8Client`, from before the app was named.
