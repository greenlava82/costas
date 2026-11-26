#!/usr/bin/env bash
# Captures WSPR encoder reference vectors from WSJT-X's wsprsim into tests/Ft8Client.Core.Tests/Vectors/wspr.tsv.
# Columns: message, type (1, 2 or 3), the 11 data bytes in hex, the 162 channel symbols (each 0 to 3).
#
# WSJT-X for Windows ships no wsprsim, so it is built from lib/wsprd in the WSJT-X source (GPLv3):
#   git clone --depth 1 --branch v3.0.1 https://github.com/WSJTX/wsjtx
#   WSJTX_SRC=/path/to/wsjtx scripts/capture-wspr-vectors.sh          # builds with $CC (default cc; "zig cc" works)
# or put an already built wsprsim on the PATH (the Linux wsjtx package has none), or set WSPRSIM=/path/to/wsprsim.
# wsprsim makes no checks on what it is given, so the messages in Vectors/wspr-messages.txt are all ones that
# wsprd reads back unchanged.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT="$ROOT/tests/Ft8Client.Core.Tests/Vectors"
SIM="${WSPRSIM:-$(command -v wsprsim || true)}"
if [ -z "$SIM" ]; then
  : "${WSJTX_SRC:?no wsprsim found: set WSPRSIM, or WSJTX_SRC to a WSJT-X source tree to build it from}"
  BUILD=$(mktemp -d)
  (cd "$WSJTX_SRC/lib/wsprd" && ${CC:-cc} -O2 -o "$BUILD/wsprsim" wsprsim.c wsprsim_utils.c wsprd_utils.c fano.c nhash.c tab.c -lm)
  SIM="$BUILD/wsprsim"
fi
dest="$OUT/wspr.tsv"
echo "# WSPR vectors from wsprsim -cd, WSJT-X 3.0.1 lib/wsprd (captured $(date -u +%Y-%m-%d))" > "$dest"
while IFS= read -r m; do
  case "$m" in ''|'#'*) continue ;; esac
  # wsprsim exits 1 even when it works, so its status is ignored; the output is checked below.
  out=$( ("$SIM" -cd "$m" || true) | tr -d '\r')
  data=$(echo "$out" | sed -n 's/^Data is :\(.*\)$/\1/p' | tr -d ' ')
  sym=$(echo "$out" | sed -n '/^Channel symbols/{n;p;}' | tr -d ' ')
  if [ -z "$data" ] || [ ${#sym} -ne 162 ]; then echo "skipped (wsprsim refused it): $m" >&2; continue; fi
  case "$m" in '<'*) type=3 ;; *' '*' '*) type=1 ;; *) type=2 ;; esac
  printf '%s\t%s\t%s\t%s\n' "$m" "$type" "$data" "$sym" >> "$dest"
done < "$OUT/wspr-messages.txt"
echo "$dest: $(grep -vc '^#' "$dest") vectors"
