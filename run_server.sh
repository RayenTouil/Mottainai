#!/usr/bin/env bash
# Usage: ./run_server.sh [-headless] [extra args]
ROOT="$(cd "$(dirname "$0")" && pwd)"
case "$(uname -s)" in
  Linux*) EXE="$ROOT/Mottainai/Builds/Server/Linux/MottainaiServer.x86_64";;
  *) EXE="$ROOT/Mottainai/Builds/Server/Windows/MottainaiServer.exe";;
esac
[ -f "$EXE" ] || { echo "[Mottainai] Server not built. Run ./build_server.sh"; exit 1; }
MODE="-screen-fullscreen 0 -screen-width 1100 -screen-height 640"
if [ "$1" = "-headless" ]; then MODE="-batchmode -nographics"; shift; fi
exec "$EXE" -server $MODE -port 7777 -http 8080 -tick 60 -log -logFile - "$@"
