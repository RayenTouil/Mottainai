#!/usr/bin/env bash
# Builds from a mirror copy of the Unity project so the editor can stay open on the real one.
# Usage: tools/mirror_build.sh <BuildTools method> [extra Unity args]
#   tools/mirror_build.sh BuildServerWindows
#   tools/mirror_build.sh BuildAndroid -out "C:/.../Builds/Mottainai.apk"
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/Mottainai"
MIRROR="${MOTTAINAI_MIRROR:-$LOCALAPPDATA/MottainaiMirror}"
MIRROR="$(cygpath -u "$MIRROR")"
DST="$MIRROR/Mottainai"
METHOD="${1:?method required}"; shift
UVER="$(grep -m1 '^m_EditorVersion:' "$SRC/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')"
UNITY="${UNITY_EXE:-/c/Program Files/Unity/Hub/Editor/$UVER/Editor/Unity.exe}"
mkdir -p "$DST" "$ROOT/Builds"
for d in Assets Packages ProjectSettings; do
  robocopy "$(cygpath -w "$SRC/$d")" "$(cygpath -w "$DST/$d")" //MIR //NFL //NDL //NJH //NJS //NP //R:1 //W:1 >/dev/null
  [ $? -ge 8 ] && { echo "[mirror] robocopy $d failed"; exit 1; }
done
if [ ! -d "$DST/Library/PackageCache" ] && [ -d "$SRC/Library/PackageCache" ]; then
  robocopy "$(cygpath -w "$SRC/Library/PackageCache")" "$(cygpath -w "$DST/Library/PackageCache")" //E //NFL //NDL //NJH //NJS //NP //R:1 //W:1 >/dev/null
fi
LOG="$ROOT/Builds/mirror_$METHOD.log"
echo "[mirror] $METHOD in $DST (log: $LOG)"
"$UNITY" -batchmode -quit -projectPath "$(cygpath -w "$DST")" -executeMethod "Mottainai.EditorTools.BuildTools.$METHOD" -logFile "$(cygpath -w "$LOG")" "$@"
code=$?
grep -aE "error CS|Shader error|\[BuildTools\] .*(Succeeded|Failed)|What went wrong" "$LOG" | sort -u | head -20
exit $code
