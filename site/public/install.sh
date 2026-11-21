#!/bin/sh
# Installs Stratum for this user on Linux (no sudo): the game in ~/.local/share/stratum, a "stratum" command in
# ~/.local/bin and an entry in the applications menu. Run it from an extracted download, or straight from the site:
#   curl -fsSL https://stratum.rushingtechnologies.com/install.sh | sh
# Running it again updates the game; worlds (in ~/.config/unity3d/Rushing Technologies/Stratum) are kept.
# "sh install.sh --uninstall" takes it away again.
set -eu

SITE="https://stratum.rushingtechnologies.com"
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/stratum"
BIN="$HOME/.local/bin"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICONS="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"

if [ "${1:-}" = "--uninstall" ]; then
  rm -rf "$DEST" "$BIN/stratum" "$APPS/stratum.desktop" "$ICONS/stratum.png"
  echo "Stratum has been removed. Your worlds are still in ~/.config/unity3d/Rushing Technologies/Stratum."
  exit 0
fi

case "$(uname -m)" in x86_64|amd64) ;; *) echo "Stratum needs a 64-bit Intel or AMD computer." >&2; exit 1 ;; esac

here="$(cd "$(dirname "$0")" 2>/dev/null && pwd || echo .)"
tmp=""
if [ -f "$here/Stratum.x86_64" ] && [ -d "$here/Stratum_Data" ]; then
  src="$here"
else
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  echo "Downloading Stratum..."
  if command -v curl >/dev/null 2>&1; then curl -fL --progress-bar "$SITE/download/linux" -o "$tmp/stratum.tar.gz"
  else wget -q --show-progress -O "$tmp/stratum.tar.gz" "$SITE/download/linux"; fi
  # Check the download against the published checksum before trusting it.
  if command -v curl >/dev/null 2>&1; then sums="$(curl -fsSL "$SITE/download/sums" || true)"; else sums="$(wget -qO- "$SITE/download/sums" || true)"; fi
  want="$(printf '%s\n' "$sums" | awk '/Linux\.tar\.gz/ { print $1; exit }')"
  if command -v sha256sum >/dev/null 2>&1; then got="$(sha256sum "$tmp/stratum.tar.gz" | awk '{ print $1 }')"
  elif command -v shasum >/dev/null 2>&1; then got="$(shasum -a 256 "$tmp/stratum.tar.gz" | awk '{ print $1 }')"
  else got=""; fi
  if [ -z "$want" ] || [ -z "$got" ]; then
    echo "Couldn't check the download (no checksum list, or no sha256sum on this computer). Nothing was changed." >&2; exit 1
  fi
  if [ "$want" != "$got" ]; then
    echo "The download doesn't match its published checksum, so it wasn't installed. Nothing was changed. Try again." >&2; exit 1
  fi
  tar -xzf "$tmp/stratum.tar.gz" -C "$tmp"
  src="$(dirname "$(find "$tmp" -name Stratum.x86_64 | head -n 1)")"
fi

# The new copy must look whole before the old one is touched.
if [ ! -s "$src/Stratum.x86_64" ] || [ ! -d "$src/Stratum_Data" ] || [ ! -f "$src/UnityPlayer.so" ] || [ ! -d "$src/Stratum_Data/Managed" ]; then
  echo "The Stratum files are incomplete, so nothing was installed. Your current copy is untouched." >&2; exit 1
fi

echo "Installing to $DEST"
rm -rf "$DEST.new"
mkdir -p "$DEST.new"
cp -R "$src"/. "$DEST.new"/
chmod +x "$DEST.new/Stratum.x86_64"
if [ ! -s "$DEST.new/Stratum.x86_64" ] || [ ! -d "$DEST.new/Stratum_Data/Managed" ]; then
  rm -rf "$DEST.new"; echo "Copying the new version failed (is the disk full?). Your current copy is untouched." >&2; exit 1
fi
# Swap: the old copy steps aside, the new one takes its place, and only then is the old one removed. If the
# swap fails the old copy goes back.
rm -rf "$DEST.old"
if [ -d "$DEST" ]; then mv "$DEST" "$DEST.old"; fi
if ! mv "$DEST.new" "$DEST"; then
  [ -d "$DEST.old" ] && mv "$DEST.old" "$DEST"
  echo "Couldn't put the new version in place. Your previous copy has been put back." >&2; exit 1
fi
rm -rf "$DEST.old"

mkdir -p "$BIN" "$APPS" "$ICONS"
ln -sf "$DEST/Stratum.x86_64" "$BIN/stratum"
[ -f "$DEST/stratum.png" ] && cp "$DEST/stratum.png" "$ICONS/stratum.png"
cat > "$APPS/stratum.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Stratum
Comment=Dig, build and survive, alone or with friends
Exec="$DEST/Stratum.x86_64"
Path=$DEST
Icon=stratum
Terminal=false
Categories=Game;
EOF
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS" >/dev/null 2>&1 || true

echo "Done. Start Stratum from your applications menu, or type: stratum"
case ":$PATH:" in *":$BIN:"*) ;; *) echo "(Add $BIN to your PATH to use the stratum command.)" ;; esac
