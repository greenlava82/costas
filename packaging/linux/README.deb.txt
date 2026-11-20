Costas for Linux (preview)
==========================

This is a preview. Costas receives and transmits FT8, FT4, PSK31, PSK63 and RTTY on Linux, but it has had little time
with real radios on Linux: on your first contacts, run the transmit test in Setup and watch the radio's ALC and power.

Installed from the .deb, Costas lives in /opt/costas. Start it from your desktop's menu (Costas, under Ham Radio or
Internet) or by typing costas in a terminal. apt installed what it needs: jt9 (wsjtx), rigctld (libhamlib-utils) and
PortAudio.

To use a serial port (your radio's CAT cable), your user needs to be in the dialout group:

    sudo usermod -aG dialout $USER     (then log out and back in)

In Setup's audio step, pick your radio's USB sound card (often "USB Audio CODEC"), or any PulseAudio or PipeWire
device by name, such as an SDR program's virtual audio devices (they end in "(PulseAudio)").

Updates: Costas tells you when a newer build is out and opens the release page. Download the new .deb and open it
(or run: sudo apt install ./costas_<version>_amd64.deb). It replaces this version. Your log and settings are kept in
~/.local/share/Costas and are not touched. To remove Costas: sudo apt remove costas.

Licence: GNU GPL v3 or later (see LICENSE). User manual: https://somedudenamedtony.github.io/costas/
