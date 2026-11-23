# Install

## Download

1. Open the [Releases page](https://github.com/somedudenamedtony/costas/releases/latest).
2. Under **Assets**, download `CostasApp-win-Setup.exe`.
3. Run it. There is nothing to choose: it installs Costas for your user only (no administrator rights needed), adds it
   to the Start menu and opens it.

Everything Costas needs is in the installer: the WSJT-X `jt9` decoder and Hamlib's `rigctld` for radio control are
bundled. You do not need WSJT-X installed.

!!! warning "Windows SmartScreen"
    Costas is not code-signed yet, so Windows may show *Windows protected your PC* with *Publisher: Unknown
    publisher* and only a **Don't run** button. Click **More info** (under the message), then **Run anyway**. Edge or
    Chrome may also say the download "isn't commonly downloaded": choose **Keep** (in Edge, **⋯ › Keep**, then
    **Keep anyway**). Each release also has a `.sha256` file if you want to check the download. Once releases are
    signed, the warning names the publisher, and it stops appearing as the signature builds a download history with
    Microsoft.

    Windows 11's **Smart App Control** is stricter: it blocks unsigned programs with no **Run anyway**. See
    [Troubleshooting](troubleshooting.md#smart-app-control).

Requirements: 64-bit Windows 10 (version 1809) or later, or Windows 11.

## Updates

Costas checks GitHub for a newer build when it starts and then once a day. When one is available, a bar
appears at the top of the main window:

![The update bar](images/update-bar.png)

- **Update** downloads only what changed since your build (usually a few MB; the bar shows the size), checks it,
  closes Costas, swaps in the new files and opens Costas again, all within a few seconds. There is nothing to click.
  While that happens a small **Updating Costas** window stays where Costas was, saying what it is doing (closing,
  installing the new version, opening it), and goes away once the new Costas is on screen. If the update cannot be
  applied, Costas opens again as it was.
  It is refused while you are transmitting or in a contact: finish or halt first.
- **Release notes** opens the release on GitHub.
- **Skip this version** hides the bar until a newer build comes out.

You can turn the daily check off, or check by hand, in **Settings › Updates**. **☰ › About** also has a **Check for updates** button, which checks even when the daily check is off. When a newer build is found, an **Update** button appears beside it, so you can install it without closing About first.

### Development builds

Testers can follow development builds: every change to Costas that passes its automatic tests, usually several a
week, before it goes into a release. Turn on [developer mode](settings.md#developer-mode), then tick **Settings ›
Developer › Development builds**. Costas checks straight away and the update bar offers the newest development build;
from then on it offers each new one. Development builds are full downloads (about 90 MB) and are tested only by CI, so
they can break on air.

To stop, untick **Development builds** (or turn developer mode off). Costas checks again and the bar offers to **go
back to the latest release**, even though it is older than the development build you have. Your settings and log are
kept.

Upgrading keeps your log and settings.

!!! note "Installed before October 2026?"
    Builds up to 0.1.0.407 used a different installer, which downloaded the whole program (about 100 MB) on every
    update. The first update after that downloads the full program once more and moves Costas to the new location
    below; it removes the old copy and keeps your Start menu entry, log and settings. From then on updates are small.
    If you had installed for all users (in `C:\Program Files\Costas\`), remove that old copy yourself from
    **Settings › Apps** after the move. An install from before the rename (when the app was called "FT8 Client") is
upgraded too, and its data is moved to the Costas data folder on first start.

## Where your data lives

| What | Where |
| --- | --- |
| Log, settings, decode journal, saved audio | `%LOCALAPPDATA%\Costas\` (for example `C:\Users\you\AppData\Local\Costas\`) |
| QRZ API key and password | Windows Credential Manager, never in a file |
| The program | `%LOCALAPPDATA%\CostasApp\` (`%LOCALAPPDATA%\Programs\Costas\` or `C:\Program Files\Costas\` for installs from before October 2026) |

Uninstalling removes the program but keeps your data folder, so a reinstall picks up where you left off.

## Linux (preview) {#linux-preview}

Each release also has three Linux builds of the same program:

| Build | For | Updates |
| --- | --- | --- |
| **AppImage**, `CostasApp.AppImage` | Any 64-bit Linux | Itself, like Windows: **Update** downloads only what changed and restarts Costas |
| **.deb**, `costas_<version>_amd64.deb` | Debian, Ubuntu, Mint | By hand: download and open the new `.deb` |
| **Tarball**, `Costas-<version>-linux-x64.tar.gz` | Any 64-bit Linux | By hand: download and unpack the new tarball |

All three include the `jt9` decoder from WSJT-X 3.0.1, as the Windows installer does, so you do not need WSJT-X
installed. It needs a Linux from 2023 or later (glibc 2.38: Debian 13, Ubuntu 24.04, Mint 22, Fedora 39 or newer); on
an older one Costas uses the `jt9` from your distribution's `wsjtx` package instead.

They do everything the Windows version does, transmit included, but Linux is still a **preview**: it has had little time
with real radios. On your first contacts, run the transmit test in Setup and watch the radio's ALC and power, as you
would on Windows.

### AppImage (updates itself)

1. Install what Costas uses from your distribution: Hamlib's `rigctld` and PortAudio. On Debian, Ubuntu and Mint:

        sudo apt install libhamlib-utils libportaudio2 libsecret-tools pulseaudio-utils

    On a Linux older than Debian 13 or Ubuntu 24.04, add `wsjtx` for the `jt9` decoder.

2. Download `CostasApp.AppImage` from the [download page](index.md#download) and put it where you keep programs,
   for example `~/Applications`. Keep it in a folder you own: Costas replaces the file when it updates.
3. Make it executable (in its file properties, or `chmod +x CostasApp.AppImage`) and double-click it.
4. For a USB CAT cable, add yourself to the `dialout` group (`sudo usermod -aG dialout $USER`), then log out and in.

If it does not start and says FUSE is missing, install `libfuse2` (`sudo apt install libfuse2t64` on Debian 13 and
Ubuntu 24.04, `libfuse2` on older ones). An AppImage adds no menu entry by itself; a helper such as Gear Lever or
AppImageLauncher can add one.

### Debian, Ubuntu and Mint

1. Download the `.deb` from the [download page](index.md#download) or the
   [Releases page](https://github.com/somedudenamedtony/costas/releases/latest).
2. Open it (double-click) to install it with your software installer, or in a terminal in the download folder:

        sudo apt install ./costas_*_amd64.deb

    apt also installs what Costas uses: `libhamlib-utils` for `rigctld`, the PortAudio audio library, and `wsjtx`,
    whose `jt9` Costas falls back to on a Linux too old for the one it brings. Costas goes in `/opt/costas`, with a **Costas** entry in your desktop's menu and a
    `costas` command.
3. For a USB CAT cable, add yourself to the `dialout` group (`sudo usermod -aG dialout $USER`), then log out and in.

### Other distributions

1. Install what Costas uses from your distribution: Hamlib's `rigctld`, PortAudio, and optionally `secret-tool`
   (libsecret) and `pactl`. On Debian and Ubuntu these are
   `sudo apt install libhamlib-utils libportaudio2 libsecret-tools pulseaudio-utils`. On a Linux older than glibc
   2.38, add the `jt9` decoder too (usually in the `wsjtx` package).

    `secret-tool` lets Costas keep your QRZ key in the desktop keyring (without it, the key is asked for again each
    time Costas starts). `pactl` lets Costas list PulseAudio and PipeWire devices by name.
2. For a USB CAT cable, add yourself to the `dialout` group (`sudo usermod -aG dialout $USER`), then log out and in.
3. Download the tarball from the [Releases page](https://github.com/somedudenamedtony/costas/releases/latest),
   unpack it and run `./Costas`. `README.txt` inside says how to add it to your desktop's menu.

### Audio on Linux

In Setup's audio step, pick the radio's USB sound card (often *USB Audio CODEC*). If it is busy because PipeWire or
PulseAudio holds it, pick the same card from the devices marked **(PulseAudio)**. Those also include virtual devices
other programs make: with a FlexRadio and RimuSDR, use RimuDAX's **rimu.dax.rx1** and **rimu.dax.tx**
([FlexRadio on Linux](setup.md#2-radio-cat-and-ptt)).

### Updates

Costas checks for updates on Linux as on Windows, and the update bar tells you when a new version is out.

- **AppImage**: press **Update**. Costas downloads only what changed, closes, replaces its AppImage file and starts
  again, as on Windows.
- **.deb**: download the new `.deb` and open it, or `sudo apt install ./costas_*_amd64.deb`. It replaces the old
  version.
- **Tarball**: download the new tarball and unpack it. It unpacks into a new folder named for its version: run
  `./Costas` from there and delete the old folder. If you added Costas to your desktop's menu, point `Exec=` in its
  `costas.desktop` at the new folder.

Whichever you use, your log and settings live in `~/.local/share/Costas/` and are kept. To move from the .deb or the
tarball to the AppImage, download the AppImage and start it; it finds the same log and settings (remove the .deb with
`sudo apt remove costas`, or delete the tarball's folder).

## Next

Start Costas. The first time, it opens [Set up Costas](setup.md).
