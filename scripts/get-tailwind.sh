#!/bin/sh
# Downloads the standalone Tailwind CLI into tools/tailwind/.
#
# Standalone rather than npm so a checkout needs nothing but the .NET SDK — no package.json, no
# node_modules. The binary is git-ignored; this script is the record of which one to fetch.
#
# Pinned on purpose (C9): a styling toolchain that silently moves underneath you produces visual
# diffs nobody asked for. Bump TAILWIND_VERSION deliberately.
set -eu

TAILWIND_VERSION="v4.3.4"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TARGET_DIR="$ROOT/tools/tailwind"
TARGET="$TARGET_DIR/tailwindcss"

case "$(uname -s)" in
  Darwin) OS="macos" ;;
  Linux)  OS="linux" ;;
  *)      echo "Unsupported OS: $(uname -s). Download the Windows build manually into $TARGET_DIR." >&2
          exit 1 ;;
esac

case "$(uname -m)" in
  arm64|aarch64) ARCH="arm64" ;;
  x86_64|amd64)  ARCH="x64" ;;
  *)             echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

ASSET="tailwindcss-${OS}-${ARCH}"
URL="https://github.com/tailwindlabs/tailwindcss/releases/download/${TAILWIND_VERSION}/${ASSET}"

if [ -x "$TARGET" ] && "$TARGET" --help >/dev/null 2>&1; then
  echo "Tailwind CLI already present at $TARGET"
  exit 0
fi

mkdir -p "$TARGET_DIR"
echo "Downloading ${ASSET} ${TAILWIND_VERSION}…"
curl -sfL "$URL" -o "$TARGET"
chmod +x "$TARGET"

echo "$TAILWIND_VERSION" > "$TARGET_DIR/.version"
echo "Tailwind CLI ready: $TARGET"
