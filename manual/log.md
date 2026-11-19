# Log, QRZ and ADIF

Press **Log** in the top bar or ++ctrl+4++.

![The Log view](images/log.png)

Every contact, newest first: date, time (UTC), call, band, mode, report sent and received, grid, country, name, the
need tag it had when you worked it, and its LoTW, QRZ and eQSL status (and Wavelog, when your profile uses Wavelog or Cloudlog).

- Contacts are grouped by UTC day. Each day opens with a heading such as "Today · Sun 1 Nov  ·  4 contacts  ·
  4 countries  ·  1 confirmed  ·  20m".
- The tiles at the right of the filters count the contacts shown, their countries and how many are confirmed.
  While a filter is on, the contacts tile reads, for example, "120 of 3,400 contacts".
- The need tag shows as a coloured tag, as in the station list. A LoTW confirmation is a green **✓ Confirmed** tag.
  A QRZ upload that is waiting (amber) or failed (red) is a tag too, so it stands out; everything else stays quiet.
  The Wavelog column works the same way: *From Wavelog* (downloaded), *In Wavelog*, or a *Waiting to upload* or
  *Retrying: reason* tag.
- The line at the bottom says when the log was last synced with QRZ, and, if you download your Wavelog log, when it
  was last downloaded.

## Filters

The row under the buttons narrows the list. The filters combine with each other and with the search box:

