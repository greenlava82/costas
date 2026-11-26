# samples/wspr

| File | Source | Content |
| --- | --- | --- |
| `150426_0918.wav` | WSJT-X source tree, tag v3.0.1, `samples/WSPR/150426_0918.wav` (GPLv3); also at sourceforge.net/projects/wsjt/files/samples/WSPR/ | One 2-minute slot (12 kHz, 16-bit mono, 1,440,000 samples), 9 decodes |

`150426_0918.golden.txt` is what `wsprd` 3.0.1 wrote for it, run from an empty folder as
`wsprd -f 14.0956 -a . 150426_0918.wav` (stdout, `wspr_spots.txt` and `ALL_WSPR.TXT`). The file name is the slot start
(`yyMMdd_HHmm`, UTC); the dial frequency is not in the WAV, so the golden run assumes the 20 m WSPR dial, 14.0956 MHz.

The WSJT-X sample set holds only this one WSPR recording. More come from `wsprsim` (V-WSPR8), when it can be built.
Callsigns in the recording are real stations heard on the air in 2015, used only by tests.
