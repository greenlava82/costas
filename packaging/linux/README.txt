Costas for Linux (preview)
==========================

This is a preview. Costas receives and transmits FT8, FT4, PSK31, PSK63 and RTTY on Linux, but it has not yet been
used with a real radio on Linux: on your first contacts, run the transmit test in Setup and watch the radio's ALC and
power.

The jt9 decoder from WSJT-X 3.0.1 comes with Costas (third_party/wsjtx). It needs glibc 2.38 or newer (Debian 13,
Ubuntu 24.04 or later); on an older Linux, also install wsjtx and Costas uses its jt9. Install the rest of what Costas
uses from your distribution (Debian and Ubuntu names):

    sudo apt install libhamlib-utils libportaudio2 libsecret-tools

libhamlib-utils provides rigctld for radio control, libportaudio2 is the audio library, and libsecret-tools lets
Costas keep your QRZ key in the desktop keyring. To use a serial port (your
radio's CAT cable), your user needs to be in the dialout group:

    sudo usermod -aG dialout $USER     (then log out and back in)

Start Costas by running ./Costas in this folder. To add it to your desktop's menu, copy costas.desktop to
~/.local/share/applications/ and costas.png to ~/.local/share/icons/, and change Exec= in costas.desktop to the full
path of Costas.

In Setup's audio step, pick your radio's USB sound card (often "USB Audio CODEC"). If the device is busy because
PipeWire or PulseAudio holds it, pick the same sound card from the devices ending in "(PulseAudio)" instead. Those
also include other programs' virtual audio devices, such as an SDR program's DAX channels.

On Debian and Ubuntu the .deb from the same release page is easier: open it to install Costas with what it needs, a
menu entry and a costas command.

Updates: Costas tells you when a newer build is out and opens the release page; download the new tarball and unpack
it. It unpacks into a new folder named for its version: run ./Costas from there, then delete this folder. If you added
Costas to your desktop's menu, change Exec= in ~/.local/share/applications/costas.desktop to the new folder. Your log
and settings are kept in ~/.local/share/Costas and are not touched.

The AppImage (CostasApp.AppImage on the same release page) updates itself like the Windows version: press Update and
Costas downloads only what changed and restarts. It uses the same log and settings.

Licence: GNU GPL v3 or later (see LICENSE). User manual: https://somedudenamedtony.github.io/costas/
