#!/usr/bin/env bash
# Publish Builds/{Android,Windows,macOS,Linux} to itch.io via Butler.
# Target: yevons/stellar-universe. The numeric id 861522 is rejected by the itch API.
# A channel is skipped when its folder has nothing to ship.
# The itch build number is automatic. --userversion is the label players see:
# ProjectSettings bundleVersion, unless a version is passed on the command line.
#   ./scripts/deploy.sh                 # all four, bundleVersion
#   ./scripts/deploy.sh android         # one channel
#   ./scripts/deploy.sh 1.1             # all four, this label
#   ./scripts/deploy.sh android 1.1     # one channel, this label
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

GAME_ID="yevons/stellar-universe"

if command -v butler >/dev/null 2>&1; then
  BUTLER="$(command -v butler)"
elif [[ -x "$HOME/Library/Application Support/itch/apps/butler/butler" ]]; then
  BUTLER="$HOME/Library/Application Support/itch/apps/butler/butler"
else
  echo "Butler introuvable. Installe-le avec l'app itch.io, ou ajoute-le au PATH." >&2
  exit 1
fi

# folder|channel
ALL=(
  "Builds/Android|android"
  "Builds/Windows|windows"
  "Builds/macOS|macos"
  "Builds/Linux|linux"
)

is_target() {
  case "$1" in
    android|windows|macos|linux|Android|Windows|macOS|Linux) return 0 ;;
    *) return 1 ;;
  esac
}

want=""
version=""
if [[ $# -ge 1 ]]; then
  if is_target "$1"; then
    want="$1"
    version="${2:-}"
    if [[ $# -gt 2 ]]; then
      echo "Usage : ./scripts/deploy.sh [canal] [version]" >&2
      exit 1
    fi
  elif [[ "$1" =~ ^[0-9] ]]; then
    version="$1"
    if [[ $# -gt 1 ]]; then
      echo "Usage : ./scripts/deploy.sh [canal] [version]" >&2
      exit 1
    fi
  else
    echo "Canal inconnu : $1 (android, windows, macos, linux)" >&2
    exit 1
  fi
fi

if [[ -z "$version" ]]; then
  line="$(grep -m1 '^  bundleVersion:' ProjectSettings/ProjectSettings.asset || true)"
  version="${line#*: }"
  version="${version// /}"
fi
if [[ -z "$version" ]]; then
  echo "Version manquante. Passe-la en argument, ou renseigne bundleVersion." >&2
  exit 1
fi

# Unity drops these next to the player. They are not part of the game.
IGNORES=(
  ".DS_Store"
  "**/.DS_Store"
  ".gitkeep"
  "*BurstDebugInformation_DoNotShip"
  "*BurstDebugInformation_DoNotShip/**"
  "*BackUpThisFolder_ButDontShipItWithYourGame"
  "*BackUpThisFolder_ButDontShipItWithYourGame/**"
)
ignore_args=()
for pattern in "${IGNORES[@]}"; do
  ignore_args+=(--ignore "$pattern")
done

failed=0
pushed=0
matched=0

for entry in "${ALL[@]}"; do
  dir="${entry%%|*}"
  channel="${entry##*|}"
  if [[ -n "$want" && "$want" != "$channel" && "$want" != "$(basename "$dir")" ]]; then
    continue
  fi
  matched=1

  mkdir -p "$dir"

  if ! find "$dir" \
      \( -name '.gitkeep' -o -name '.DS_Store' \
         -o -name '*BurstDebugInformation_DoNotShip' \
         -o -name '*BackUpThisFolder_ButDontShipItWithYourGame' \) -prune \
      -o -type f -print -quit | grep -q .; then
    echo "$dir : vide, rien à envoyer."
    continue
  fi

  echo "Envoi de $dir vers ${GAME_ID}:${channel} (${version})"
  if "$BUTLER" push "$dir" "${GAME_ID}:${channel}" \
      --userversion "$version" \
      --assume-yes --fix-permissions --if-changed \
      "${ignore_args[@]}"; then
    pushed=$((pushed + 1))
  else
    echo "Échec : ${GAME_ID}:${channel}" >&2
    failed=1
  fi
done

if [[ "$matched" -eq 0 && -n "$want" ]]; then
  echo "Canal inconnu : $want (android, windows, macos, linux)" >&2
  exit 1
fi

exit "$failed"
