#!/usr/bin/env bash
# Costas - a station-centric FT8/FT4 client.
# Copyright (C) 2026 Costas contributors. GPLv3; see LICENSE.
#
# Builds the Linux preview: publishes the app self-contained for linux-x64 and packs it as a tarball and as a Debian
# package. jt9, rigctld and PortAudio come from the distribution (wsjtx, libhamlib-utils, libportaudio2); the app finds
# them on the PATH, and the .deb names them as dependencies so apt installs them too.
# Output: artifacts/Costas-<version>-linux-x64.tar.gz, artifacts/costas_<version>_amd64.deb and a .sha256 for each
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
