#!/usr/bin/env bash
# Costas - a station-centric FT8/FT4 client.
# Copyright (C) 2026 Costas contributors. GPLv3; see LICENSE.
#
# Builds the Linux preview: publishes the app self-contained for linux-x64 and packs it as a tarball and as a Debian
# package. jt9, rigctld and PortAudio come from the distribution (wsjtx, libhamlib-utils, libportaudio2); the app finds
# them on the PATH, and the .deb names them as dependencies so apt installs them too.
# Output: artifacts/Costas-<version>-linux-x64.tar.gz, artifacts/costas_<version>_amd64.deb and a .sha256 for each, and
# Velopack's AppImage (CostasApp.AppImage), package and feed in artifacts/velopack-linux (velopack-linux-dev for the
# development channel).
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
# Release builds are <major>.<minor>.<CI run number>, as on Windows (Velopack needs three parts there).
[ -n "${1:-}" ] && version="${version%.*}.$1"
name="Costas-$version-linux-x64"
art="$ROOT/artifacts"
stage="$art/$name"
deb="costas_${version}_amd64.deb"
rm -rf "$stage" "$art/$name.tar.gz" "$art/$name.tar.gz.sha256" "$art/deb" "$art/$deb" "$art/$deb.sha256"
mkdir -p "$stage"

dotnet publish src/Ft8Client.App -c Release -r linux-x64 --self-contained -o "$stage" "-p:Version=$version"
rm -f "$stage"/*.xml "$stage"/*.pdb
cp LICENSE "$stage/"
cp packaging/linux/README.txt packaging/linux/costas.desktop "$stage/"
cp src/Ft8Client.App/Assets/costas-256.png "$stage/costas.png"
[ -x "$stage/Costas" ] || { echo "Costas missing from the publish output" >&2; exit 1; }

tar -C "$art" -czf "$art/$name.tar.gz" "$name"
(cd "$art" && sha256sum "$name.tar.gz" | cut -d' ' -f1 > "$name.tar.gz.sha256")
echo "$art/$name.tar.gz"

# Debian package: the same files in /opt/costas, a "costas" command, and a menu entry with its icon. Installing a newer
# .deb replaces the program; the log and settings in ~/.local/share/Costas are never touched.
root="$art/deb"
mkdir -p "$root/opt" "$root/usr/bin" "$root/usr/share/applications" "$root/usr/share/icons/hicolor/256x256/apps" \
  "$root/usr/share/doc/costas" "$root/DEBIAN"
cp -a "$stage" "$root/opt/costas"
rm -f "$root/opt/costas/costas.desktop"
cp packaging/linux/README.deb.txt "$root/opt/costas/README.txt"
printf '#!/bin/sh\nexec /opt/costas/Costas "$@"\n' > "$root/usr/bin/costas"
chmod 755 "$root/usr/bin/costas"
sed 's:^Exec=.*:Exec=/opt/costas/Costas:' packaging/linux/costas.desktop > "$root/usr/share/applications/costas.desktop"
cp src/Ft8Client.App/Assets/costas-256.png "$root/usr/share/icons/hicolor/256x256/apps/costas.png"
cp packaging/linux/copyright "$root/usr/share/doc/costas/copyright"
size=$(du -sk --exclude=DEBIAN "$root" | cut -f1)
sed -e "s/@VERSION@/$version/" -e "s/@SIZE@/$size/" packaging/linux/control > "$root/DEBIAN/control"
find "$root" -type d -exec chmod 755 {} +
dpkg-deb --root-owner-group -Zxz --build "$root" "$art/$deb" > /dev/null
rm -rf "$root"
(cd "$art" && sha256sum "$deb" | cut -d' ' -f1 > "$deb.sha256")
echo "$art/$deb"

# AppImage through Velopack, the same updater as on Windows: Costas updates itself in place (Update, then it restarts),
# downloading only what changed when the previous release's package is there to make a delta from. Pack id CostasApp
# as on Windows; Linux uses Velopack's "linux" channel (releases.linux.json), so the two feeds never mix. Needs
# mksquashfs (squashfs-tools).
if command -v mksquashfs > /dev/null; then
  vp="$art/velopack-linux"
  rm -rf "$vp" "$art/velopack-linux-dev"
  mkdir -p "$vp"
  dotnet tool restore > /dev/null
  if [ "${VELOPACK_PREVIOUS:-}" = 1 ]; then
    # The first Linux Velopack release has nothing to download; it ships without a delta.
    dotnet vpk download github --repoUrl https://github.com/somedudenamedtony/costas --channel linux --outputDir "$vp" ||
      echo "::warning::No earlier Linux Velopack release to make a delta from"
  fi
  vpk=(dotnet vpk pack --packId CostasApp --packVersion "$version" --packDir "$stage" --mainExe Costas --packTitle Costas
    --packAuthors 'Costas contributors' --icon src/Ft8Client.App/Assets/costas-256.png --runtime linux-x64)
  "${vpk[@]}" --outputDir "$vp"
  # Only this version's packages are published; an earlier one downloaded for the delta is already on its own release.
  find "$vp" -name '*.nupkg' ! -name "*-$version-*" -delete
  # Development builds are packed again in their own channel (releases.linux-dev.json), for installs that follow them.
  if [ "${COSTAS_CHANNEL:-}" = dev ]; then
    mkdir -p "$art/velopack-linux-dev"
    "${vpk[@]}" --channel linux-dev --outputDir "$art/velopack-linux-dev"
  fi
  ls -la "$vp"
else
  echo "mksquashfs is missing (squashfs-tools): no AppImage" >&2
fi
