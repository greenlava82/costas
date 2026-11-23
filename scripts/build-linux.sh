#!/usr/bin/env bash
# Costas - a station-centric FT8/FT4 client.
# Copyright (C) 2026 Costas contributors. GPLv3; see LICENSE.
#
# Builds the Linux preview: publishes the app self-contained for linux-x64 and packs it as a tarball and as a Debian
# package. jt9 comes with it, taken from the official WSJT-X AppImage (see stage_jt9 below), so decoding works without
# WSJT-X installed. rigctld and PortAudio come from the distribution (libhamlib-utils, libportaudio2); the app finds
# them on the PATH, and the .deb names them as dependencies so apt installs them too. It also still names wsjtx: the
# app falls back to the distribution's jt9 where the bundled one cannot run (a distribution older than glibc 2.38).
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

# jt9 and wsjtx_app_version (which Costas asks for jt9's version before keeping jt9 running through shared memory),
# with the libraries they load, from the official WSJT-X AppImage. Its programs look for their libraries in ../lib
# (RUNPATH $ORIGIN/../lib), so third_party/wsjtx/bin and third_party/wsjtx/lib keep that layout, beside the app where
# Jt9Locator looks. The AppImage is never run: its squashfs is unpacked from the offset where its runtime ELF ends.
# Only what jt9 needs is copied (about 55 MB of WSJT-X's 260 MB); glibc, libstdc++, libgcc_s and zlib come from the
# system, as with every AppImage. A user without a new enough glibc gets the distribution's jt9 instead (the app tries
# each jt9 it finds until one runs).
wsjtx_version=3.0.1
# SHA-256 of wsjtx-3.0.1-linux-x86_64.AppImage (85,678,584 bytes), computed from a download of the SourceForge URL
# below on 2026-11-23. A new WSJT-X version needs its own hash here.
wsjtx_sha256=8671113ee3d61c44bd286bb597b70215e9bd74b44cdc932131920a7d0e8a51a5
stage_jt9() {
  local dl="$ROOT/third_party/.download" appimage tmp out lib
  appimage="$dl/wsjtx-$wsjtx_version-linux-x86_64.AppImage"
  mkdir -p "$dl"
  if [ ! -f "$appimage" ]; then
    curl -fsSL --proto '=https' --tlsv1.2 -A Wget -o "$appimage.part" \
      "https://sourceforge.net/projects/wsjt/files/wsjtx-$wsjtx_version/wsjtx-$wsjtx_version-linux-x86_64.AppImage/download"
    mv "$appimage.part" "$appimage"
  fi
  if [ "$(sha256sum "$appimage" | cut -d' ' -f1)" != "$wsjtx_sha256" ]; then
    rm -f "$appimage"; echo "The WSJT-X AppImage download does not match its pinned SHA-256" >&2; return 1
  fi
  tmp=$(mktemp -d)
  # The squashfs starts where the AppImage runtime's ELF ends: section header offset + entry size x entry count.
  local offset
  offset=$(python3 -I -c 'import struct,sys
h=open(sys.argv[1],"rb").read(64)
assert h[:4]==b"\x7fELF" and h[4]==2
print(struct.unpack_from("<Q",h,0x28)[0]+struct.unpack_from("<H",h,0x3A)[0]*struct.unpack_from("<H",h,0x3C)[0])' "$appimage")
  unsquashfs -q -n -o "$offset" -d "$tmp/sq" "$appimage" usr/bin/jt9 usr/bin/wsjtx_app_version usr/lib usr/share/doc > /dev/null
  out="$stage/third_party/wsjtx"
  lib="$tmp/sq/usr/lib"
  mkdir -p "$out/bin" "$out/lib" "$stage/third_party/licences/wsjtx"
  cp "$tmp/sq/usr/bin/jt9" "$tmp/sq/usr/bin/wsjtx_app_version" "$out/bin/"
  # The libraries those two need, and theirs in turn, that the AppImage carries; anything else is the system's.
  python3 -I - "$out/bin/jt9" "$out/bin/wsjtx_app_version" "$lib" "$out/lib" <<'PY'
import os, shutil, subprocess, sys
*progs, src, dest = sys.argv[1:]
def needed(path):
    out = subprocess.run(["readelf", "-d", path], capture_output=True, text=True, check=True).stdout
    return [l.split("[", 1)[1].split("]", 1)[0] for l in out.splitlines() if "(NEEDED)" in l]
todo, seen = list(progs), set()
while todo:
    for name in needed(todo.pop()):
        if name not in seen and os.path.exists(os.path.join(src, name)):
            seen.add(name)
            shutil.copy(os.path.realpath(os.path.join(src, name)), os.path.join(dest, name))
            todo.append(os.path.join(dest, name))
print("jt9 libraries:", " ".join(sorted(seen)))
PY
  for p in libqt5core5t64 libicu74 libfftw3-single3 libgfortran5 libgomp1 libdouble-conversion3 libpcre2-16-0 \
    libpcre2-8-0 libglib2.0-0t64 libzstd1; do
    [ -f "$tmp/sq/usr/share/doc/$p/copyright" ] && cp "$tmp/sq/usr/share/doc/$p/copyright" "$stage/third_party/licences/wsjtx/$p.copyright"
  done
  cp "$tmp/sq/usr/share/doc/wsjtx/COPYING" "$stage/third_party/licences/wsjtx/COPYING"
  cat > "$stage/third_party/THIRD-PARTY.txt" <<TXT
Costas bundles jt9 (the FT8/FT4 decoder) and wsjtx_app_version, unmodified, as separate executables, with the
libraries they load, all taken from the official WSJT-X $wsjtx_version AppImage (wsjtx-$wsjtx_version-linux-x86_64.AppImage).

WSJT-X - GNU GPL version 3. Source: https://sourceforge.net/projects/wsjt/files/
The libraries (Qt 5 Core, ICU, FFTW, the GNU Fortran and OpenMP runtimes, double-conversion, PCRE2, GLib, zstd) are
under their own licences; their copyright files are in licences/wsjtx.
TXT
  rm -rf "$tmp"
  chmod 755 "$out/bin/"*
  chmod 644 "$out/lib/"* "$stage/third_party/licences/wsjtx/"*
}
stage_jt9

# The staged jt9 must run on its own libraries and decode: a busy sample through the app's decoder host, with only the
# bundled copy to find, and no library resolved from anywhere but the bundle or the C and C++ runtimes.
if ldd "$stage/third_party/wsjtx/bin/jt9" | grep -E '=> (not found|/(usr/)?lib)' | grep -vE 'lib(c|m|mvec|stdc\+\+|gcc_s|z)\.so'; then
  echo "The staged jt9 loads a library from outside the bundle (above)" >&2; exit 1
fi
decodes=$(dotnet run --project tools/Ft8Client.DecodeCli -c Release -- --jt9 "$stage/third_party/wsjtx/bin/jt9" samples/ft8/181201_180245.wav)
echo "$decodes" | tail -3
[ "$(echo "$decodes" | grep -c '~')" -ge 5 ] || { echo "The staged jt9 did not decode the sample" >&2; exit 1; }

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
  # jt9 must still be executable inside the AppImage, or Costas falls back to a jt9 the user may not have.
  img=$(find "$vp" -maxdepth 1 -name '*.AppImage' | head -1)
  if [ -n "$img" ]; then
    img_offset=$(python3 -I -c 'import struct,sys
h=open(sys.argv[1],"rb").read(64)
print(struct.unpack_from("<Q",h,0x28)[0]+struct.unpack_from("<H",h,0x3A)[0]*struct.unpack_from("<H",h,0x3C)[0])' "$img")
    listing=$(unsquashfs -lls -o "$img_offset" "$img")
    echo "$listing" | grep -E '^-rwx.*/third_party/wsjtx/bin/jt9$' ||
      { echo "jt9 is missing or not executable in $img" >&2; echo "$listing" | grep -i jt9 >&2; exit 1; }
  fi
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