| Filter | Choices |
| --- | --- |
| Station | Your callsign (the current station profile's log: the default), or All stations. Contacts made under another callsign, such as another operator's profile or a log imported from an old call, show only with All stations |
| Band | All bands, or any band you have a contact on |
| Mode | All digital (the default: everything but phone and CW), All modes, FT8, FT4, PSK (PSK31 and PSK63) or RTTY |
| Date | All time, Today (UTC), Last 7 days, Last 30 days, This year. Days are UTC days, as the log shows them |
| Confirmed | Confirmed or not, Confirmed (by QRZ, the ADIF you imported, or LoTW), Not confirmed |

Your callsign and All digital are the starting point, so they do not count as filters, and Clear filters leaves the station as it is; the totals count your station's contacts only. While another filter is set, the status line says *Showing 120 of 3,400 contacts* and **Clear filters** puts them all back.
The search text is kept. The list shows the newest 5,000 matching contacts; the totals always count all of them.

| Button | What it does |
| --- | --- |
| Search box | Finds contacts by call, grid, country or name |
| **Send to loggers again** | Sends the selected contact over UDP again, exactly as when it was logged (WSJT-X *QSO Logged* and *Logged ADIF*), for a logger such as Log4OM, WLGate or JTAlert that was not running then. The line under the list says where it went, or why nothing was sent (for example UDP is off in [Settings › Other programs](settings.md#other-programs)). Your logger decides whether a second copy becomes a duplicate |
| **Edit** | Opens the selected contact to correct it (call, grid, reports, name, power, comment). Your corrections stay: a later QRZ sync adds confirmations but does not change what you edited. The change is not sent to QRZ; correct it there too if you want both to match |
| **Delete** | Removes the selected contact from the local log. Press it twice to confirm. It is **not** deleted from QRZ, but QRZ syncs and LoTW downloads will not bring it back. Importing an ADIF file that holds it does |
| **Import ADIF** | Reads an `.adi` file (from WSJT-X, another logger, or a QRZ export). Duplicates are merged, not added twice; Costas reports how many were imported, merged and skipped |
| **Export ADIF** | Writes your station's contacts to an `.adi` file, or every station's when the Station filter is All stations, with each contact's LoTW, eQSL and QRZ sent and received state (and the paper QSL and Club Log fields of contacts you imported). An export with more than one station callsign is written as one file per callsign, named after the one you chose (`log-W7LIT.adi`, `log-VE3_W7LIT.adi`), so each can be uploaded to that callsign's logbook |
| **Sync with QRZ now** | Reads your QRZ logbook and uploads anything waiting |
| **LoTW ▾** | Downloads new LoTW confirmations, or signs and uploads new contacts with TQSL (see [LoTW](#lotw)) |
| **Wavelog ▾** | Shown only when your profile uses Wavelog or Cloudlog. **Download new contacts** fetches what is new in your Wavelog log now instead of waiting for the 15-minute check (needs the download switched on, see [Using Wavelog as your logbook](settings.md#using-wavelog-as-your-logbook-for-worked-before)). **Upload waiting contacts now** sends contacts that are waiting or retrying without waiting for their retry time |

## What Costas logs

Call, date and time on and off, band, frequency, mode (FT8 or FT4), reports sent and received, their grid, your grid
and callsign, your power, the name, state and country from QRZ lookups when available, and a comment in the form
other FT8 loggers use (`FT8 Sent: -01 Rcvd: -13`). Power comes from the radio when
it can report its power setting; otherwise from **Settings › Station › Power (watts)**.

When the station was spotted on pota.app at a park on the band you worked it on, the contact also gets the ADIF
fields `SIG` = `POTA` and `SIG_INFO` = the park reference (such as `US-1234`). These go to QRZ, into ADIF exports and
to other programs over UDP. A station calling CQ POTA that nobody has spotted is logged without a park, since Costas
does not know which one it is. Parks already in your log (from `SIG`/`SIG_INFO`, or `POTA_REF` in imported files)
are what decide *New park* or *Park* on the Operate screen, and imported park fields are kept on export.

## QRZ logbook

With a QRZ logbook API key (set in [setup](setup.md#6-qrz-and-psk-reporter) or
[Settings › Logbooks › QRZ](settings.md#qrz)):

- **Sync down.** Costas reads your whole QRZ logbook, including confirmations, and keeps checking for changes
  (every 15 minutes by default). This is what makes *New DXCC*, *New DXCC 20 m* and the log totals right. Without it,
  Costas only knows the contacts made in Costas or imported from ADIF, and every other station shows as a new call.
- **Upload.** Each contact is uploaded as soon as it is logged. Its status shows in the Log and in **Worked this session**:
  *Uploaded to QRZ*, *Waiting to upload · retrying*, or *Upload failed · Retry*. If QRZ is unreachable, uploads wait
  in a queue and retry with back-off; nothing is lost.
- **Confirmed only.** If you chase confirmations rather than contacts, tick
  **Settings › Who to call › Count a country or state as worked only when confirmed**. By default only LoTW
  confirmations count, because QRZ and eQSL confirmations do not count for DXCC (see below).

## LoTW

ARRL's Logbook of the World is where DXCC and WAS credit comes from. Set it up in [Settings › Logbooks › LoTW](settings.md#lotw).

- **Confirmations.** With your LoTW website login, Costas downloads new LoTW confirmations every 12 hours, and
  when you choose **LoTW ▾ › Download new confirmations** in the Log view. Each one marks the matching contact
  (same call, band and mode within 30 minutes) as confirmed on LoTW. A confirmation for a contact that is not in your
  log adds it.
- **Uploads.** Costas signs and uploads contacts through TQSL, ARRL's signing program, which holds your callsign
  certificate. Choose **LoTW ▾ › Upload new contacts with TQSL**, or turn on uploading each contact as it is logged.
  TQSL skips contacts LoTW already has. Install TQSL first and set up your Station Location in it. With uploading as
  each contact is logged, a contact logged while TQSL is still busy is sent straight after, and an upload that failed
  (LoTW unreachable, for example) is tried again after 2, 4, 8 and so on up to 60 minutes.
- **Who uses LoTW.** Costas downloads ARRL's list of LoTW users once a week. Station details then say *LoTW (last
  upload 1 Oct 2026)*, the Why tooltip says *Uses LoTW*, and with only confirmed contacts counting, stations that
  upload to LoTW rank ahead of those that do not.

The Log view's **LoTW** column says *Confirmed* or *Uploaded*. Station details say whether each past contact was
*Confirmed on LoTW* or *Confirmed (QRZ, eQSL or card)*. Nothing is downloaded into the log or uploaded in simulation.

## Other loggers

Costas sends the WSJT-X UDP messages (decodes, status, logged contact as ADIF) to `127.0.0.1` port 2237 by default
(in PSK and RTTY only the status, naming the mode, and logged contacts),
so GridTracker, JTAlert, N1MM+ and loggers that understand WSJT-X see your activity. Change the address in
[Settings › Other programs](settings.md#other-programs). If your logger was not running when a contact was logged,
select the contact in the Log view and press **Send to loggers again**.

They can also drive Costas back, the same way they drive WSJT-X:

- **Calling from another program.** Double-clicking a CQ in GridTracker (or a station calling you) calls it in Costas,
  exactly as double-clicking it in [Raw decodes](raw-decodes.md) would. Costas acts only if it decoded that same
  message itself, and never starts a call that neither you nor that program asked for.
- **Halt.** A Halt Tx from the other program stops transmitting at once.
- **Highlights.** Colours JTAlert or GridTracker give a callsign show in Raw decodes.

With the address left at `127.0.0.1`, only programs on this computer can do this.

## Club Log

To send each contact to [Club Log](https://clublog.org) as you make it, open [Settings › Logbooks › Club Log](settings.md#club-log),
enter the email your Club Log account uses and an **Application Password** (make one in Club Log under Settings ›
API; it is not your Club Log password), and tick **Upload each new contact**. The Club Log light in the bottom bar
shows how uploads are going. If Club Log refuses the login, Costas stops sending until you change the email or
password, because Club Log blocks programs that keep trying. Real-time upload is for new contacts only: to send an
older log, upload an ADIF export on the Club Log site.

## Backups {#backups}

Once a day Costas copies the log and your settings into the `backups` folder in its data folder
(`%LOCALAPPDATA%\Costas\backups` on Windows), as `costas-2026-11-15.db` and `settings-2026-11-15.json`. It keeps the
last 14 days. The copy is taken while Costas runs, so there is nothing to do.

To go back to a copy: close Costas, rename `costas.db` in the data folder to keep it, copy the backup you want in as
`costas.db`, and start Costas again. Contacts made after that backup are in your QRZ logbook, LoTW or your other
logger if uploads were on, and an ADIF import brings them back.

### ADIF copy of the log {#adif-copy}

The same folder also holds a plain ADIF copy of your whole log, `costas-log-W7LIT.adi` (one file per station
callsign; a `/` in the call becomes `_`). It holds every contact, oldest first, with its LoTW, eQSL and QRZ sent and
received state, and any logger, Costas included, can import it. Costas rewrites it a few seconds after you log, edit
or delete a contact and after an import or a sync, in the background, so it never holds up logging or transmitting.
Each file is written whole and then swapped in, so even a crash or a power cut never leaves half a file. The copy is
kept in simulation too.

To restore from it, open the Log view and press **Import ADIF**. Contacts come back with their callsign, reports,
confirmations and upload state; ones already in the log are merged, not added twice. They are filed under the
station profile you are using when you import.

[Settings › Logbooks › Backups](settings.md#backups) shows where the folder is, when the copy was last written, and
has an **Open folder** button. If the copy cannot be written (the disk is full, or the folder was removed), that page
says why, a **Log copy** light shows in the bottom bar, and Costas tries again after a minute, then less often.

The file is plain UTF-8. Names with accented letters are counted in bytes, which most loggers expect; ADIF itself
only allows plain ASCII in `.adi` files, so a few strict programs may still complain about them.

Settings are protected too: each save keeps the previous file as `settings.json.bak`, and if the settings file is
ever damaged Costas starts from that copy and says so on the status bar.

## Decode journal and saved audio

Every decode and transmission is written to a monthly text file in the data folder's `journal` folder. Costas reads
it back to tell you [when you hear each country](awards.md#when-to-look). You can also
keep the received audio of each slot as WAV files (**Settings › Other programs › Save received audio**: none, only
slots with decodes, or all, kept for a number of days).
