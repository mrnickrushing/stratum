#!/usr/bin/env bash
# Packs the desktop builds for release: the Windows installer (NSIS), a Windows zip, and a Linux tarball that
# carries install.sh. Run after building both players (Assets/Editor/Build.cs):
#   Tools/release/package.sh 0.2.0
set -euo pipefail
V="${1:?version, e.g. 0.2.0}"
cd "$(dirname "$0")/../.."
[ -f Builds/Windows/Stratum.exe ] && [ -x Builds/Linux/Stratum.x86_64 ] || { echo "build both players first" >&2; exit 1; }
out="Builds/release"
rm -rf "$out" && mkdir -p "$out"

# Windows installer and a zip for anyone who'd rather not install.
cp Tools/brand/stratum.ico Tools/release/stratum.ico
makensis -V2 -DVERSION="$V" -DSRC="$PWD/Builds/Windows" -DOUT="$PWD/$out/Stratum-$V-Setup.exe" Tools/release/stratum.nsi
rm Tools/release/stratum.ico
(cd Builds/Windows && zip -qr "../../$out/Stratum-$V-Windows.zip" . -x "*_DoNotShip*")

# Linux: the game, its icon and the installer in one folder.
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
mkdir "$stage/Stratum-$V"
cp -R Builds/Linux/. "$stage/Stratum-$V/"
rm -rf "$stage/Stratum-$V"/*_DoNotShip
cp Tools/brand/stratum-256.png "$stage/Stratum-$V/stratum.png"
cp Tools/release/install.sh "$stage/Stratum-$V/install.sh"
chmod +x "$stage/Stratum-$V/install.sh" "$stage/Stratum-$V/Stratum.x86_64"
tar -czf "$out/Stratum-$V-Linux.tar.gz" -C "$stage" "Stratum-$V"
cp Tools/release/install.sh "$out/install.sh"

(cd "$out" && sha256sum Stratum-* > SHA256SUMS.txt)
ls -la "$out"
