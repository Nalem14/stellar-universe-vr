#!/usr/bin/env bash
# Publish Builds/{Android,Windows,macOS,Linux} to itch.io via Butler.
# Target: game 861522. A channel is skipped when its folder has nothing to ship.
#   ./scripts/deploy.sh            # all four
#   ./scripts/deploy.sh android    # one channel
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

GAME_ID="861522"

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

want="${1:-}"
failed=0
pushed=0

for entry in "${ALL[@]}"; do
  dir="${entry%%|*}"
  channel="${entry##*|}"
  if [[ -n "$want" && "$want" != "$channel" && "$want" != "$(basename "$dir")" ]]; then
    continue
  fi

  mkdir -p "$dir"

  if ! find "$dir" -mindepth 1 ! -name '.gitkeep' ! -name '.DS_Store' -print -quit | grep -q .; then
    echo "$dir : vide, rien à envoyer."
    continue
  fi

  echo "Envoi de $dir vers ${GAME_ID}:${channel}"
  if "$BUTLER" push "$dir" "${GAME_ID}:${channel}" \
      --assume-yes --fix-permissions --if-changed \
      --ignore ".DS_Store" --ignore ".gitkeep" \
      --ignore "*BurstDebugInformation_DoNotShip"; then
    pushed=$((pushed + 1))
  else
    echo "Échec : ${GAME_ID}:${channel}" >&2
    failed=1
  fi
done

if [[ "$pushed" -eq 0 && "$failed" -eq 0 && -n "$want" ]]; then
  echo "Canal inconnu : $want (android, windows, macos, linux)" >&2
  exit 1
fi

exit "$failed"
