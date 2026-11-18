#!/usr/bin/env bash
# Links jt9, ft8code, ft4code and rigctld from the distribution packages into third_party/.
# Debian/Ubuntu: sudo apt-get install wsjtx libhamlib-utils
set -euo pipefail
ROOT=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$ROOT/wsjtx/bin" "$ROOT/hamlib/bin"
for t in jt9 ft8code ft4code ft8sim wsjtx_app_version; do
  p=$(command -v "$t" || true)
  [ -n "$p" ] && ln -sf "$p" "$ROOT/wsjtx/bin/$t" || echo "warning: $t not found (install the wsjtx package)"
done
for t in rigctld rigctl; do
  p=$(command -v "$t" || true)
  [ -n "$p" ] && ln -sf "$p" "$ROOT/hamlib/bin/$t" || echo "warning: $t not found (install libhamlib-utils)"
done
# jt9 and rigctld come from the distribution's packages, which apt has already checked against the archive's signed
# index, so nothing here is downloaded except cty.dat. That changes every few weeks, so it cannot be pinned; it is not
# shipped from here (the app embeds src/Ft8Client.Data/Resources/cty.dat) and is only checked for being a country file.
tmp="$ROOT/cty.dat.new"
if curl -fsSL --proto '=https' --tlsv1.2 -o "$tmp" https://www.country-files.com/cty/cty.dat; then
  size=$(wc -c < "$tmp")
  entities=$(grep -cE '^[^[:space:]:][^:]*:[[:space:]]*[0-9]+:[[:space:]]*[0-9]+:[[:space:]]*[A-Z]{2}:' "$tmp" || true)
  if [ "$size" -ge 20480 ] && [ "$size" -le 5242880 ] && [ "$entities" -ge 300 ] && ! grep -q '<' "$tmp"; then
    mv "$tmp" "$ROOT/cty.dat"
  else
    rm -f "$tmp"; echo "warning: cty.dat download is not a country file ($entities entities); kept the old one"
  fi
else
  rm -f "$tmp"; echo "warning: cty.dat download failed"
fi
echo "done"
