# third_party

Binaries the app runs as child processes. Not committed; fetched by script.

| Folder | What | Licence | Source |
| --- | --- | --- | --- |
| `wsjtx/bin/` | `jt9` decoder (and `ft8code`, `ft4code` for test vectors) with its runtime DLLs | GPLv3 | https://wsjt.sourceforge.io/ , source https://github.com/WSJTX/wsjtx |
| `hamlib/bin/` | `rigctld`, `rigctl` | LGPLv2.1 / GPLv2 | https://hamlib.github.io/ |
| `cty.dat` | Country file | free use, see country-files.com | https://www.country-files.com/cty/cty.dat |

Fetch:

- Windows: `pwsh third_party/fetch.ps1` (needs 7-Zip on PATH to unpack the WSJT-X installer).
- Linux: `third_party/fetch.sh` (uses the distribution packages `wsjtx` and `libhamlib-utils`).

`fetch.ps1` checks each download against a SHA-256 pinned in the script and stops on a mismatch, or when the version
asked for has no pinned hash. Moving to a new WSJT-X or Hamlib version means adding its hash there, checked against
what upstream publishes. `cty.dat` changes every few weeks and is not pinned; both scripts only check that it is a
country file. It is not what the installer ships: the app embeds `src/Ft8Client.Data/Resources/cty.dat`.

During development the app also finds an installed WSJT-X (`C:\WSJT\wsjtx\bin\jt9.exe`, `/usr/bin/jt9`)
and the `FT8CLIENT_JT9` environment variable. Track WSJT-X general-availability releases only.
