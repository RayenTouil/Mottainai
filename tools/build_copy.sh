#!/usr/bin/env bash
# Builds from a mirrored copy of the project (.bc/Mottainai) so the Unity editor can stay open.
# First run imports the copy (slow, one time); later runs are incremental.
# Usage: tools/build_copy.sh [android|win] [-dev]   (android -> Builds/Mottainai.apk, win -> Builds/Win/Mottainai.exe)
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TARGET=android; case "$1" in android|win) TARGET=$1; shift;; esac
SRC="$ROOT/Mottainai"; DST="$ROOT/.bc/Mottainai"
UVER="$(grep -m1 '^m_EditorVersion:' "$SRC/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')"
UNITY="${UNITY_EXE:-/c/Program Files/Unity/Hub/Editor/$UVER/Editor/Unity.exe}"
mkdir -p "$DST" "$ROOT/Builds"
for d in Assets Packages ProjectSettings; do
  robocopy "$(cygpath -w "$SRC/$d")" "$(cygpath -w "$DST/$d")" //MIR //NFL //NDL //NJH //NJS //NP //R:1 //W:1 > /dev/null
  [ $? -ge 8 ] && { echo "robocopy $d failed"; exit 1; }
done
if [ "$TARGET" = win ]; then BT=Win64; M=BuildWindows; OUT="$ROOT/Builds/Win/Mottainai.exe"; else BT=Android; M=BuildAndroid; OUT="$ROOT/Builds/Mottainai.apk"; fi
LOG="$ROOT/Builds/build_copy_$TARGET.log"
echo "[build_copy] Unity $UVER $TARGET -> $OUT (log: $LOG)"
"$UNITY" -batchmode -quit -projectPath "$(cygpath -w "$DST")" -buildTarget $BT -executeMethod Mottainai.EditorTools.BuildTools.$M \
  -out "$(cygpath -w "$OUT")" -logFile "$(cygpath -w "$LOG")" "$@"
st=$?
grep -E "\[BuildTools\]|error CS|Build Finished|BuildFailedException|Shader error" "$LOG" | tail -8
[ $st -eq 0 ] && echo "[build_copy] OK" || echo "[build_copy] FAILED ($st)"
exit $st
